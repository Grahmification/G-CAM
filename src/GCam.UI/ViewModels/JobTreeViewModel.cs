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
        private readonly IGCamLog _log;

        private JobTreeNode _selectedNode;

        public JobTreeViewModel(JobDocument document, IJobEditor editor = null, IGCamLog log = null)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            _editor = editor;
            _log = log ?? NullLog.Instance;

            Refresh();
        }

        public ObservableCollection<JobTreeNode> Nodes { get; } = new ObservableCollection<JobTreeNode>();

        public JobTreeNode SelectedNode
        {
            get => _selectedNode;
            set => Set(ref _selectedNode, value);
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
            string selectedId = (SelectedNode as JobNode)?.Job.Id;

            Nodes.Clear();

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
            Select(copy);
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

        private void Select(Job job)
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
