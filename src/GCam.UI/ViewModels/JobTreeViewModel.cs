using System;
using System.Collections.ObjectModel;
using System.Linq;
using GCam.Core.Abstractions;
using GCam.Core.Diagnostics;
using GCam.Core.Model;

namespace GCam.UI.ViewModels
{
    /// <summary>
    /// The G-CAM tab's tree: the jobs in one document, and what the context menu does to
    /// them.
    /// </summary>
    /// <remarks>
    /// Presentation state only. Every rule - unique names, which job is the default,
    /// where the default goes when one is deleted - belongs to
    /// <see cref="JobDocument"/> in Core, where it can be tested without WPF. This class
    /// turns that model into nodes and passes intent on to an
    /// <see cref="IJobEditor"/>, because editing means a SOLIDWORKS PropertyManager page
    /// and this project cannot reach SOLIDWORKS.
    /// </remarks>
    public sealed class JobTreeViewModel : ViewModelBase
    {
        private readonly JobDocument _document;
        private readonly IJobEditor _editor;
        private readonly IJobPreview _preview;
        private readonly IGCamLog _log;

        private JobTreeNode _selectedNode;

        public JobTreeViewModel(
            JobDocument document,
            IJobEditor editor = null,
            IGCamLog log = null,
            IJobPreview preview = null)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            _editor = editor;
            _preview = preview;
            _log = log ?? NullLog.Instance;

            Refresh();
        }

        public ObservableCollection<JobTreeNode> Nodes { get; } = new ObservableCollection<JobTreeNode>();

        /// <summary>
        /// What the user has picked in the tree. Setting it also decides what the 3D view
        /// shows.
        /// </summary>
        /// <remarks>
        /// Selection is the trigger for the preview because it is the one signal that
        /// means "this is the job I am looking at" - it covers clicking a node, arrowing
        /// through the tree, and the reselection that follows a refresh, without any of
        /// them having to know the preview exists.
        ///
        /// Still presentation state, in the sense the class comment means: the rule about
        /// which job an operation node stands for lives in
        /// <see cref="SelectedJobNode"/>, and what a job's stock looks like lives in
        /// Core and GCam.SolidWorks. This only says which one is current.
        /// </remarks>
        public JobTreeNode SelectedNode
        {
            get => _selectedNode;
            set
            {
                if (Set(ref _selectedNode, value))
                {
                    // An operation stands in for its job here too, so selecting one keeps
                    // its job's stock on screen rather than clearing it.
                    _preview?.ShowJob(SelectedJobNode?.Job);
                }
            }
        }

        public bool HasJobs => _document.Jobs.Count > 0;

        /// <summary>Shown instead of the tree when the document has no jobs.</summary>
        public string EmptyMessage => "No job in this document.";

        /// <summary>The job the selected node belongs to, or null.</summary>
        public JobNode SelectedJobNode
        {
            get
            {
                if (SelectedNode is JobNode job)
                {
                    return job;
                }

                // An operation stands in for its parent, so right-clicking one and
                // choosing New Operation does the expected thing.
                return Nodes.OfType<JobNode>().FirstOrDefault(j => j.Children.Contains(SelectedNode));
            }
        }

        /// <summary>
        /// Rebuilds every node from the model.
        /// </summary>
        /// <remarks>
        /// Wholesale rather than incrementally, the same way the tool library browser
        /// refreshes its folders. The tree is small and a rebuild cannot drift out of
        /// step with the model the way a patch can.
        /// </remarks>
        public void Refresh()
        {
            string selectedId = SelectedJobNode?.Job.Id;

            Nodes.Clear();

            // The nodes about to be rebuilt are not the ones we are holding. Dropping the
            // selection here rather than leaving it dangling is what makes a deleted job
            // take its preview with it: RestoreSelection puts it back if the job is still
            // there, and if it is not, nothing does.
            SelectedNode = null;

            foreach (Job job in _document.Jobs)
            {
                var node = new JobNode(job) { IsDefault = _document.IsDefault(job) };

                foreach (Operation operation in job.Operations)
                {
                    node.Children.Add(new OperationNode(operation));
                }

                Nodes.Add(node);
            }

            RestoreSelection(selectedId);

            Raise(nameof(HasJobs));
        }

        private void RestoreSelection(string jobId)
        {
            if (jobId == null)
            {
                return;
            }

            JobNode match = Nodes.OfType<JobNode>()
                .FirstOrDefault(n => string.Equals(n.Job.Id, jobId, StringComparison.Ordinal));

            if (match != null)
            {
                match.IsSelected = true;
                SelectedNode = match;
            }
        }

        // ---- Actions ---------------------------------------------------------

        public void EditJob(JobNode node)
        {
            if (node != null)
            {
                _editor?.EditJob(node.Job);
            }
        }

        public void NewOperation(JobNode node)
        {
            _editor?.NewOperation(node?.Job ?? _document.DefaultJob);
        }

        public void Duplicate(JobNode node)
        {
            if (node == null)
            {
                return;
            }

            Job copy = _document.Duplicate(node.Job);
            _log.Info("Duplicated job '{0}' as '{1}'.", node.Job.Name, copy.Name);

            Refresh();
            SelectJob(copy);
        }

        public void Delete(JobNode node)
        {
            if (node == null)
            {
                return;
            }

            _document.Remove(node.Job);
            _log.Info("Deleted job '{0}'.", node.Job.Name);

            Refresh();
        }

        public void MakeDefault(JobNode node)
        {
            if (node == null)
            {
                return;
            }

            _document.MakeDefault(node.Job);

            foreach (JobNode job in Nodes.OfType<JobNode>())
            {
                job.IsDefault = _document.IsDefault(job.Job);
            }
        }

        /// <summary>
        /// Commits an in-place rename.
        /// </summary>
        /// <exception cref="GCamUserException">
        /// The name is blank or already taken. The caller reports it and puts the node
        /// back into edit mode.
        /// </exception>
        public void Rename(JobNode node, string newName)
        {
            if (node == null)
            {
                return;
            }

            _document.Rename(node.Job, newName);

            // The model may have trimmed it, so take the name back rather than assuming
            // what was typed is what was stored.
            node.Name = node.Job.Name;
        }

        /// <summary>How many operations would go with a job, for the delete prompt.</summary>
        public int OperationCount(JobNode node) => node?.Job.Operations.Count ?? 0;

        /// <summary>
        /// Puts the selection on a job, if the tree is showing it.
        /// </summary>
        /// <remarks>
        /// Public because creating a job from the toolbar has to land on it: the job was
        /// added by the add-in rather than by this viewmodel, and leaving the tree with
        /// nothing selected right after someone set up a job's stock makes the preview
        /// look broken.
        /// </remarks>
        public void SelectJob(Job job)
        {
            JobNode node = Nodes.OfType<JobNode>()
                .FirstOrDefault(n => ReferenceEquals(n.Job, job));

            if (node != null)
            {
                node.IsSelected = true;
                SelectedNode = node;
            }
        }
    }
}
