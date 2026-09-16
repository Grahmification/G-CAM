using System;
using System.Collections.ObjectModel;
using System.Linq;
using GCam.Core.Abstractions;
using GCam.Core.Diagnostics;
using GCam.Core.Generation;
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
                    Raise(nameof(SelectedOperationNode));

                    // An operation stands in for its job here too, so selecting one keeps
                    // its job's stock on screen rather than clearing it.
                    _preview?.ShowJob(SelectedJobNode?.Job);
                }
            }
        }

        public bool HasJobs => _document.Jobs.Count > 0;

        /// <summary>The selected node when it is an operation, or null.</summary>
        public OperationNode SelectedOperationNode => SelectedNode as OperationNode;

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

            // Held separately from the job's id because an operation node stands in for
            // its job everywhere else, and restoring the job would quietly move the
            // selection up a level every time an operation was renamed or suppressed.
            string selectedOperationId = SelectedOperationNode?.Operation.Id;

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

            RestoreSelection(selectedId, selectedOperationId);

            Raise(nameof(HasJobs));
        }

        /// <summary>
        /// Puts the selection back after a rebuild - on the same operation if it is still
        /// there, otherwise on its job.
        /// </summary>
        /// <remarks>
        /// Falling back to the job is what a delete relies on: the operation the user was
        /// on has just gone, and landing on its job keeps the stock and the remaining
        /// toolpaths on screen rather than clearing the 3D view.
        /// </remarks>
        private void RestoreSelection(string jobId, string operationId)
        {
            if (jobId == null)
            {
                return;
            }

            JobNode match = Nodes.OfType<JobNode>()
                .FirstOrDefault(n => string.Equals(n.Job.Id, jobId, StringComparison.Ordinal));

            if (match == null)
            {
                return;
            }

            JobTreeNode target = operationId == null
                ? match
                : match.Children.OfType<OperationNode>().FirstOrDefault(
                      n => string.Equals(n.Operation.Id, operationId, StringComparison.Ordinal))
                  ?? (JobTreeNode)match;

            target.IsSelected = true;
            SelectedNode = target;
        }

        // ---- Actions ---------------------------------------------------------

        public void EditJob(JobNode node)
        {
            if (node != null)
            {
                _editor?.EditJob(node.Job);
            }
        }

        /// <summary>
        /// Opens whichever node is selected for editing - a job, or an operation.
        /// </summary>
        /// <remarks>
        /// One entry point so that double-click, Enter and the context menu all mean the
        /// same thing whichever kind of node is under them.
        /// </remarks>
        public void EditSelected()
        {
            if (SelectedNode is OperationNode operation)
            {
                _editor?.EditOperation(SelectedJobNode?.Job, operation.Operation);
                return;
            }

            EditJob(SelectedJobNode);
        }

        public void NewOperation(JobNode node)
        {
            _editor?.NewOperation(node?.Job ?? _document.DefaultJob);
        }

        /// <summary>Computes the toolpaths for a job's operations.</summary>
        public void GenerateJob(JobNode node)
        {
            if (node?.Job != null)
            {
                _editor?.GenerateJob(node.Job);
            }
        }

        public void Duplicate(JobNode node)
        {
            if (node == null)
            {
                return;
            }

            Job copy = _document.Duplicate(node.Job);
            _log.Info("Duplicated job '{0}' as '{1}'.", node.Job.Name, copy.Name);

            Changed();
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

            Changed();
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

            Changed();
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

            Changed();
        }

        /// <summary>How many operations would go with a job, for the delete prompt.</summary>
        public int OperationCount(JobNode node) => node?.Job.Operations.Count ?? 0;

        // ---- Operations ------------------------------------------------------

        /// <summary>Computes the toolpath for one operation.</summary>
        public void GenerateOperation(OperationNode node)
        {
            Job job = JobOf(node);

            if (job != null)
            {
                _editor?.GenerateOperation(job, node.Operation);
            }
        }

        /// <summary>Copies an operation, landing the selection on the copy.</summary>
        public void DuplicateOperation(OperationNode node)
        {
            Job job = JobOf(node);
            if (job == null)
            {
                return;
            }

            Operation copy = job.DuplicateOperation(node.Operation);
            _log.Info("Duplicated operation '{0}' as '{1}'.", node.Operation.Name, copy.Name);

            // The copy sits where the original's output used to be in the cutting order,
            // so anything rest-machining below it now sees different stock.
            Staleness.OperationOrderChanged(job);

            Changed();
            Refresh();
            SelectOperation(copy);
        }

        /// <summary>
        /// Takes an operation out of its job, toolpath and all.
        /// </summary>
        /// <remarks>
        /// There is no undo - <c>Core/Commands</c> is not built - so the caller asks first.
        /// The toolpath leaves the 3D view through the refresh, because the preview puts
        /// up a layer per operation and rebuilds them all from the job it is given.
        /// </remarks>
        public void DeleteOperation(OperationNode node)
        {
            Job job = JobOf(node);
            if (job == null)
            {
                return;
            }

            job.RemoveOperation(node.Operation);
            _log.Info("Deleted operation '{0}' from job '{1}'.", node.Operation.Name, job.Name);

            // Whatever machined what this one left behind has to be computed again.
            Staleness.OperationOrderChanged(job);

            Changed();
            Refresh();
        }

        /// <summary>
        /// Suppresses or restores an operation.
        /// </summary>
        /// <remarks>
        /// A suppressed operation keeps its toolpath and its parameters; it is skipped by
        /// generation, is not drawn, and posts nothing. Its own path stays trustworthy -
        /// nothing about the operation changed - but what reaches the operations below it
        /// just did, in both directions, which is what
        /// <see cref="Staleness.OperationEnabledChanged"/> covers.
        /// </remarks>
        public void SetOperationEnabled(OperationNode node, bool enabled)
        {
            Job job = JobOf(node);
            if (job == null || node.Operation.Enabled == enabled)
            {
                return;
            }

            node.Operation.Enabled = enabled;
            _log.Info(
                "{0} operation '{1}'.", enabled ? "Restored" : "Suppressed", node.Operation.Name);

            Staleness.OperationEnabledChanged(job, node.Operation);

            Changed();
            Refresh();
        }

        /// <summary>
        /// Commits an in-place rename of an operation.
        /// </summary>
        /// <exception cref="GCamUserException">
        /// The name is blank or already used in this job. The caller reports it and puts
        /// the node back into edit mode, exactly as it does for a job.
        /// </exception>
        public void RenameOperation(OperationNode node, string newName)
        {
            Job job = JobOf(node);
            if (job == null)
            {
                return;
            }

            job.RenameOperation(node.Operation, newName);

            node.Name = node.Operation.Name;

            Changed();
        }

        /// <summary>Puts the selection on an operation, if the tree is showing it.</summary>
        public void SelectOperation(Operation operation)
        {
            OperationNode node = Nodes.OfType<JobNode>()
                .SelectMany(j => j.Children.OfType<OperationNode>())
                .FirstOrDefault(n => ReferenceEquals(n.Operation, operation));

            if (node != null)
            {
                node.IsSelected = true;
                SelectedNode = node;
            }
        }

        /// <summary>The job holding an operation node, or null if there is not one.</summary>
        private Job JobOf(OperationNode node)
        {
            if (node?.Operation == null)
            {
                return null;
            }

            return Nodes.OfType<JobNode>()
                .FirstOrDefault(j => j.Job.Operations.Contains(node.Operation))
                ?.Job;
        }

        /// <summary>
        /// Says the document now has unsaved CAM changes.
        /// </summary>
        /// <remarks>
        /// Every mutation above ends here. The jobs live inside the part, and SOLIDWORKS
        /// only writes them during a save it has already decided to do - so an edit that
        /// forgets this is an edit the user loses at close without ever being asked.
        /// </remarks>
        private void Changed() => _editor?.DocumentChanged();

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
