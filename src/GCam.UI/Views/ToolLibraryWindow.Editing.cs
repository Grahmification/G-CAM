using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GCam.Core.Diagnostics;
using GCam.Core.Tooling;
using GCam.Core.Tooling.Import;
using GCam.UI.ViewModels;

namespace GCam.UI.Views
{
    /// <summary>
    /// Creating, editing and deleting tools, and committing a session.
    /// </summary>
    /// <remarks>
    /// Split from the browsing half of the window so the two concerns stay readable -
    /// the same partial-class habit the add-in uses.
    ///
    /// Nothing here writes to disk. Every edit changes the in-memory library and marks
    /// it dirty; only OK commits, and only Cancel discards.
    /// </remarks>
    public partial class ToolLibraryWindow
    {
        /// <summary>True once the session was committed, so the caller knows it saved.</summary>
        public bool Committed { get; private set; }

        // ── New tool ─────────────────────────────────────────────────────────

        private void OnNewTool(object sender, RoutedEventArgs e)
        {
            if (!RequireEditableLibrary())
            {
                return;
            }

            // A type menu first, as HSMWorks does - the type decides which fields even
            // apply, so choosing it before the dialog opens saves a round trip.
            var menu = new ContextMenu { PlacementTarget = NewToolButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Top };

            foreach (ToolType type in Enum.GetValues(typeof(ToolType)).Cast<ToolType>())
            {
                ToolType captured = type;
                menu.Items.Add(new MenuItem
                {
                    Header = ToolSearch.DisplayName(captured),
                    Command = new RelayCommand(() => CreateTool(captured)),
                });
            }

            menu.IsOpen = true;
        }

        private void CreateTool(ToolType type)
        {
            ToolLibrary library = _model.CurrentLibrary;

            var tool = new Tool
            {
                Type = type,
                Number = library.NextToolNumber(),
                Geometry = DefaultGeometryFor(type),
                Cutting = new CuttingData { SpindleRpm = 5000, CuttingFeed = 500, PlungeFeed = 150 },
            };

            tool.Machine.DiameterOffset = tool.Number;
            tool.Machine.LengthOffset = tool.Number;

            if (ShowEditor(tool, isNew: true, out Tool edited))
            {
                library.Tools.Add(edited);
                _model.MarkCurrentDirty();
                _model.ReloadCurrentTools(edited);
            }
        }

        /// <summary>
        /// Plausible starting dimensions so the preview shows a tool rather than nothing.
        /// </summary>
        private static ToolGeometry DefaultGeometryFor(ToolType type)
        {
            var geometry = new ToolGeometry
            {
                Diameter = 6,
                FluteLength = 20,
                ShoulderLength = 20,
                BodyLength = 30,
                OverallLength = 50,
                ShankDiameter = 6,
                FluteCount = 2,
            };

            switch (type)
            {
                case ToolType.BallEndMill:
                    geometry.CornerRadius = 3;
                    break;
                case ToolType.BullNoseEndMill:
                    geometry.CornerRadius = 0.5;
                    break;
                case ToolType.Drill:
                    geometry.TipAngle = 118;
                    break;
                case ToolType.SpotDrill:
                case ToolType.ChamferMill:
                    geometry.TipAngle = 90;
                    break;
                case ToolType.Tap:
                    geometry.ThreadPitch = 1;
                    geometry.ThreadProfileAngle = 60;
                    break;
            }

            return geometry;
        }

        // ── Edit, duplicate, delete ──────────────────────────────────────────

        private void OnEditTool(object sender, RoutedEventArgs e) => EditSelectedTool();

        private void EditSelectedTool()
        {
            Tool selected = _model.SelectedTool;
            if (selected == null || !RequireEditableLibrary())
            {
                return;
            }

            if (ShowEditor(selected, isNew: false, out Tool edited))
            {
                ToolLibrary library = _model.CurrentLibrary;
                int index = library.Tools.IndexOf(selected);
                if (index >= 0)
                {
                    library.Tools[index] = edited;
                }

                _model.MarkCurrentDirty();
                _model.ReloadCurrentTools(edited);
            }
        }

        private void OnDuplicateTool(object sender, RoutedEventArgs e)
        {
            Tool selected = _model.SelectedTool;
            if (selected == null || !RequireEditableLibrary())
            {
                return;
            }

            ToolLibrary library = _model.CurrentLibrary;

            Tool copy = selected.CloneAsNew();
            copy.Number = library.NextToolNumber();
            copy.Name = string.IsNullOrWhiteSpace(selected.Name) ? null : selected.Name + " (copy)";

            if (ShowEditor(copy, isNew: true, out Tool edited))
            {
                library.Tools.Add(edited);
                _model.MarkCurrentDirty();
                _model.ReloadCurrentTools(edited);
            }
        }

        private void OnDeleteTool(object sender, RoutedEventArgs e)
        {
            Tool selected = _model.SelectedTool;
            if (selected == null || !RequireEditableLibrary())
            {
                return;
            }

            MessageBoxResult answer = MessageBox.Show(
                this,
                $"Delete {selected}?" + Environment.NewLine + Environment.NewLine +
                "The library is not written until you click OK.",
                "G-CAM",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);

            if (answer != MessageBoxResult.OK)
            {
                return;
            }

            _model.CurrentLibrary.Tools.Remove(selected);
            _model.MarkCurrentDirty();
            _model.ReloadCurrentTools();
        }

        private bool ShowEditor(Tool tool, bool isNew, out Tool edited)
        {
            var model = new ToolEditorViewModel(tool, _model.CurrentLibrary, isNew);
            var dialog = new ToolEditorWindow(model) { Owner = this };

            if (dialog.ShowDialog() == true)
            {
                edited = dialog.Result;
                return true;
            }

            edited = null;
            return false;
        }

        // ── Context menu ─────────────────────────────────────────────────────

        /// <summary>
        /// Selects the row under the pointer before the menu opens, so the commands act
        /// on what was right-clicked rather than on whatever was selected before.
        /// </summary>
        private void OnRowRightClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListViewItem item)
            {
                item.IsSelected = true;
                item.Focus();
            }
        }

        private void OnContextMenuOpened(object sender, RoutedEventArgs e)
        {
            bool editable = _model.CanEditCurrent && _model.SelectedTool != null;

            EditToolItem.IsEnabled = editable;
            DuplicateToolItem.IsEnabled = editable;
            DeleteToolItem.IsEnabled = editable;

            BuildCopyToLibraryMenu();
        }

        /// <summary>
        /// Fills the "Copy to library" submenu with the other writable libraries open in
        /// this session.
        /// </summary>
        /// <remarks>
        /// Only libraries already opened this session appear: copying into one that has
        /// never been loaded would mean reading it behind the user's back, and would put
        /// a library they have not looked at into the unsaved set.
        /// </remarks>
        private void BuildCopyToLibraryMenu()
        {
            CopyToLibraryItem.Items.Clear();

            List<ToolLibrary> targets = _model.Session.WritableOpenLibraries
                .Where(l => !ReferenceEquals(l, _model.CurrentLibrary))
                .ToList();

            CopyToLibraryItem.IsEnabled = _model.SelectedTool != null && targets.Count > 0;

            if (targets.Count == 0)
            {
                CopyToLibraryItem.Items.Add(new MenuItem
                {
                    Header = "No other G-CAM library open",
                    IsEnabled = false,
                });
                return;
            }

            foreach (ToolLibrary target in targets)
            {
                ToolLibrary captured = target;
                CopyToLibraryItem.Items.Add(new MenuItem
                {
                    Header = captured.Name ?? Path.GetFileName(captured.SourcePath),
                    Command = new RelayCommand(() => CopyToolTo(captured)),
                });
            }
        }

        private void CopyToolTo(ToolLibrary target)
        {
            Tool selected = _model.SelectedTool;
            if (selected == null)
            {
                return;
            }

            Tool copy = selected.CloneAsNew();
            copy.Number = target.NextToolNumber();

            // Bring the holder across, sharing the target's copy if it already has one.
            if (copy.Holder != null)
            {
                Holder existing = target.FindHolderById(copy.Holder.Id);
                if (existing == null)
                {
                    existing = copy.Holder.Clone();
                    target.Holders.Add(existing);
                }

                copy.Holder = existing;
            }

            target.Tools.Add(copy);
            _model.Session.MarkDirty(target.SourcePath);
            _model.RefreshDirtyMarkers();

            SetStatus($"Copied {selected} to {target.Name}.");
        }

        // ── New library, and converting a read-only one ──────────────────────

        private void OnNewLibrary(object sender, RoutedEventArgs e)
        {
            string path = AskForLibraryPath("New tool library", "NewLibrary");
            if (path == null)
            {
                return;
            }

            // Written straight away: the user chose a path, and a library that exists
            // only in memory would not appear in the folder tree.
            if (!TryWrite(() => _model.Session.CreateNew(path, Path.GetFileNameWithoutExtension(path))))
            {
                return;
            }

            _model.RefreshAndSelect(path);
            SetStatus($"Created {Path.GetFileName(path)}.");
        }

        private void OnSaveAsGcamLibrary(object sender, RoutedEventArgs e)
        {
            if (_model.CurrentLibrary == null)
            {
                SetStatus("Select a library first.");
                return;
            }

            string suggested = Path.GetFileNameWithoutExtension(_model.CurrentLibrary.SourcePath ?? "Library");
            string path = AskForLibraryPath("Save as G-CAM library", suggested);
            if (path == null)
            {
                return;
            }

            ToolLibrary source = _model.CurrentLibrary;

            if (!TryWrite(() => _model.Session.SaveAsCopy(source, path, source.Name)))
            {
                return;
            }

            _model.RefreshAndSelect(path);
            SetStatus($"Saved an editable copy as {Path.GetFileName(path)}.");
        }

        /// <summary>
        /// Runs an action that writes a file, reporting failure rather than letting it
        /// escape. Creating a library happens immediately, so it can fail immediately -
        /// a read-only share or a path the user cannot write.
        /// </summary>
        private bool TryWrite(Action write)
        {
            try
            {
                write();
                return true;
            }
            catch (GCamUserException ex)
            {
                MessageBox.Show(this, ex.Message, "G-CAM", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }

        private string AskForLibraryPath(string title, string suggestedName)
        {
            using (var dialog = new System.Windows.Forms.SaveFileDialog())
            {
                dialog.Title = title;
                dialog.Filter = "G-CAM tool library (*" + GcamXmlLibrary.FileExtension + ")|*" +
                                GcamXmlLibrary.FileExtension;
                dialog.DefaultExt = GcamXmlLibrary.FileExtension;
                dialog.FileName = suggestedName + GcamXmlLibrary.FileExtension;
                dialog.OverwritePrompt = true;

                // Default to the folder of whatever is selected, which is nearly always
                // where the new library belongs.
                string startIn = SelectedFolderPath();
                if (startIn != null)
                {
                    dialog.InitialDirectory = startIn;
                }

                return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dialog.FileName : null;
            }
        }

        private string SelectedFolderPath()
        {
            switch (FolderTree.SelectedItem)
            {
                case FolderNode folder:
                    return folder.FullPath;
                case LibraryFileNode file:
                    return Path.GetDirectoryName(file.FullPath);
                default:
                    return (FolderTree.Items.Count > 0 ? FolderTree.Items[0] as FolderNode : null)?.FullPath;
            }
        }

        private bool RequireEditableLibrary()
        {
            if (_model.CurrentLibrary == null)
            {
                SetStatus("Select a library first.");
                return false;
            }

            if (!_model.CanEditCurrent)
            {
                MessageBox.Show(
                    this,
                    "Imported libraries are read-only." + Environment.NewLine + Environment.NewLine +
                    "G-CAM can read HSMWorks libraries but cannot write them. Use " +
                    "\"Save as G-CAM library…\" to make an editable copy.",
                    "G-CAM",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return false;
            }

            return true;
        }

        // ── Commit ───────────────────────────────────────────────────────────

        private void OnOk(object sender, RoutedEventArgs e)
        {
            IReadOnlyList<string> failures = _model.Session.SaveAll();

            if (failures.Count > 0)
            {
                // Some libraries may have saved; say so rather than implying nothing did.
                MessageBox.Show(
                    this,
                    "Some libraries could not be saved:" + Environment.NewLine + Environment.NewLine +
                    string.Join(Environment.NewLine, failures),
                    "G-CAM",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                _model.RefreshDirtyMarkers();
                return;
            }

            Committed = true;
            _closing = true;
            Close();
        }

        /// <summary>
        /// Cancel simply closes. Prompting and discarding belong to OnClosing, which is
        /// the one path every route out of the window goes through - the button, the
        /// title-bar cross and Escape alike.
        /// </summary>
        private void OnCancel(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>
        /// Asks before throwing away unsaved work. Returns false to stay open.
        /// </summary>
        private bool ConfirmDiscard()
        {
            if (!_model.Session.HasUnsavedChanges)
            {
                return true;
            }

            int count = _model.Session.DirtyPaths.Count;
            string what = count == 1 ? "1 library" : count + " libraries";

            return MessageBox.Show(
                this,
                $"Discard changes to {what}?",
                "G-CAM",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning) == MessageBoxResult.OK;
        }

        private void SetStatus(string text)
        {
            _model.SetStatus(text);
        }
    }

    /// <summary>Minimal ICommand, for menu items built in code.</summary>
    internal sealed class RelayCommand : ICommand
    {
        private readonly Action _execute;

        public RelayCommand(Action execute) => _execute = execute;

        public event EventHandler CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object parameter) => true;

        public void Execute(object parameter) => _execute();
    }
}
