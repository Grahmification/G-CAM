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
    /// The G-CAM tab's tree: the part, the jobs in it, what is selected, and what the
    /// context menu does to them.
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

        /// <summary>
        /// What the part is called, asked for again on every rebuild.
        /// </summary>
        /// <remarks>
        /// A callback rather than a string, because the name belongs to SOLIDWORKS and this
        /// project cannot reach it. Asking again each time is what makes a Save As follow
        /// without anything having to subscribe to a rename.
        /// </remarks>
        private readonly Func<string> _partName;

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
            IJobPreview preview = null,
            Func<string> partName = null)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            _editor = editor;
            _preview = preview;
            _log = log ?? NullLog.Instance;
            _partName = partName;

            Refresh();
        }

        /// <summary>
        /// What the tree shows, which is one <see cref="PartNode"/> and everything under it.
        /// </summary>
        /// <remarks>
        /// A collection of one rather than the part node on its own, because that is what a
        /// <c>TreeView</c> binds its <c>ItemsSource</c> to.
        /// </remarks>
        public ObservableCollection<JobTreeNode> Nodes { get; } = new ObservableCollection<JobTreeNode>();

        /// <summary>The row standing for the part. Always there, jobs or no jobs.</summary>
        public PartNode Part => Nodes.OfType<PartNode>().FirstOrDefault();

        /// <summary>The job rows, which are the part row's children.</summary>
        private IEnumerable<JobNode> JobNodes =>
            Part?.Children.OfType<JobNode>() ?? Enumerable.Empty<JobNode>();

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

            AddVisible(Nodes, rows);

            return rows;
        }

        private static void AddVisible(IEnumerable<JobTreeNode> rows, List<JobTreeNode> into)
        {
            foreach (JobTreeNode row in rows)
            {
                into.Add(row);

                if (row.IsExpanded)
                {
                    AddVisible(row.Children, into);
                }
            }
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

            // The part row is one of a kind, so it is remembered as a flag rather than by
            // an id. There is nothing else it could be restored onto.
            bool partSelected = _selection.Items.OfType<PartNode>().Any();
            bool partAnchored = SelectedNode is PartNode;

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

                var part = new PartNode(_partName?.Invoke());

                foreach (Job job in _document.Jobs)
                {
                    var node = new JobNode(job) { IsDefault = _document.IsDefault(job) };

                    foreach (Operation operation in job.Operations)
                    {
                        node.Children.Add(new OperationNode(operation));
                    }

                    part.Children.Add(node);
                }

                Nodes.Add(part);

                RestoreSelection(
                    selectedJobs,
                    selectedOperations,
                    partSelected,
                    partAnchored,
                    anchorJob,
                    anchorOperation,
                    anchorFallbackJob);
            }
            finally
            {
                _rebuilding = false;
            }

            AfterSelectionChanged(true);
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
            bool partSelected,
            bool partAnchored,
            string anchorJob,
            string anchorOperation,
            string anchorFallbackJob)
        {
            List<JobTreeNode> survivors = AllRows()
                .Where(Survives)
                .ToList();

            JobTreeNode anchor = partAnchored ? Part : Find(anchorJob, anchorOperation);

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

            bool Survives(JobTreeNode row)
            {
                switch (row)
                {
                    case PartNode _:
                        return partSelected;

                    case JobNode job:
                        return jobIds.Contains(job.Job.Id, StringComparer.Ordinal);

                    case OperationNode operation:
                        return operationIds.Contains(operation.Operation.Id, StringComparer.Ordinal);

                    default:
                        return false;
                }
            }
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

            return JobNodes.FirstOrDefault(
                n => string.Equals(n.Job.Id, jobId, StringComparison.Ordinal));
        }

        // ---- The part --------------------------------------------------------

        /// <summary>Creates a job for this part.</summary>
        public void NewJob() => _editor?.NewJob();

        /// <summary>Computes the toolpaths for every job in the part.</summary>
        public void GenerateAll() => _editor?.GenerateAll();

        /// <summary>True when the part has a job to generate.</summary>
        public bool HasJobs => _document.Jobs.Count > 0;

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

            foreach (JobNode job in JobNodes)
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

        // ---- Reordering ------------------------------------------------------

        /// <summary>
        /// Moves an operation to a position in a job - its own, or another one.
        /// </summary>
        /// <remarks>
        /// The destination is the row it lands in front of, or null for last - see
        /// <see cref="JobDocument.MoveOperation"/>, which is where that is worked out.
        ///
        /// <b>Which toolpaths this invalidates is the whole reason it is not a list
        /// shuffle.</b> Order decides what stock an operation meets, so everything in the
        /// job that rest-machines is suspect; and an operation that has changed jobs was
        /// computed in the coordinate system of the job it left, so its own path is worth
        /// nothing until it runs again. Both jobs are told, because the one it left has a
        /// gap in it now.
        /// </remarks>
        public bool MoveOperation(OperationNode node, JobNode target, OperationNode before)
        {
            Job source = JobOf(node);

            if (source == null || target?.Job == null)
            {
                return false;
            }

            if (!_document.MoveOperation(node.Operation, target.Job, before?.Operation))
            {
                return false;
            }

            if (ReferenceEquals(source, target.Job))
            {
                _log.Info("Moved operation '{0}' within job '{1}'.", node.Operation.Name, source.Name);
                Staleness.OperationOrderChanged(source);
            }
            else
            {
                _log.Info(
                    "Moved operation '{0}' from job '{1}' to '{2}'.",
                    node.Operation.Name, source.Name, target.Job.Name);

                // Its own path was computed in the frame of the job it has left.
                Staleness.OperationEdited(target.Job, node.Operation);
                Staleness.OperationOrderChanged(source);
            }

            Changed();
            Refresh();
            SelectOperation(node.Operation);

            return true;
        }

        /// <summary>
        /// Moves a job to a position in the part's job order.
        /// </summary>
        /// <remarks>
        /// Nothing goes stale. A job is a self-contained setup - its own stock, its own
        /// coordinate system - and no rule in <see cref="Staleness"/> reaches across one,
        /// because rest machining between jobs is not modelled. If it ever is, this is
        /// where the call goes.
        /// </remarks>
        public bool MoveJob(JobNode node, JobNode before)
        {
            if (node?.Job == null || !_document.MoveJob(node.Job, before?.Job))
            {
                return false;
            }

            _log.Info("Moved job '{0}'.", node.Job.Name);

            Changed();
            Refresh();
            SelectJob(node.Job);

            return true;
        }

        /// <summary>
        /// Marks where a drop would land, clearing every other row's mark.
        /// </summary>
        /// <remarks>
        /// One call rather than a set and a clear, because a drag moves between rows
        /// continuously and two marks on screen at once is the bug that would produce.
        /// Passing null clears them all, which is what leaving the tree or finishing the
        /// drag amounts to.
        /// </remarks>
        public void ShowDropAt(JobTreeNode node, DropIndicator where)
        {
            foreach (JobTreeNode row in AllNodes())
            {
                row.Drop = ReferenceEquals(row, node) ? where : DropIndicator.None;
            }
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

            Select(JobNodes.Where(n => wanted.Any(j => ReferenceEquals(n.Job, j))));
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
        public JobNode JobNodeOf(JobTreeNode node)
        {
            if (node == null)
            {
                return null;
            }

            if (node is JobNode job)
            {
                return job;
            }

            return JobNodes.FirstOrDefault(j => j.Children.Contains(node));
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

        private IEnumerable<JobTreeNode> AllNodes() => Walk(Nodes);

        private static IEnumerable<JobTreeNode> Walk(IEnumerable<JobTreeNode> rows)
        {
            foreach (JobTreeNode row in rows)
            {
                yield return row;

                foreach (JobTreeNode child in Walk(row.Children))
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
