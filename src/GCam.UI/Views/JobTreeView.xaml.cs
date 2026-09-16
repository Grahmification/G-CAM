using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using GCam.Core.Diagnostics;
using GCam.UI.ViewModels;
using WinForms = System.Windows.Forms;

namespace GCam.UI.Views
{
    /// <summary>
    /// Tree of jobs and operations, hosted in the SOLIDWORKS Manager Pane.
    /// </summary>
    /// <remarks>
    /// Keeps a parameterless constructor: SOLIDWORKS activates the hosting control
    /// through COM, so nothing can be injected here. The viewmodel arrives afterwards
    /// through <see cref="Bind"/>.
    ///
    /// The context menu is WinForms rather than WPF. That was adopted while chasing a
    /// crash which turned out to have nothing to do with it - the cause was
    /// IPropertyManagerPageControl.Visible, see
    /// docs/solidworks-api/property-manager-pages.md - so a WPF ContextMenu would very
    /// probably work here now. It is left as it is because it works and is proven;
    /// switching back is a safe thing to try, not a fix for anything.
    ///
    /// Enablement is set while the menu is built, rather than through viewmodel
    /// commands, of which this codebase has none.
    ///
    /// Every handler here is an entry point in the sense of docs/error-handling.md: WPF
    /// calls them, so none of them may let an exception escape.
    /// </remarks>
    public partial class JobTreeView : UserControl
    {
        private JobTreeViewModel _model;
        private ErrorHandler _errors;
        private WinForms.ContextMenuStrip _nodeMenu;

        public JobTreeView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Gives the view its document. Called once the hosting control has been
        /// activated by SOLIDWORKS.
        /// </summary>
        public void Bind(JobTreeViewModel model, ErrorHandler errors = null)
        {
            _model = model;
            _errors = errors;
            DataContext = model;
        }

        private void Handle(Exception ex, string context)
        {
            if (_errors != null)
            {
                _errors.Handle(ex, "JobTreeView." + context);
                return;
            }

            // No error handler was supplied - better a message than a silent failure or
            // an exception crossing back into WPF's dispatcher.
            MessageBox.Show(
                ex is GCamUserException ? ex.Message : "G-CAM hit a problem: " + ex.Message,
                "G-CAM",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        // ---- Selection -------------------------------------------------------

        private void OnTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            try
            {
                if (_model != null)
                {
                    _model.SelectedNode = e.NewValue as JobTreeNode;
                }
            }
            catch (Exception ex)
            {
                Handle(ex, nameof(OnTreeSelectionChanged));
            }
        }

        /// <summary>
        /// Selects the row under the cursor before the context menu opens, so the menu
        /// acts on what was right-clicked rather than on whatever was selected before.
        /// </summary>
        /// <remarks>
        /// <b>The row comes from <see cref="RoutedEventArgs.OriginalSource"/>, never from
        /// <c>sender</c>.</b> This is wired to <c>PreviewMouseRightButtonDown</c>, which
        /// *tunnels* - it runs from the root of the tree downwards, so an operation's
        /// parent job gets the handler first and <c>e.Handled</c> then stops the tunnel
        /// before the operation's own item is ever reached. Trusting <c>sender</c>
        /// therefore selects the outermost node, which is the exact opposite of what this
        /// method is for: right-clicking an operation jumped the selection up to its job
        /// and gave you the job's menu.
        ///
        /// <c>OriginalSource</c> is the element the input system hit-tested, whichever way
        /// the event is routed, so the deepest <see cref="TreeViewItem"/> above it is the
        /// row under the cursor. That holds if this is ever moved to the bubbling event or
        /// onto the <see cref="TreeView"/> itself.
        /// </remarks>
        private void OnNodeRightClick(object sender, MouseButtonEventArgs e)
        {
            try
            {
                TreeViewItem item = RowUnder(e.OriginalSource as DependencyObject);

                if (item != null)
                {
                    item.IsSelected = true;
                    item.Focus();

                    // The row is chosen; nothing above it needs to see this.
                    e.Handled = true;
                }
            }
            catch (Exception ex)
            {
                Handle(ex, nameof(OnNodeRightClick));
            }
        }

        /// <summary>
        /// The innermost tree row containing the clicked element, or null.
        /// </summary>
        /// <remarks>
        /// A job's row contains its operations' rows, so the *innermost* one is always the
        /// row that was actually clicked - the job's own label belongs to the job's row and
        /// to nothing below it.
        ///
        /// The walk steps through the logical tree wherever the visual one runs out, which
        /// is what carries it past a <c>Run</c> or any other content element that is not a
        /// <see cref="Visual"/>.
        /// </remarks>
        private static TreeViewItem RowUnder(DependencyObject clicked)
        {
            while (clicked != null && !(clicked is TreeViewItem))
            {
                clicked = clicked is Visual || clicked is Visual3D
                    ? VisualTreeHelper.GetParent(clicked)
                    : LogicalTreeHelper.GetParent(clicked);
            }

            return clicked as TreeViewItem;
        }

        /// <summary>Double-click opens the job's page - the usual CAM gesture.</summary>
        private void OnNodeDoubleClick(object sender, MouseButtonEventArgs e)
        {
            try
            {
                _model?.EditSelected();
                e.Handled = true;
            }
            catch (Exception ex)
            {
                Handle(ex, nameof(OnNodeDoubleClick));
            }
        }

        private void OnTreeKeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                if (e.Key == Key.F2)
                {
                    BeginRename();
                    e.Handled = true;
                }
                else if (e.Key == Key.Enter)
                {
                    // Whatever is selected, the same as double-click - an operation node
                    // opened its job's page before operations were editable.
                    _model?.EditSelected();
                    e.Handled = true;
                }
                else if (e.Key == Key.Delete)
                {
                    DeleteSelected();
                    e.Handled = true;
                }
            }
            catch (Exception ex)
            {
                Handle(ex, nameof(OnTreeKeyDown));
            }
        }

        // ---- Context menu ----------------------------------------------------

        /// <summary>
        /// Shows the node menu on right-button *up*, once the selection made on the way
        /// down has settled.
        /// </summary>
        private void OnNodeRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            try
            {
                ShowNodeMenu();
                e.Handled = true;
            }
            catch (Exception ex)
            {
                Handle(ex, nameof(OnNodeRightButtonUp));
            }
        }

        /// <summary>
        /// Builds and shows the menu for whichever kind of node is selected.
        /// </summary>
        /// <remarks>
        /// Two menus rather than one that hides rows, because the two nodes share almost
        /// nothing: Make Default and New Operation mean nothing on an operation, and
        /// Suppress means nothing on a job.
        /// </remarks>
        private void ShowNodeMenu()
        {
            // Rebuilt per click. The menu is small, and building it fresh is how
            // enablement stays honest without a separate Opened handler.
            _nodeMenu?.Dispose();
            _nodeMenu = new WinForms.ContextMenuStrip();

            OperationNode operation = _model?.SelectedOperationNode;

            if (operation != null)
            {
                BuildOperationMenu(operation);
            }
            else
            {
                JobNode job = _model?.SelectedJobNode;
                if (job == null)
                {
                    return;
                }

                BuildJobMenu(job);
            }

            _nodeMenu.Show(WinForms.Control.MousePosition);
        }

        private void BuildJobMenu(JobNode job)
        {
            AddMenuItem("Edit…", () => _model.EditJob(job));
            AddMenuItem("Rename", BeginRename, shortcut: "F2");
            _nodeMenu.Items.Add(new WinForms.ToolStripSeparator());
            AddMenuItem("New Operation…", () => _model.NewOperation(job));
            AddMenuItem(
                "Generate",
                () => _model.GenerateJob(job),
                enabled: job.Job != null && job.Job.Operations.Count > 0);
            _nodeMenu.Items.Add(new WinForms.ToolStripSeparator());
            AddMenuItem("Duplicate", () => _model.Duplicate(job));

            // Nothing to do for a job that is already the default.
            AddMenuItem("Make Default", () => _model.MakeDefault(job), enabled: !job.IsDefault);
            AddMenuItem("Delete", DeleteSelected, shortcut: "Del");
        }

        /// <summary>
        /// The operation menu, in the same order as the job one so the two read alike.
        /// </summary>
        /// <remarks>
        /// Generate is greyed out on a suppressed operation because
        /// <see cref="GCam.Core.Generation.GenerationQueue"/> skips one - offering a
        /// command that is guaranteed to do nothing is worse than not offering it.
        /// </remarks>
        private void BuildOperationMenu(OperationNode operation)
        {
            bool enabled = operation.Operation?.Enabled ?? true;

            AddMenuItem("Edit…", () => _model.EditSelected());
            AddMenuItem("Rename", BeginRename, shortcut: "F2");
            _nodeMenu.Items.Add(new WinForms.ToolStripSeparator());
            AddMenuItem(
                "Generate", () => _model.GenerateOperation(operation), enabled: enabled);
            AddMenuItem(
                "Suppress",
                () => _model.SetOperationEnabled(operation, !operation.Operation.Enabled),
                isChecked: !enabled);
            _nodeMenu.Items.Add(new WinForms.ToolStripSeparator());
            AddMenuItem("Duplicate", () => _model.DuplicateOperation(operation));
            AddMenuItem("Delete", DeleteSelected, shortcut: "Del");
        }

        private void AddMenuItem(
            string text,
            Action action,
            bool enabled = true,
            string shortcut = null,
            bool isChecked = false)
        {
            var item = new WinForms.ToolStripMenuItem(text)
            {
                Enabled = enabled,
                ShortcutKeyDisplayString = shortcut,
                Checked = isChecked,
            };

            // Entry point: WinForms raises this, so nothing may escape.
            item.Click += (sender, args) =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Handle(ex, "menu:" + text);
                }
            };

            _nodeMenu.Items.Add(item);
        }

        /// <summary>
        /// Deletes the node the user is on, after asking.
        /// </summary>
        /// <remarks>
        /// <b>An operation deletes the operation, not its job.</b> Everywhere else an
        /// operation node stands in for its parent - New Operation, the 3D preview - and
        /// this is the one place that would be destructive, because Delete on an operation
        /// used to take the whole job with it.
        ///
        /// Both kinds ask first. There is no undo in G-CAM yet - <c>Core/Commands</c> is
        /// unbuilt - so a delete is final, and it takes a generated toolpath with it.
        /// </remarks>
        private void DeleteSelected()
        {
            OperationNode operation = _model?.SelectedOperationNode;

            if (operation != null)
            {
                if (Ask($"Delete {operation.Name}?"))
                {
                    _model.DeleteOperation(operation);
                }

                return;
            }

            JobNode job = _model?.SelectedJobNode;
            if (job == null)
            {
                return;
            }

            int operations = _model.OperationCount(job);
            string question = operations == 0
                ? $"Delete {job.Name}?"
                : $"Delete {job.Name} and its {operations} operation{(operations == 1 ? string.Empty : "s")}?";

            if (Ask(question))
            {
                _model.Delete(job);
            }
        }

        private static bool Ask(string question)
        {
            return MessageBox.Show(
                       question,
                       "G-CAM",
                       MessageBoxButton.OKCancel,
                       MessageBoxImage.Question) == MessageBoxResult.OK;
        }

        // ---- Rename ----------------------------------------------------------

        private void BeginRename()
        {
            JobTreeNode node = _model?.SelectedNode;
            if (node != null && node.CanRename)
            {
                node.IsEditing = true;
            }
        }

        /// <summary>
        /// Puts the caret in the box as soon as it appears. Without this the user has to
        /// click the box they just asked for.
        /// </summary>
        private void OnRenameBoxVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            try
            {
                if (sender is TextBox box && box.IsVisible)
                {
                    box.Focus();
                    box.SelectAll();
                }
            }
            catch (Exception ex)
            {
                Handle(ex, nameof(OnRenameBoxVisibleChanged));
            }
        }

        private void OnRenameKeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                if (!(sender is TextBox box))
                {
                    return;
                }

                if (e.Key == Key.Enter)
                {
                    CommitRename(box);
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    CancelRename(box);
                    e.Handled = true;
                }
            }
            catch (Exception ex)
            {
                Handle(ex, nameof(OnRenameKeyDown));
            }
        }

        private void OnRenameLostFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            try
            {
                // Clicking away commits, matching the SOLIDWORKS feature tree. A rejected
                // name would otherwise be lost silently along with the focus.
                if (sender is TextBox box && box.IsVisible)
                {
                    CommitRename(box);
                }
            }
            catch (Exception ex)
            {
                Handle(ex, nameof(OnRenameLostFocus));
            }
        }

        /// <summary>
        /// Commits an in-place rename of whichever kind of node is being edited.
        /// </summary>
        /// <remarks>
        /// Both kinds fail the same way - a blank name, or one already taken - and both
        /// are reported and returned to edit mode rather than silently discarded.
        /// </remarks>
        private void CommitRename(TextBox box)
        {
            if (!(box.DataContext is JobTreeNode node))
            {
                return;
            }

            // Leave edit mode first. Renaming can fail, and the box has to be gone
            // before a modal message appears or the focus handler re-enters this.
            node.IsEditing = false;

            try
            {
                switch (node)
                {
                    case JobNode job:
                        _model.Rename(job, box.Text);
                        break;

                    case OperationNode operation:
                        _model.RenameOperation(operation, box.Text);
                        break;
                }
            }
            catch (GCamUserException ex)
            {
                MessageBox.Show(ex.Message, "G-CAM", MessageBoxButton.OK, MessageBoxImage.Information);
                node.IsEditing = true;
            }
        }

        private static void CancelRename(TextBox box)
        {
            if (box.DataContext is JobTreeNode node)
            {
                node.IsEditing = false;

                // The box binds one-way, so abandoning it is enough to discard the edit.
            }
        }
    }
}
