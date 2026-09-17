using System.Collections.ObjectModel;
using GCam.Core.Model;

namespace GCam.UI.ViewModels
{
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

        public bool IsExpanded
        {
            get => _isExpanded;
            set => Set(ref _isExpanded, value);
        }

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
