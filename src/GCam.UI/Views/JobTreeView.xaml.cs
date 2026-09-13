using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GCam.Core.Diagnostics;
using GCam.UI.ViewModels;

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
    /// Menu items are named and wired to Click handlers, with enablement set in
    /// <see cref="OnContextMenuOpened"/> - the same arrangement as the tool library
    /// browser, rather than viewmodel commands, of which this codebase has none.
    ///
    /// Every handler here is an entry point in the sense of docs/error-handling.md: WPF
    /// calls them, so none of them may let an exception escape.
    /// </remarks>
    public partial class JobTreeView : UserControl
    {
        private JobTreeViewModel _model;
        private ErrorHandler _errors;

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
        private void OnNodeRightClick(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (sender is TreeViewItem item)
                {
                    item.IsSelected = true;
                    item.Focus();

                    // Without this the click keeps bubbling to the parent item and the
                    // outermost node wins instead of the one under the cursor.
                    e.Handled = true;
                }
            }
            catch (Exception ex)
            {
                Handle(ex, nameof(OnNodeRightClick));
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

        private void OnContextMenuOpened(object sender, RoutedEventArgs e)
        {
            try
            {
                JobNode job = _model?.SelectedJobNode;
                bool onJob = job != null;

                EditJobItem.IsEnabled = onJob;
                RenameItem.IsEnabled = onJob;
                NewOperationItem.IsEnabled = onJob;
                DuplicateItem.IsEnabled = onJob;
                DeleteItem.IsEnabled = onJob;

                // Nothing to do for a job that is already the default.
                MakeDefaultItem.IsEnabled = onJob && !job.IsDefault;
            }
            catch (Exception ex)
            {
                Handle(ex, nameof(OnContextMenuOpened));
            }
        }

        private void OnEditJob(object sender, RoutedEventArgs e)
        {
            try
            {
                _model?.EditJob(_model.SelectedJobNode);
            }
            catch (Exception ex)
            {
                Handle(ex, nameof(OnEditJob));
            }
        }

        private void OnNewOperation(object sender, RoutedEventArgs e)
        {
            try
            {
                _model?.NewOperation(_model.SelectedJobNode);
            }
            catch (Exception ex)
            {
                Handle(ex, nameof(OnNewOperation));
            }
        }

        private void OnDuplicate(object sender, RoutedEventArgs e)
        {
            try
            {
                _model?.Duplicate(_model.SelectedJobNode);
            }
            catch (Exception ex)
            {
                Handle(ex, nameof(OnDuplicate));
            }
        }

        private void OnMakeDefault(object sender, RoutedEventArgs e)
        {
            try
            {
                _model?.MakeDefault(_model.SelectedJobNode);
            }
            catch (Exception ex)
            {
                Handle(ex, nameof(OnMakeDefault));
            }
        }

        private void OnDelete(object sender, RoutedEventArgs e)
        {
            try
            {
                DeleteSelected();
            }
            catch (Exception ex)
            {
                Handle(ex, nameof(OnDelete));
            }
        }

        private void DeleteSelected()
        {
            JobNode job = _model?.SelectedJobNode;
            if (job == null)
            {
                return;
            }

            int operations = _model.OperationCount(job);
            string question = operations == 0
                ? $"Delete {job.Name}?"
                : $"Delete {job.Name} and its {operations} operation{(operations == 1 ? string.Empty : "s")}?";

            MessageBoxResult answer = MessageBox.Show(
                question,
                "G-CAM",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);

            if (answer == MessageBoxResult.OK)
            {
                _model.Delete(job);
            }
        }

        // ---- Rename ----------------------------------------------------------

        private void OnRename(object sender, RoutedEventArgs e)
        {
            try
            {
                BeginRename();
            }
            catch (Exception ex)
            {
                Handle(ex, nameof(OnRename));
            }
        }

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

        private void CommitRename(TextBox box)
        {
            if (!(box.DataContext is JobNode node))
            {
                return;
            }

            // Leave edit mode first. Renaming can fail, and the box has to be gone
            // before a modal message appears or the focus handler re-enters this.
            node.IsEditing = false;

            try
            {
                _model.Rename(node, box.Text);
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
