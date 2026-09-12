using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using GCam.Core.Tooling;
using GCam.UI.ViewModels;

namespace GCam.UI.Views
{
    /// <summary>
    /// Browser for tool libraries: watched folders on the left, the selected library's
    /// tools in the middle, and the selected tool's profile below the tree.
    /// </summary>
    public partial class ToolLibraryWindow : Window
    {
        private readonly ToolLibraryBrowserViewModel _model;

        /// <summary>Set once OK or Cancel has decided what happens; stops OnClosing re-asking.</summary>
        private bool _closing;

        private GridViewColumnHeader _sortedHeader;
        private ListSortDirection _sortDirection = ListSortDirection.Ascending;

        public ToolLibraryWindow(ToolLibraryBrowserViewModel model)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));

            InitializeComponent();
            DataContext = _model;

            // Header clicks are wired in code: a GridView has no sort behaviour of its own.
            ToolList.AddHandler(
                GridViewColumnHeader.ClickEvent, new RoutedEventHandler(OnColumnHeaderClick));

            UpdateRemoveButton();
        }

        /// <summary>
        /// The tool the user last selected, or null. Set even when the window is used
        /// purely for browsing, so a future picker needs no new plumbing.
        /// </summary>
        public Tool SelectedTool => _model.SelectedTool;

        /// <summary>Raised when a tool row is double-clicked.</summary>
        public event EventHandler<Tool> ToolActivated;

        private void OnAddFolder(object sender, RoutedEventArgs e)
        {
            // WinForms' folder browser rather than a WPF one: WPF has no folder picker,
            // and GCam.UI already references WinForms for ElementHost.
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description =
                    "Choose a folder containing tool libraries (" + _model.SupportedFormats + ").";
                dialog.ShowNewFolderButton = false;

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    _model.AddFolder(dialog.SelectedPath);
                }
            }
        }

        private void OnRemoveFolder(object sender, RoutedEventArgs e)
        {
            if (FolderTree.SelectedItem is FolderNode node && node.IsRoot)
            {
                _model.RemoveFolder(node);
            }
        }

        private void OnRefresh(object sender, RoutedEventArgs e)
        {
            _model.Refresh();
        }

        private void OnTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            // Only library files load tools; selecting a folder just navigates.
            _model.SelectedLibrary = e.NewValue as LibraryFileNode;
            UpdateRemoveButton();
        }

        private void UpdateRemoveButton()
        {
            RemoveFolderButton.IsEnabled = FolderTree.SelectedItem is FolderNode node && node.IsRoot;
        }

        private void OnToolDoubleClick(object sender, MouseButtonEventArgs e)
        {
            Tool tool = _model.SelectedTool;
            if (tool == null)
            {
                return;
            }

            // When used as a picker, double-click chooses. Otherwise it edits, which is
            // what double-click means everywhere else in a list of editable things.
            if (ToolActivated != null)
            {
                ToolActivated.Invoke(this, tool);
                return;
            }

            EditSelectedTool();
        }

        /// <summary>
        /// Escape closes, as the Cancel button would.
        /// </summary>
        /// <remarks>
        /// Bubbling rather than preview, so a control that wants Escape for itself - a
        /// dropdown closing, an edit being abandoned - gets it first.
        /// </remarks>
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (!e.Handled && e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
                return;
            }

            base.OnKeyDown(e);
        }

        /// <summary>
        /// The single exit path: the Cancel button, the title-bar cross and Escape all
        /// arrive here, so the unsaved-changes question is asked exactly once.
        /// </summary>
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_closing && !ConfirmDiscard())
            {
                e.Cancel = true;
                return;
            }

            if (!_closing)
            {
                _model.Session.DiscardAll();
            }

            base.OnClosing(e);
        }

        /// <summary>
        /// Sorts the list by the clicked column, toggling direction on repeat clicks.
        /// </summary>
        private void OnColumnHeaderClick(object sender, RoutedEventArgs e)
        {
            if (!(e.OriginalSource is GridViewColumnHeader header) || header.Column == null)
            {
                return;
            }

            // The padding header at the right edge has no column content.
            if (!(header.Column.DisplayMemberBinding is Binding binding))
            {
                return;
            }

            string path = binding.Path?.Path;
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            _sortDirection = ReferenceEquals(header, _sortedHeader) && _sortDirection == ListSortDirection.Ascending
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;
            _sortedHeader = header;

            ICollectionView view = _model.Tools;
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription(path, _sortDirection));
            view.Refresh();
        }

    }
}
