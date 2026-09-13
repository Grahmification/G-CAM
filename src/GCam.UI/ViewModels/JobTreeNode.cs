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
        private bool _isExpanded = true;
        private bool _isEditing;

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

        public bool IsSelected
        {
            get => _isSelected;
            set => Set(ref _isSelected, value);
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
        }

        public Operation Operation { get; }

        public override string Glyph => "⚙";
    }
}
