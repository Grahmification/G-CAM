using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
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

        /// <summary>
        /// True while this view is moving WPF's own selection, so the change it provokes is
        /// not read back as the user asking for something.
        /// </summary>
        private bool _drivingSelection;

        /// <summary>
        /// The row a slow double-click would rename, and the wait that decides whether it
        /// was a slow double-click at all. Null between clicks.
        /// </summary>
        private JobTreeNode _renameRow;

        private DispatcherTimer _renameTimer;

        /// <summary>
        /// Changes the selection, ignoring whatever the tree says about it while the change
        /// is being made.
        /// </summary>
        /// <remarks>
        /// <b>This is what stops a selection change re-entering the one it came from, and
        /// that is a crash, not an untidiness.</b> Changing the selection writes
        /// <c>IsCurrent</c> back onto the rows, which is <c>TreeViewItem.IsSelected</c>,
        /// which makes the <see cref="TreeView"/> raise <c>SelectedItemChanged</c> - inside
        /// the handler that is already running, and from there into the 3D preview and its
        /// SOLIDWORKS COM calls. Shift-clicking a row <i>above</i> the anchor took
        /// SOLIDWORKS down that way; clicking below it did not, because only the upward
        /// range reported a change it had not made.
        ///
        /// That false positive is fixed where it belongs, in
        /// <c>MultiSelection.ExtendTo</c>. This is the guard that makes the re-entry
        /// harmless whatever else learns to report one - every route into the selection goes
        /// through here.
        /// </remarks>
        private void Apply(Action change)
        {
            if (_drivingSelection)
            {
                return;
            }

            _drivingSelection = true;

            try
            {
                change();
            }
            finally
            {
                _drivingSelection = false;
            }
        }

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

        /// <summary>
        /// Applies the click, Ctrl-click and Shift-click rules to the row under the cursor.
        /// </summary>
        /// <remarks>
        /// <b>The row comes from <see cref="RoutedEventArgs.OriginalSource"/>, never from
        /// <c>sender</c></b> - see the remarks on <see cref="OnNodeRightClick"/>, which is
        /// where that was learned.
        ///
        /// <b>Wired to the TreeView, not to each row</b>, and that is not a tidiness
        /// choice. <c>PreviewMouseLeftButtonDown</c> tunnels, so a handler on the rows runs
        /// once for an operation's own row and once for the job's row above it - and a
        /// Ctrl-click that toggles twice selects nothing at all. One subscription on the
        /// tree runs once, whatever was clicked.
        ///
        /// Deliberately not marked handled: WPF still moves its own single selection to the
        /// clicked row, which is what carries the focus and the keyboard with it.
        /// <see cref="OnTreeSelectionChanged"/> knows to leave the result alone.
        /// </remarks>
        private void OnNodeLeftClick(object sender, MouseButtonEventArgs e)
        {
            try
            {
                JobTreeNode node = NodeUnder(e.OriginalSource as DependencyObject);

                // Whatever this click turns out to be, it is not the tail of the last one.
                CancelPendingRename();

                // Where a drag would start from, if the pointer goes on to move far enough
                // to mean one. A click that lands anywhere else cancels the last candidate.
                _dragRow = node;
                _dragOrigin = e.GetPosition(JobTree);

                if (node == null || _model == null)
                {
                    return;
                }

                // A click on a row that was already the whole selection is the second half
                // of a slow double-click - read *before* this click changes anything, which
                // is the only moment the distinction exists.
                _renameRow = WouldRename(node) ? node : null;

                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    Apply(() => _model.ToggleSelection(node));
                }
                else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    Apply(() => _model.ExtendSelectionTo(node));
                }
                else
                {
                    Apply(() => _model.SelectOnly(node));
                }
            }
            catch (Exception ex)
            {
                Handle(ex, nameof(OnNodeLeftClick));
            }
        }

        /// <summary>
        /// Starts the wait that turns a second click on an already-selected row into a
        /// rename.
        /// </summary>
        /// <remarks>
        /// On the button coming *up*, not going down, so that a click which turned into a
        /// drag never leaves a rename armed behind it.
        /// </remarks>
        private void OnTreeMouseUp(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (_renameRow == null || _dragging)
                {
                    return;
                }

                // Released somewhere else - over another row, or off the tree entirely.
                if (!ReferenceEquals(NodeUnder(e.OriginalSource as DependencyObject), _renameRow))
                {
                    _renameRow = null;
                    return;
                }

                StartRenameWait();
            }
            catch (Exception ex)
            {
                Handle(ex, nameof(OnTreeMouseUp));
            }
        }

        /// <summary>
        /// Whether clicking this row again would mean "rename it", read before the click is
        /// applied.
        /// </summary>
        /// <remarks>
        /// It has to be the whole selection already: the first click of a selection must
        /// not arm a rename, or every click on the tree would end in an edit box. A
        /// modifier means the click is about the selection rather than about the row, the
        /// part row has no name of its own, and a row already being edited is one the user
        /// is renaming right now.
        /// </remarks>
        private bool WouldRename(JobTreeNode node)
        {
            return node.CanRename
                   && !node.IsEditing
                   && Keyboard.Modifiers == ModifierKeys.None
                   && _model.SelectedNodes.Count == 1
                   && _model.IsSelected(node);
        }

        /// <summary>
        /// Waits out the double-click interval, then renames.
        /// </summary>
        /// <remarks>
        /// <b>The wait is what separates this from the gesture that opens the page.</b>
        /// Windows' own double-click time is the definition of "too quick to be two
        /// clicks", so anything faster is a double-click and is already on its way to the
        /// property page - <see cref="OnNodeDoubleClick"/> cancels this, as does the next
        /// mouse-down, whichever arrives first.
        ///
        /// Background priority on purpose: the tick sets a node into edit mode, which swaps
        /// the row's template and takes the keyboard. That is input-shaped work and has no
        /// business pre-empting a render or an input event that is already queued.
        /// </remarks>
        private void StartRenameWait()
        {
            if (_renameTimer == null)
            {
                _renameTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher);
                _renameTimer.Tick += OnRenameWaitElapsed;
            }

            _renameTimer.Interval =
                TimeSpan.FromMilliseconds(WinForms.SystemInformation.DoubleClickTime);
            _renameTimer.Start();
        }

        private void OnRenameWaitElapsed(object sender, EventArgs e)
        {
            try
            {
                JobTreeNode row = _renameRow;

                CancelPendingRename();

                // Re-asked rather than trusted: the wait is half a second of someone else's
                // time, and the tree can have been rebuilt, reselected or deleted out from
                // under this in it.
                if (row != null && _model != null && WouldRename(row))
                {
                    row.IsEditing = true;
                }
            }
            catch (Exception ex)
            {
                Handle(ex, nameof(OnRenameWaitElapsed));
            }
        }

        private void CancelPendingRename()
        {
            _renameTimer?.Stop();
            _renameRow = null;
        }

        /// <summary>
        /// Follows WPF's own selection, which is what the keyboard moves.
        /// </summary>
        /// <remarks>
        /// Three of the four ways this fires are already accounted for elsewhere, and the
        /// guards are what stop them undoing each other:
        ///
        /// Ctrl is down, so a Ctrl-click has just been dealt with by
        /// <see cref="OnNodeLeftClick"/>, which may well have <i>deselected</i> the row WPF
        /// has now made current - reselecting it here is the one thing that must not happen.
        /// Shift is down, so this is a Shift-arrow, and the range is extended rather than
        /// replaced. Or the row is already the anchor and already selected, which means the
        /// viewmodel drove this itself - restoring a selection after a rebuild, most often -
        /// and collapsing its work to one row would undo it.
        ///
        /// What is left is a plain arrow key, or a click whose selection has already been
        /// made and matches. Both mean the same thing: this row alone.
        /// </remarks>
        private void OnTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            try
            {
                if (_model == null || _drivingSelection)
                {
                    return;
                }

                // Null means the tree is being rebuilt underneath us. The viewmodel is
                // holding the selection across that itself.
                if (!(e.NewValue is JobTreeNode node))
                {
                    return;
                }

                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    Apply(() => _model.ExtendSelectionTo(node));
                    return;
                }

                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    return;
                }

                if (ReferenceEquals(_model.SelectedNode, node) && _model.IsSelected(node))
                {
                    return;
                }

                Apply(() => _model.SelectOnly(node));
            }
            catch (Exception ex)
            {
                Handle(ex, nameof(OnTreeSelectionChanged));
            }
        }

        /// <summary>
        /// Makes sure the context menu is about to act on something the user can see is
        /// selected.
        /// </summary>
        /// <remarks>
        /// <b>A right-click inside the selection leaves it alone</b>, which is what makes
        /// "select three operations, right-click, Delete" mean the three. Only a click on a
        /// row that is not selected replaces the selection with it.
        ///
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
                CancelPendingRename();

                TreeViewItem item = RowUnder(e.OriginalSource as DependencyObject);

                if (item == null || _model == null)
                {
                    return;
                }

                if (item.DataContext is JobTreeNode node && !_model.IsSelected(node))
                {
                    Apply(() => _model.SelectOnly(node));
                }

                // Focusing a TreeViewItem selects it as far as WPF is concerned, which
                // would otherwise collapse a multiple selection through
                // OnTreeSelectionChanged. The move is ours and means nothing about what is
                // selected.
                Apply(() => item.Focus());

                // The row is chosen; nothing above it needs to see this.
                e.Handled = true;
            }
            catch (Exception ex)
            {
                Handle(ex, nameof(OnNodeRightClick));
            }
        }

        /// <summary>
        /// Takes the expander arrow off a row that cannot be collapsed.
        /// </summary>
        /// <remarks>
        /// The model already refuses to fold such a row - see
        /// <see cref="JobTreeNode.CanCollapse"/>, which is what covers the left arrow key as
        /// well - so this is only about not offering an arrow that would snap back.
        ///
        /// <b>Reached through the visual tree rather than by retemplating.</b> The expander
        /// is a <see cref="ToggleButton"/> inside <c>TreeViewItem</c>'s own template, and a
        /// style outside that template cannot name it. Replacing the template to get at it
        /// would mean owning the indentation, the focus visuals and the selection
        /// highlight - three things that currently work - for the sake of one arrow. If a
        /// future theme names it differently the search finds nothing, and the row keeps an
        /// arrow that does not do anything, which is the failure worth having.
        /// </remarks>
        private void OnRowLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!(sender is TreeViewItem item) ||
                    !(item.DataContext is JobTreeNode node) ||
                    node.CanCollapse)
                {
                    return;
                }

                ToggleButton expander = ExpanderOf(item);

                if (expander != null)
                {
                    // Hidden, not collapsed: the template gives the arrow a column of its
                    // own, and taking it out of the layout would shuffle the row left of
                    // where every other row's glyph sits.
                    expander.Visibility = Visibility.Hidden;
                }
            }
            catch (Exception ex)
            {
                Handle(ex, nameof(OnRowLoaded));
            }
        }

        /// <summary>
        /// The expander button belonging to this row, or null.
        /// </summary>
        /// <remarks>
        /// Stops at any nested <see cref="TreeViewItem"/>: a job's rows sit inside the
        /// part's, and its arrow is the one row's arrow this must not touch.
        /// </remarks>
        private static ToggleButton ExpanderOf(DependencyObject row)
        {
            int children = VisualTreeHelper.GetChildrenCount(row);

            for (int i = 0; i < children; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(row, i);

                if (child is ToggleButton button)
                {
                    return button;
                }

                if (child is TreeViewItem)
                {
                    continue;
                }

                ToggleButton found = ExpanderOf(child);

                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>
        /// The node behind a clicked element, or null when the click was not on a row.
        /// </summary>
        /// <remarks>
        /// The expander arrow is inside the row but is not part of it: clicking one opens a
        /// job, and opening a job is not selecting it. A click in the rename box is left
        /// alone for the same kind of reason - it is aimed at the text, not at the tree.
        /// </remarks>
        private static JobTreeNode NodeUnder(DependencyObject clicked)
        {
            DependencyObject walk = clicked;

            while (walk != null && !(walk is TreeViewItem))
            {
                if (walk is ToggleButton || walk is TextBox)
                {
                    return null;
                }

                walk = walk is Visual || walk is Visual3D
                    ? VisualTreeHelper.GetParent(walk)
                    : LogicalTreeHelper.GetParent(walk);
            }

            return (walk as TreeViewItem)?.DataContext as JobTreeNode;
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
        /// <remarks>
        /// The part row has no page to open, and cannot be folded away either, so its
        /// double-click is left unhandled and does nothing. Passing it on is still the right
        /// thing: what WPF would do with it - fold the row - is refused by the model rather
        /// than by intercepting the gesture, so there is one rule about that row and not
        /// two.
        /// </remarks>
        private void OnNodeDoubleClick(object sender, MouseButtonEventArgs e)
        {
            try
            {
                // Two quick clicks mean the page, not a rename. This is the other half of
                // the slow double-click: whichever gesture completes first wins.
                CancelPendingRename();

                if (_model?.SelectedNode is PartNode)
                {
                    return;
                }

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
                // Whatever the key does, it is not the second half of a mouse gesture.
                CancelPendingRename();

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

        // ---- Drag and drop ---------------------------------------------------

        /// <summary>
        /// Where a dragged row would land: the row the line is drawn on, and what that
        /// means to the model.
        /// </summary>
        /// <remarks>
        /// <see cref="Before"/> is the row the dragged one ends up in front of, null for
        /// last, which is what <c>JobDocument</c> takes. Nothing here works out an index -
        /// see the remarks there for why that arithmetic lives in Core.
        /// </remarks>
        private sealed class Placement
        {
            public JobTreeNode Row { get; set; }

            public DropIndicator Edge { get; set; }

            /// <summary>The job an operation is being dropped into. Null for a job drag.</summary>
            public JobNode Job { get; set; }

            public JobTreeNode Before { get; set; }
        }

        /// <summary>The dragged row travels as itself; nothing leaves this control.</summary>
        private const string RowFormat = "GCam.JobTreeRow";

        private Point _dragOrigin;
        private JobTreeNode _dragRow;
        private bool _dragging;

        /// <summary>
        /// Starts a drag once the pointer has moved far enough to mean one.
        /// </summary>
        /// <remarks>
        /// The threshold is Windows' own, so a click with a shaky hand stays a click. A row
        /// being renamed is never dragged - the pointer is there to put a caret in the text
        /// box, and <see cref="NodeUnder"/> has already refused to make one the candidate.
        ///
        /// <b><c>DoDragDrop</c> does not return until the drop.</b> It runs its own message
        /// loop, so everything below it - the drag feedback, the drop, the model change -
        /// happens inside this call.
        /// </remarks>
        private void OnTreeMouseMove(object sender, MouseEventArgs e)
        {
            try
            {
                // The part row is the tree itself; there is nowhere it could go.
                if (_dragging || _dragRow == null || _dragRow is PartNode ||
                    e.LeftButton != MouseButtonState.Pressed)
                {
                    return;
                }

                Vector moved = e.GetPosition(JobTree) - _dragOrigin;

                if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
                {
                    return;
                }

                JobTreeNode row = _dragRow;

                // The click is a drag, not the first half of anything.
                CancelPendingRename();

                _dragging = true;

                try
                {
                    DragDrop.DoDragDrop(
                        JobTree, new DataObject(RowFormat, row), DragDropEffects.Move);
                }
                finally
                {
                    _dragging = false;
                    _dragRow = null;
                    _model?.ShowDropAt(null, DropIndicator.None);
                }
            }
            catch (Exception ex)
            {
                Handle(ex, nameof(OnTreeMouseMove));
            }
        }

        private void OnTreeDragOver(object sender, DragEventArgs e)
        {
            try
            {
                Placement drop = Resolve(e);

                e.Effects = drop == null ? DragDropEffects.None : DragDropEffects.Move;
                e.Handled = true;

                _model?.ShowDropAt(drop?.Row, drop?.Edge ?? DropIndicator.None);
            }
            catch (Exception ex)
            {
                Handle(ex, nameof(OnTreeDragOver));
            }
        }

        private void OnTreeDragLeave(object sender, DragEventArgs e)
        {
            try
            {
                _model?.ShowDropAt(null, DropIndicator.None);
            }
            catch (Exception ex)
            {
                Handle(ex, nameof(OnTreeDragLeave));
            }
        }

        /// <summary>
        /// Carries out the move the line was promising.
        /// </summary>
        /// <remarks>
        /// A drop that would change nothing is not refused here - the model answers that,
        /// and answering it in two places is how the two come to disagree.
        /// </remarks>
        private void OnTreeDrop(object sender, DragEventArgs e)
        {
            try
            {
                Placement drop = Resolve(e);

                _model?.ShowDropAt(null, DropIndicator.None);
                e.Handled = true;

                if (drop == null || _model == null)
                {
                    return;
                }

                switch (Dragged(e))
                {
                    case OperationNode operation:
                        _model.MoveOperation(operation, drop.Job, drop.Before as OperationNode);
                        break;

                    case JobNode job:
                        _model.MoveJob(job, drop.Before as JobNode);
                        break;
                }
            }
            catch (Exception ex)
            {
                Handle(ex, nameof(OnTreeDrop));
            }
        }

        private static JobTreeNode Dragged(DragEventArgs e) =>
            e.Data?.GetDataPresent(RowFormat) == true
                ? e.Data.GetData(RowFormat) as JobTreeNode
                : null;

        /// <summary>
        /// Works out where the row under the cursor would put the one being dragged, or
        /// null when it would not take it.
        /// </summary>
        /// <remarks>
        /// <b>An operation goes between operations, and a job between jobs.</b> Dropping an
        /// operation on a job's own row is the one crossing: it means first in that job,
        /// drawn as the line directly under the row, which is where it would appear. A job
        /// dropped on an operation is refused outright rather than guessed at - the rows
        /// around an operation belong to a job that is not the one being moved, so any
        /// answer would be an invention.
        ///
        /// Which half of the row the cursor is in decides which side of it the line goes,
        /// measured against the header rather than the row: a job's row contains all of its
        /// operations, so its own height is the whole block.
        /// </remarks>
        private Placement Resolve(DragEventArgs e)
        {
            JobTreeNode dragged = Dragged(e);

            if (dragged == null || _model == null)
            {
                return null;
            }

            TreeViewItem item = RowUnder(e.OriginalSource as DependencyObject);

            if (!(item?.DataContext is JobTreeNode row))
            {
                return null;
            }

            bool below = InLowerHalf(item, e);

            switch (dragged)
            {
                case OperationNode _ when row is OperationNode target:
                    JobNode owner = _model.JobNodeOf(target);

                    return owner == null ? null : new Placement
                    {
                        Row = target,
                        Edge = below ? DropIndicator.Below : DropIndicator.Above,
                        Job = owner,
                        Before = below ? After(owner.Children, target) : target,
                    };

                case OperationNode _ when row is JobNode target:
                    return new Placement
                    {
                        Row = target,
                        Edge = DropIndicator.Below,
                        Job = target,
                        Before = target.Children.FirstOrDefault(),
                    };

                case JobNode _ when row is JobNode target:
                    return new Placement
                    {
                        Row = target,
                        Edge = below ? DropIndicator.Below : DropIndicator.Above,
                        Before = below ? After(_model.Part?.Children, target) : target,
                    };

                // Dropping a job on the part row means first in the part, which reads the
                // same way as dropping an operation on a job's row.
                case JobNode _ when row is PartNode part:
                    return new Placement
                    {
                        Row = part,
                        Edge = DropIndicator.Below,
                        Before = part.Children.FirstOrDefault(),
                    };

                default:
                    return null;
            }
        }

        /// <summary>The row after this one among its siblings, or null when it is the last.</summary>
        private static JobTreeNode After(IList<JobTreeNode> rows, JobTreeNode row)
        {
            if (rows == null)
            {
                return null;
            }

            int index = rows.IndexOf(row);

            return index >= 0 && index + 1 < rows.Count ? rows[index + 1] : null;
        }

        /// <summary>
        /// Whether the cursor is in the bottom half of a row's own label.
        /// </summary>
        /// <remarks>
        /// Measured against the header part rather than the <see cref="TreeViewItem"/>,
        /// because an expanded job's item is as tall as the job and everything in it -
        /// halfway down that is somewhere in the middle of its operations. Falls back to
        /// the item if a theme ever stops naming its header, which costs the accuracy of
        /// the halves and nothing else.
        /// </remarks>
        private static bool InLowerHalf(TreeViewItem item, DragEventArgs e)
        {
            var header = item.Template?.FindName("PART_Header", item) as FrameworkElement ?? item;

            return e.GetPosition(header).Y > header.ActualHeight / 2;
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
        /// Builds and shows the menu for what is selected.
        /// </summary>
        /// <remarks>
        /// A menu per kind rather than one that hides rows, because the two nodes share
        /// almost nothing: Make Default and New Operation mean nothing on an operation, and
        /// Suppress means nothing on a job.
        ///
        /// <b>What is selected decides the menu, not what was clicked</b> - the click that
        /// opened it has already made sure the row under the cursor is part of the
        /// selection. A selection holding both kinds gets the third menu: only the two
        /// commands that mean the same thing for either.
        ///
        /// Commands that can only act on one thing - Edit, Rename, Make Default, New
        /// Operation - are left out when several rows are selected rather than shown
        /// greyed. Half a menu of dead rows reads as something being broken.
        /// </remarks>
        private void ShowNodeMenu()
        {
            // Rebuilt per click. The menu is small, and building it fresh is how
            // enablement stays honest without a separate Opened handler.
            _nodeMenu?.Dispose();
            _nodeMenu = new WinForms.ContextMenuStrip();

            if (_model == null)
            {
                return;
            }

            IReadOnlyList<JobNode> jobs = _model.SelectedJobNodes;
            IReadOnlyList<OperationNode> operations = _model.SelectedOperationNodes;

            if (jobs.Count == 0 && operations.Count == 0)
            {
                // The part row, which is the only other thing there is to click. It offers
                // the two commands that mean something for a whole part - and for a part
                // with no jobs it is the only row there is to ask.
                if (!(_model.SelectedNode is PartNode))
                {
                    return;
                }

                BuildPartMenu();
            }
            else if (jobs.Count == 0)
            {
                BuildOperationMenu(operations);
            }
            else if (operations.Count == 0)
            {
                BuildJobMenu(jobs);
            }
            else
            {
                BuildMixedMenu();
            }

            _nodeMenu.Show(WinForms.Control.MousePosition);
        }

        /// <summary>
        /// The part row: make a job, or generate everything in the part.
        /// </summary>
        /// <remarks>
        /// Generate All is greyed out on a part with no jobs, which is also the state the
        /// row exists to give somewhere to click in.
        /// </remarks>
        private void BuildPartMenu()
        {
            AddMenuItem("New Job…", () => _model.NewJob());
            AddMenuItem("Generate All", () => _model.GenerateAll(), enabled: _model.HasJobs);
        }

        private void BuildJobMenu(IReadOnlyList<JobNode> jobs)
        {
            bool one = jobs.Count == 1;

            if (one)
            {
                AddMenuItem("Edit…", () => _model.EditJob(jobs[0]));
                AddMenuItem("Rename", BeginRename, shortcut: "F2");
                _nodeMenu.Items.Add(new WinForms.ToolStripSeparator());
                AddMenuItem("New Operation…", () => _model.NewOperation(jobs[0]));
            }

            AddMenuItem(
                "Generate",
                () => _model.GenerateJobs(jobs),
                enabled: jobs.Any(j => j.Job != null && j.Job.Operations.Count > 0));
            _nodeMenu.Items.Add(new WinForms.ToolStripSeparator());
            AddMenuItem("Duplicate", () => _model.DuplicateJobs(jobs));

            // Nothing to do for a job that is already the default, and no answer at all for
            // several of them.
            if (one)
            {
                AddMenuItem("Make Default", () => _model.MakeDefault(jobs[0]), enabled: !jobs[0].IsDefault);
            }

            AddMenuItem("Delete", DeleteSelected, shortcut: "Del");
        }

        /// <summary>
        /// The operation menu, in the same order as the job one so the two read alike.
        /// </summary>
        /// <remarks>
        /// Generate is greyed out when every selected operation is suppressed, because
        /// <see cref="GCam.Core.Generation.GenerationQueue"/> skips those - offering a
        /// command that is guaranteed to do nothing is worse than not offering it.
        ///
        /// Suppress is one command over the whole selection rather than a toggle each:
        /// anything still running gets suppressed, and only when none of them is does it
        /// turn into Restore. A per-row toggle would leave a mixed selection in a state
        /// nobody asked for.
        /// </remarks>
        private void BuildOperationMenu(IReadOnlyList<OperationNode> operations)
        {
            bool one = operations.Count == 1;
            bool anyEnabled = operations.Any(o => o.Operation?.Enabled ?? true);

            if (one)
            {
                AddMenuItem("Edit…", () => _model.EditSelected());
                AddMenuItem("Rename", BeginRename, shortcut: "F2");
                _nodeMenu.Items.Add(new WinForms.ToolStripSeparator());
            }

            AddMenuItem("Generate", () => _model.GenerateOperations(operations), enabled: anyEnabled);
            AddMenuItem(
                "Suppress",
                () => _model.SetOperationsEnabled(operations, !anyEnabled),
                isChecked: !anyEnabled);
            _nodeMenu.Items.Add(new WinForms.ToolStripSeparator());
            AddMenuItem("Duplicate", () => _model.DuplicateOperations(operations));
            AddMenuItem("Delete", DeleteSelected, shortcut: "Del");
        }

        /// <summary>
        /// Jobs and operations selected together: the two commands that mean the same thing
        /// for both.
        /// </summary>
        private void BuildMixedMenu()
        {
            // The selection is what is being generated, and GenerateSelection is what knows
            // that an operation inside one of these jobs is about to be generated by it.
            AddMenuItem("Generate", () => _model.GenerateSelection());

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
        /// Deletes everything selected, after asking once.
        /// </summary>
        /// <remarks>
        /// <b>An operation deletes the operation, not its job.</b> An operation node stands
        /// in for its parent elsewhere - New Operation - and this is the one place that
        /// would be destructive, because Delete on an operation used to take the whole job
        /// with it.
        ///
        /// One question for the whole selection, naming what is about to go. There is no
        /// undo in G-CAM yet - <c>Core/Commands</c> is unbuilt - so a delete is final, and
        /// it takes generated toolpaths with it.
        /// </remarks>
        private void DeleteSelected()
        {
            if (_model == null)
            {
                return;
            }

            IReadOnlyList<JobNode> jobs = _model.SelectedJobNodes;
            IReadOnlyList<OperationNode> operations = _model.SelectedOperationNodes;

            if (jobs.Count == 0 && operations.Count == 0)
            {
                return;
            }

            if (Ask(DeleteQuestion(jobs, operations)))
            {
                _model.Delete(_model.SelectedNodes.ToList());
            }
        }

        /// <summary>
        /// What the delete prompt says, which has to be true of every shape a selection can
        /// take.
        /// </summary>
        /// <remarks>
        /// One job and one operation are named, because a name is what someone checks before
        /// answering. Past that, counts: reading eight names back is not a check, it is a
        /// wall. The mixed wording says "selected" rather than claiming the two counts are
        /// separate things, because an operation inside one of the selected jobs is in both.
        /// </remarks>
        private string DeleteQuestion(
            IReadOnlyList<JobNode> jobs, IReadOnlyList<OperationNode> operations)
        {
            if (jobs.Count == 0)
            {
                return operations.Count == 1
                    ? $"Delete {operations[0].Name}?"
                    : $"Delete these {operations.Count} operations?";
            }

            int inside = jobs.Sum(j => _model.OperationCount(j));

            if (operations.Count > 0)
            {
                return $"Delete the {Count(jobs.Count, "job")} and "
                    + $"{Count(operations.Count, "operation")} selected?";
            }

            string what = jobs.Count == 1 ? jobs[0].Name : $"these {jobs.Count} jobs";

            return inside == 0
                ? $"Delete {what}?"
                : $"Delete {what} and {(jobs.Count == 1 ? "its" : "their")} {Count(inside, "operation")}?";
        }

        private static string Count(int n, string noun) => n + " " + noun + (n == 1 ? string.Empty : "s");

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
