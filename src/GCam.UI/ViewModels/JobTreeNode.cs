using System.Collections.ObjectModel;
using GCam.Core.Model;

namespace GCam.UI.ViewModels
{
    /// <summary>
    /// Where a dragged row would land, relative to the row drawing the mark.
    /// </summary>
    /// <remarks>
    /// A line between rows rather than a highlight on one, because what a drop changes is a
    /// position in a list and not the row underneath the cursor. The one exception reads
    /// the same way: dropping an operation on a job's own row means "first in this job",
    /// which is the line directly under that row.
    /// </remarks>
    public enum DropIndicator
    {
        None = 0,
        Above,
        Below,
    }

    /// <summary>
    /// A node in the G-CAM tree. Presentation state only - the job and operation data
    /// live in Core.
    /// </summary>
    public abstract class JobTreeNode : ViewModelBase
    {
        private string _name;
        private bool _isSelected;
        private bool _isCurrent;
        private bool _isExpanded = true;
        private bool _isEditing;
        private bool _isSuppressed;
        private OperationBadge _badge;
        private string _statusText;
        private DropIndicator _drop;
        private string _note;

        protected JobTreeNode(string name)
        {
            _name = name;
        }

        public ObservableCollection<JobTreeNode> Children { get; } =
            new ObservableCollection<JobTreeNode>();

        /// <summary>
        /// The label. Settable because renaming happens in place; the Core object is
        /// only updated once the rename is accepted.
        /// </summary>
        public string Name
        {
            get => _name;
            set => Set(ref _name, value);
        }

        /// <summary>
        /// True when this row is one of the selected ones, which is what draws it
        /// highlighted and what puts it in the 3D view.
        /// </summary>
        /// <remarks>
        /// G-CAM's own selection, not WPF's. A <see cref="System.Windows.Controls.TreeView"/>
        /// selects one row and only one, so the highlight is drawn by the row template from
        /// this flag and the built-in one is turned off - see JobTreeView.xaml.
        /// </remarks>
        public bool IsSelected
        {
            get => _isSelected;
            set => Set(ref _isSelected, value);
        }

        /// <summary>
        /// True for the one row WPF itself considers selected: where the keyboard is, and
        /// where a Shift-click measures from.
        /// </summary>
        /// <remarks>
        /// Bound two-way to <c>TreeViewItem.IsSelected</c>, which is what keeps arrow keys,
        /// focus and scroll-into-view working while the visible selection is ours. It draws
        /// nothing by itself, and it is not the same as <see cref="IsSelected"/>: Ctrl-click
        /// a selected row and it stays current while ceasing to be selected.
        /// </remarks>
        public bool IsCurrent
        {
            get => _isCurrent;
            set => Set(ref _isCurrent, value);
        }

        /// <summary>
        /// Whether this row's children are showing. Refused for a row that
        /// <see cref="CanCollapse"/> says cannot be folded away.
        /// </summary>
        /// <remarks>
        /// Refused here rather than in the view because there are several ways to collapse
        /// a row - the arrow, the left arrow key, double-click - and only one of them is
        /// worth hiding. The binding writes false, this hands back true, and the binding
        /// puts it straight back.
        /// </remarks>
        public bool IsExpanded
        {
            get => _isExpanded;
            set => Set(ref _isExpanded, value || !CanCollapse);
        }

        /// <summary>Whether this kind of row can be folded away at all.</summary>
        public virtual bool CanCollapse => true;

        /// <summary>
        /// True while the label is an editable text box rather than static text.
        /// </summary>
        public bool IsEditing
        {
            get => _isEditing;
            set => Set(ref _isEditing, value);
        }

        /// <summary>Emoji shown before the label, matching the tool library browser.</summary>
        public abstract string Glyph { get; }

        /// <summary>Whether this kind of node can be renamed at all.</summary>
        public virtual bool CanRename => false;

        /// <summary>
        /// Drawn greyed out. Only an operation is ever suppressed, but the flag is on the
        /// base because one label template serves every kind of node.
        /// </summary>
        public bool IsSuppressed
        {
            get => _isSuppressed;
            set => Set(ref _isSuppressed, value);
        }

        /// <summary>
        /// The mark drawn in the corner of <see cref="Glyph"/>. On the base for the same
        /// reason as <see cref="IsSuppressed"/>; a job never earns one.
        /// </summary>
        public OperationBadge Badge
        {
            get => _badge;
            set => Set(ref _badge, value);
        }

        /// <summary>
        /// A word about what is happening to this row right now, shown after the name.
        /// Null when there is nothing to say, which is nearly always.
        /// </summary>
        /// <remarks>
        /// For states that pass rather than states that last: "(generating…)" while the
        /// queue is on this operation, and a percentage beside it once generation runs off
        /// the SOLIDWORKS thread and there is time to read one. What an operation <i>is</i>
        /// - stale, failed, suppressed - is the badge and the greyed name, which persist
        /// and are rebuilt from the model.
        /// </remarks>
        public string Note
        {
            get => _note;
            set => Set(ref _note, value);
        }

        /// <summary>
        /// Where a row being dragged would land, drawn as a line along this row's edge.
        /// </summary>
        /// <remarks>
        /// On the base because either kind of row can be dropped next to, and because one
        /// row template draws them all. Only ever set while a drag is in progress, and
        /// cleared whichever way the drag ends.
        /// </remarks>
        public DropIndicator Drop
        {
            get => _drop;
            set => Set(ref _drop, value);
        }

        /// <summary>
        /// The row's tooltip, or null for a node with nothing to explain.
        /// </summary>
        /// <remarks>
        /// Null rather than empty on purpose - WPF suppresses a null tooltip and shows an
        /// empty box for "".
        /// </remarks>
        public string StatusText
        {
            get => _statusText;
            set => Set(ref _statusText, value);
        }
    }

    /// <summary>The part itself, with every job in it underneath.</summary>
    /// <remarks>
    /// One row, always there, whether or not the part has any jobs - which is what gives
    /// an empty part somewhere to right-click to make its first one. It draws nothing in
    /// the 3D view: a job shows its stock and an operation its toolpath, and a container
    /// shows neither.
    ///
    /// <b>The name comes from SOLIDWORKS and is read fresh on every rebuild</b>, so a Save
    /// As follows it without anything having to subscribe to a rename. GCam.UI cannot reach
    /// SOLIDWORKS, so what arrives here is a string somebody else asked for.
    /// </remarks>
    public sealed class PartNode : JobTreeNode
    {
        public PartNode(string partName)
            : base(Label(partName))
        {
        }

        public override string Glyph => "📄";

        /// <summary>
        /// The part row never folds away.
        /// </summary>
        /// <remarks>
        /// It is the top of the tree rather than a container anyone needs to get out of the
        /// way: collapsing it would hide every job in the part and leave one row saying
        /// nothing. Its arrow is hidden too, so there is nothing to click that would be
        /// refused - see JobTreeView.OnRowLoaded.
        /// </remarks>
        public override bool CanCollapse => false;

        /// <summary>
        /// "<c>Bracket Operations</c>" - the part's name without its file extension, and
        /// what the rows under it are.
        /// </summary>
        /// <remarks>
        /// Falls back to the bare word when there is no name to be had, which is what an
        /// unsaved part with no title would give.
        /// </remarks>
        public static string Label(string partName)
        {
            partName = (partName ?? string.Empty).Trim();

            return partName.Length == 0 ? "Operations" : partName + " Operations";
        }
    }

    /// <summary>A job, with its operations underneath it.</summary>
    public sealed class JobNode : JobTreeNode
    {
        private bool _isDefault;

        public JobNode(Job job)
            : base(job.Name)
        {
            Job = job;
        }

        public Job Job { get; }

        /// <summary>
        /// Shown in bold. The default job is the one New Operation targets when nothing
        /// is selected.
        /// </summary>
        public bool IsDefault
        {
            get => _isDefault;
            set => Set(ref _isDefault, value);
        }

        public override string Glyph => "📁";

        public override bool CanRename => true;
    }

    /// <summary>An operation inside a job.</summary>
    public sealed class OperationNode : JobTreeNode
    {
        public OperationNode(Operation operation)
            : base(operation.Name)
        {
            Operation = operation;
            IsSuppressed = !operation.Enabled;
            Badge = OperationStatus.Badge(operation.State);
            StatusText = OperationStatus.Describe(operation);
        }

        public Operation Operation { get; }

        public override string Glyph => "⚙";

        /// <summary>
        /// Operations rename in place, like jobs.
        /// </summary>
        /// <remarks>
        /// This is the *only* way to rename one: the Operation property page deliberately
        /// has no name field, using the operation's name as its panel title instead, on the
        /// grounds that the tree already renames in place. See docs/design/operations.md.
        /// </remarks>
        public override bool CanRename => true;
    }
}
