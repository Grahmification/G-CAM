using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GCam.Core.Abstractions;
using GCam.Core.Diagnostics;
using GCam.Core.Generation;
using GCam.Core.Model;
using GCam.Core.Rendering;
using GCam.Core.Selection;

namespace GCam.UI.ViewModels
{
    /// <summary>
    /// The G-CAM tab's tree: the jobs in one document, what is selected in it, and what
    /// the context menu does to them.
    /// </summary>
    /// <remarks>
    /// Presentation state only. Every rule - unique names, which job is the default,
    /// where the default goes when one is deleted - belongs to
    /// <see cref="JobDocument"/> in Core, where it can be tested without WPF. This class
    /// turns that model into nodes and passes intent on to an
    /// <see cref="IJobEditor"/>, because editing means a SOLIDWORKS PropertyManager page
    /// and this project cannot reach SOLIDWORKS.
    ///
    /// Selection is the same arrangement: the rules behind click, Ctrl-click and
    /// Shift-click are <see cref="MultiSelection{T}"/> in Core, and what a selection means
    /// for the 3D view is <see cref="PreviewSelection"/> beside it. What is left here is
    /// which nodes those rules are being applied to.
    /// </remarks>
    public sealed class JobTreeViewModel : ViewModelBase
    {
        private readonly JobDocument _document;
        private readonly IJobEditor _editor;
        private readonly IJobPreview _preview;
        private readonly IGCamLog _log;

        private readonly MultiSelection<JobTreeNode> _selection = new MultiSelection<JobTreeNode>();

        /// <summary>
        /// True while the tree is being rebuilt, so the 3D view is redrawn once at the end
        /// rather than for each of the selection changes a rebuild passes through.
        /// </summary>
        private bool _rebuilding;

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
        /// Everything the user has picked in the tree. Changing it is what decides what the
        /// 3D view shows.
        /// </summary>
        /// <remarks>
        /// Selection is the trigger for the preview because it is the one signal that
        /// means "this is what I am looking at" - it covers clicking a node, arrowing
        /// through the tree, and the reselection that follows a refresh, without any of
        /// them having to know the preview exists.
        /// </remarks>
        public IReadOnlyList<JobTreeNode> SelectedNodes => _selection.Items;

        /// <summary>
        /// The node a command that can only act on one thing acts on - Edit, Rename, Make
        /// Default - and the row a Shift-click measures its range from.
        /// </summary>
        public JobTreeNode SelectedNode => _selection.Anchor;

        /// <summary>The anchor when it is an operation, or null.</summary>
        public OperationNode SelectedOperationNode => SelectedNode as OperationNode;

        /// <summary>The selected operations, in tree order.</summary>
        public IReadOnlyList<OperationNode> SelectedOperationNodes =>
            InTreeOrder(_selection.Items.OfType<OperationNode>());

        /// <summary>The selected jobs, in tree order. A selected operation is not one.</summary>
        public IReadOnlyList<JobNode> SelectedJobNodes =>
            InTreeOrder(_selection.Items.OfType<JobNode>());

        public bool HasJobs => _document.Jobs.Count > 0;

        /// <summary>Shown instead of the tree when the document has no jobs.</summary>
        public string EmptyMessage => "No job in this document.";

        /// <summary>
        /// The job the anchor belongs to, or null.
        /// </summary>
        /// <remarks>
        /// An operation stands in for its parent here, so right-clicking one and choosing
        /// New Operation does the expected thing. It does <i>not</i> stand in for it in the
        /// 3D view any more: an operation shows its own toolpath, and only a job selected
        /// in its own right shows stock.
        /// </remarks>
        public JobNode SelectedJobNode => JobNodeOf(SelectedNode);

        public bool IsSelected(JobTreeNode node) => _selection.Contains(node);

        // ---- Selection -------------------------------------------------------

        /// <summary>Selects one node, dropping everything else. A plain click.</summary>
        public void SelectOnly(JobTreeNode node) => AfterSelectionChanged(_selection.Select(node));

        /// <summary>Adds a node to the selection, or takes it out again. A Ctrl-click.</summary>
        public void ToggleSelection(JobTreeNode node) => AfterSelectionChanged(_selection.Toggle(node));

        /// <summary>
        /// Selects everything between the anchor and this node. A Shift-click.
        /// </summary>
        /// <remarks>
        /// Measured down the rows the user can see, so a range over a collapsed job takes
        /// the job and not the operations hidden inside it.
        /// </remarks>
        public void ExtendSelectionTo(JobTreeNode node) =>
            AfterSelectionChanged(_selection.ExtendTo(node, VisibleOrder()));

        public void ClearSelection() => AfterSelectionChanged(_selection.Clear());

        /// <summary>
        /// The rows in the order they appear on screen, with collapsed children left out.
        /// </summary>
        public IReadOnlyList<JobTreeNode> VisibleOrder()
        {
            var rows = new List<JobTreeNode>();

            foreach (JobTreeNode node in Nodes)
            {
                rows.Add(node);

                if (node.IsExpanded)
                {
                    rows.AddRange(node.Children);
                }
            }

            return rows;
        }

        /// <summary>
        /// Puts the selection flags on the nodes, tells the view what moved, and redraws
        /// the 3D view.
        /// </summary>
        private void AfterSelectionChanged(bool changed)
        {
            if (!changed)
            {
                return;
            }

            foreach (JobTreeNode node in AllNodes())
            {
                node.IsSelected = _selection.Contains(node);
                node.IsCurrent = ReferenceEquals(node, _selection.Anchor);
            }

            Raise(nameof(SelectedNode));
            Raise(nameof(SelectedNodes));
            Raise(nameof(SelectedJobNodes));
            Raise(nameof(SelectedOperationNode));
            Raise(nameof(SelectedOperationNodes));

            UpdatePreview();
        }

        /// <summary>
        /// States the whole 3D picture: stock and an origin for each selected job, a
        /// toolpath for each selected operation, and an origin for the job it belongs to.
        /// </summary>
        /// <remarks>
        /// Which of those follows which selection is <see cref="PreviewSelection"/>'s rule,
        /// not this one's. All that happens here is the walk from nodes to the model
        /// objects underneath them.
        /// </remarks>
        private void UpdatePreview()
        {
            if (_rebuilding || _preview == null)
            {
                return;
            }

            var builder = new PreviewSelection.Builder();

            foreach (JobTreeNode node in _selection.Items)
            {
                switch (node)
                {
                    case JobNode job:
                        builder.AddJob(job.Job);
                        break;

                    case OperationNode operation:
                        builder.AddOperation(JobOf(operation), operation.Operation);
                        break;
                }
            }

            _preview.Show(builder.Build());
        }

        // ---- Refresh ---------------------------------------------------------

        /// <summary>
        /// Rebuilds every node from the model.
        /// </summary>
        /// <remarks>
        /// Wholesale rather than incrementally, the same way the tool library browser
        /// refreshes its folders. The tree is small and a rebuild cannot drift out of
        /// step with the model the way a patch can.
        ///
        /// The selection is remembered as ids and put back afterwards, because the nodes it
        /// pointed at are not the nodes that come out. A generate ends here, which is how a
        /// fresh toolpath reaches the screen.
        /// </remarks>
        public void Refresh()
        {
            List<string> selectedJobs = _selection.Items.OfType<JobNode>()
                .Select(n => n.Job.Id).ToList();

            // Held separately from the jobs' ids because an operation node is not its job
            // here: restoring the job instead would quietly move the selection up a level
            // every time an operation was renamed or suppressed.
            List<string> selectedOperations = _selection.Items.OfType<OperationNode>()
                .Select(n => n.Operation.Id).ToList();

            string anchorJob = (SelectedNode as JobNode)?.Job.Id;
            string anchorOperation = SelectedOperationNode?.Operation.Id;
            string anchorFallbackJob = SelectedJobNode?.Job.Id;

            _rebuilding = true;

            try
            {
                Nodes.Clear();

                // The nodes about to be rebuilt are not the ones being held. Dropping the
                // selection here rather than leaving it dangling is what makes a deleted
                // job take its preview with it: what follows puts back only what is still
                // there.
                _selection.Clear();

                foreach (Job job in _document.Jobs)
                {
                    var node = new JobNode(job) { IsDefault = _document.IsDefault(job) };

                    foreach (Operation operation in job.Operations)
                    {
                        node.Children.Add(new OperationNode(operation));
                    }

                    Nodes.Add(node);
                }

                RestoreSelection(
                    selectedJobs, selectedOperations, anchorJob, anchorOperation, anchorFallbackJob);
            }
            finally
            {
                _rebuilding = false;
            }

            AfterSelectionChanged(true);
            Raise(nameof(HasJobs));
        }

        /// <summary>
        /// Puts the selection back after a rebuild - on the same nodes where they are still
        /// there, otherwise on the job of whatever the user was on.
        /// </summary>
        /// <remarks>
        /// Falling back to the job is what a delete relies on: the operation the user was
        /// on has just gone, and landing on its job keeps something on screen rather than
        /// clearing the 3D view.
        /// </remarks>
        private void RestoreSelection(
            IReadOnlyCollection<string> jobIds,
            IReadOnlyCollection<string> operationIds,
            string anchorJob,
            string anchorOperation,
            string anchorFallbackJob)
        {
            List<JobTreeNode> survivors = AllRows()
                .Where(n => n is JobNode job
                    ? jobIds.Contains(job.Job.Id, StringComparer.Ordinal)
                    : operationIds.Contains(((OperationNode)n).Operation.Id, StringComparer.Ordinal))
                .ToList();

            JobTreeNode anchor = Find(anchorJob, anchorOperation);

            if (anchor == null && survivors.Count == 0)
            {
                // Everything the user was on has gone - a deleted operation, most often.
                // Its job is the nearest thing left to be looking at.
                anchor = Find(anchorFallbackJob, null);

                if (anchor != null)
                {
                    survivors.Add(anchor);
                }
            }

            // With the anchor gone but other rows still selected, SelectAll settles it on
            // the first of them rather than on nothing.
            _selection.SelectAll(survivors, anchor);
        }

        private JobTreeNode Find(string jobId, string operationId)
        {
            if (operationId != null)
            {
                return AllNodes().OfType<OperationNode>().FirstOrDefault(
                    n => string.Equals(n.Operation.Id, operationId, StringComparison.Ordinal));
            }

            if (jobId == null)
            {
                return null;
            }

            return Nodes.OfType<JobNode>().FirstOrDefault(
                n => string.Equals(n.Job.Id, jobId, StringComparison.Ordinal));
        }

        // ---- Jobs ------------------------------------------------------------

        public void EditJob(JobNode node)
        {
            if (node != null)
            {
                _editor?.EditJob(node.Job);
            }
        }

        /// <summary>
        /// Opens whichever node is current for editing - a job, or an operation.
        /// </summary>
        /// <remarks>
        /// One entry point so that double-click, Enter and the context menu all mean the
        /// same thing whichever kind of node is under them. Always one node: a property
        /// page edits one thing, so this acts on the anchor however many rows are selected.
        /// </remarks>
        public void EditSelected()
        {
            if (SelectedNode is OperationNode operation)
            {
                _editor?.EditOperation(JobOf(operation), operation.Operation);
                return;
            }

            EditJob(SelectedJobNode);
        }

        public void NewOperation(JobNode node)
        {
            _editor?.NewOperation(node?.Job ?? _document.DefaultJob);
        }

        /// <summary>Computes the toolpaths for whole jobs.</summary>
        public void GenerateJobs(IEnumerable<JobNode> nodes)
        {
            foreach (JobNode node in InTreeOrder(nodes))
            {
                if (node.Job != null)
                {
                    _editor?.GenerateJob(node.Job);
                }
            }
        }

        public void DuplicateJobs(IEnumerable<JobNode> nodes)
        {
            var copies = new List<Job>();

            foreach (JobNode node in InTreeOrder(nodes))
            {
                Job copy = _document.Duplicate(node.Job);
                _log.Info("Duplicated job '{0}' as '{1}'.", node.Job.Name, copy.Name);
                copies.Add(copy);
            }

            if (copies.Count == 0)
            {
                return;
            }

            Changed();
            Refresh();
            SelectJobs(copies);
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

        /// <summary>
        /// Computes the toolpaths for the operations the user picked out.
        /// </summary>
        /// <remarks>
        /// One call per job rather than one per operation, so a selection spanning two jobs
        /// is two runs of the queue and not six - and so each run sees its operations in
        /// tree order, which is the order their results have to be read in.
        /// </remarks>
        public void GenerateOperations(IEnumerable<OperationNode> nodes)
        {
            foreach (IGrouping<Job, OperationNode> group in ByJob(nodes))
            {
                _editor?.GenerateOperations(
                    group.Key, group.Select(n => n.Operation).ToList());
            }
        }

        /// <summary>Copies operations, landing the selection on the copies.</summary>
        public void DuplicateOperations(IEnumerable<OperationNode> nodes)
        {
            var copies = new List<Operation>();

            foreach (IGrouping<Job, OperationNode> group in ByJob(nodes))
            {
                foreach (OperationNode node in group)
                {
                    Operation copy = group.Key.DuplicateOperation(node.Operation);
                    _log.Info("Duplicated operation '{0}' as '{1}'.", node.Operation.Name, copy.Name);
                    copies.Add(copy);
                }

                // The copies sit where their originals' output used to be in the cutting
                // order, so anything rest-machining below them now sees different stock.
                Staleness.OperationOrderChanged(group.Key);
            }

            if (copies.Count == 0)
            {
                return;
            }

            Changed();
            Refresh();
            SelectOperations(copies);
        }

        /// <summary>
        /// Takes nodes out of the document - whole jobs, operations, or both at once.
        /// </summary>
        /// <remarks>
        /// There is no undo - <c>Core/Commands</c> is not built - so the caller asks first.
        /// Jobs go first, and an operation inside one of them is already gone by the time
        /// its own turn comes; removing it again would be a second delete of something that
        /// no longer exists.
        ///
        /// The toolpaths leave the 3D view through the refresh, because the preview is
        /// stated in full from the selection every time it changes.
        /// </remarks>
        public void Delete(IEnumerable<JobTreeNode> nodes)
        {
            List<JobTreeNode> targets = InTreeOrder(nodes);

            var jobs = targets.OfType<JobNode>().Select(n => n.Job).ToList();
            bool deleted = false;

            foreach (Job job in jobs)
            {
                _document.Remove(job);
                _log.Info("Deleted job '{0}'.", job.Name);
                deleted = true;
            }

            foreach (IGrouping<Job, OperationNode> group in ByJob(targets.OfType<OperationNode>()))
            {
                if (jobs.Contains(group.Key))
                {
                    continue;
                }

                foreach (OperationNode node in group)
                {
                    group.Key.RemoveOperation(node.Operation);
                    _log.Info(
                        "Deleted operation '{0}' from job '{1}'.", node.Operation.Name, group.Key.Name);
                    deleted = true;
                }

                // Whatever machined what these left behind has to be computed again.
                Staleness.OperationOrderChanged(group.Key);
            }

            if (!deleted)
            {
                return;
            }

            Changed();
            Refresh();
        }

        /// <summary>
        /// Suppresses or restores operations.
        /// </summary>
        /// <remarks>
        /// A suppressed operation keeps its toolpath and its parameters; it is skipped by
        /// generation, is not drawn even when it is selected, and posts nothing. Its own
        /// path stays trustworthy - nothing about the operation changed - but what reaches
        /// the operations below it just did, in both directions, which is what
        /// <see cref="Staleness.OperationEnabledChanged"/> covers.
        /// </remarks>
        public void SetOperationsEnabled(IEnumerable<OperationNode> nodes, bool enabled)
        {
            bool changed = false;

            foreach (IGrouping<Job, OperationNode> group in ByJob(nodes))
            {
                foreach (OperationNode node in group)
                {
                    if (node.Operation.Enabled == enabled)
                    {
                        continue;
                    }

                    node.Operation.Enabled = enabled;
                    _log.Info(
                        "{0} operation '{1}'.", enabled ? "Restored" : "Suppressed", node.Operation.Name);

                    Staleness.OperationEnabledChanged(group.Key, node.Operation);
                    changed = true;
                }
            }

            if (!changed)
            {
                return;
            }

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

        // ---- Selecting model objects -----------------------------------------

        /// <summary>
        /// Puts the selection on a job, if the tree is showing it.
        /// </summary>
        /// <remarks>
        /// Public because creating a job from the toolbar has to land on it: the job was
        /// added by the add-in rather than by this viewmodel, and leaving the tree with
        /// nothing selected right after someone set up a job's stock makes the preview
        /// look broken.
        /// </remarks>
        public void SelectJob(Job job) => SelectJobs(new[] { job });

        public void SelectJobs(IEnumerable<Job> jobs)
        {
            List<Job> wanted = (jobs ?? Enumerable.Empty<Job>()).Where(j => j != null).ToList();

            Select(Nodes.OfType<JobNode>().Where(n => wanted.Any(j => ReferenceEquals(n.Job, j))));
        }

        /// <summary>Puts the selection on an operation, if the tree is showing it.</summary>
        public void SelectOperation(Operation operation) => SelectOperations(new[] { operation });

        public void SelectOperations(IEnumerable<Operation> operations)
        {
            List<Operation> wanted =
                (operations ?? Enumerable.Empty<Operation>()).Where(o => o != null).ToList();

            Select(AllNodes().OfType<OperationNode>()
                .Where(n => wanted.Any(o => ReferenceEquals(n.Operation, o))));
        }

        private void Select(IEnumerable<JobTreeNode> nodes)
        {
            List<JobTreeNode> found = InTreeOrder(nodes);

            if (found.Count > 0)
            {
                AfterSelectionChanged(_selection.SelectAll(found));
            }
        }

        // ---- Helpers ---------------------------------------------------------

        /// <summary>The job holding an operation node, or null if there is not one.</summary>
        private Job JobOf(OperationNode node) => JobNodeOf(node)?.Job;

        /// <summary>The job node a node belongs to - itself, when it is a job.</summary>
        private JobNode JobNodeOf(JobTreeNode node)
        {
            if (node == null)
            {
                return null;
            }

            if (node is JobNode job)
            {
                return job;
            }

            return Nodes.OfType<JobNode>().FirstOrDefault(j => j.Children.Contains(node));
        }

        /// <summary>
        /// Groups operation nodes by the job they belong to, each group in tree order.
        /// </summary>
        /// <remarks>
        /// Everything done to several operations at once needs both halves of this: the
        /// job, because that is what owns the rules and the staleness, and tree order,
        /// because a selection carries the order things were clicked in and no rule about
        /// cutting cares about that.
        /// </remarks>
        private IEnumerable<IGrouping<Job, OperationNode>> ByJob(IEnumerable<OperationNode> nodes)
        {
            return InTreeOrder(nodes)
                .Select(n => new { Node = n, Job = JobOf(n) })
                .Where(x => x.Job != null)
                .GroupBy(x => x.Job, x => x.Node);
        }

        /// <summary>Whatever was passed in, ordered as it appears in the tree.</summary>
        private List<T> InTreeOrder<T>(IEnumerable<T> nodes)
            where T : JobTreeNode
        {
            if (nodes == null)
            {
                return new List<T>();
            }

            List<JobTreeNode> order = AllRows();

            return nodes.Where(n => n != null).Distinct()
                .OrderBy(n => order.IndexOf(n))
                .ToList();
        }

        /// <summary>Every row, collapsed or not, top to bottom.</summary>
        private List<JobTreeNode> AllRows() => AllNodes().ToList();

        private IEnumerable<JobTreeNode> AllNodes()
        {
            foreach (JobTreeNode node in Nodes)
            {
                yield return node;

                foreach (JobTreeNode child in node.Children)
                {
                    yield return child;
                }
            }
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
    }
}
