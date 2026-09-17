using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using GCam.Core.Diagnostics;
using GCam.Core.Model;
using GCam.Core.Strategies;

namespace GCam.Core.Generation
{
    /// <summary>
    /// Runs operations through their strategies, one at a time, in tree order.
    /// </summary>
    /// <remarks>
    /// **Generation is explicit.** Editing an operation marks it stale and leaves the old
    /// toolpath on screen; nothing here runs until someone asks for it.
    ///
    /// **Nothing here creates a thread.** The caller runs this on a worker and the work
    /// stays off SOLIDWORKS' STA thread, which is the rule in docs/architecture.md. Core
    /// spawning its own threads would make the same rule harder to see.
    ///
    /// **A failure is a state, not an exception out of here.** Generating a whole job is
    /// exactly when failures come in groups, and a dialog per failure - or worse, an
    /// exception that abandons the remaining operations - would be useless. Each operation
    /// gets its outcome and the run carries on. The only thing that stops a run early is
    /// cancellation.
    ///
    /// **A failed operation keeps its previous toolpath.** A retry that fails must never
    /// lose a path that was already proven on a machine.
    /// </remarks>
    public sealed class GenerationQueue
    {
        private readonly StrategyCatalog _catalog;
        private readonly IGenerationContextFactory _contexts;
        private readonly IGCamLog _log;

        public GenerationQueue(
            StrategyCatalog catalog, IGenerationContextFactory contexts, IGCamLog log = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));
            _log = log ?? NullLog.Instance;
        }

        /// <summary>Generates one operation.</summary>
        public GenerationResult Generate(
            Job job,
            Operation operation,
            IProgress<GenerationProgress> progress = null,
            CancellationToken cancellation = default)
        {
            return Run(new[] { new Item(job, operation) }, progress, cancellation);
        }

        /// <summary>Generates a chosen few of one job's operations, in the order given.</summary>
        /// <remarks>
        /// What a multiple selection in the job tree asks for. The caller passes them in
        /// tree order, because an operation generated before the one that machines away
        /// what it left behind is the order the result has to be read in - the same reason
        /// <see cref="Generate(Job, IProgress{GenerationProgress}, CancellationToken)"/>
        /// runs a job in tree order rather than in any order it likes.
        /// </remarks>
        public GenerationResult Generate(
            Job job,
            IEnumerable<Operation> operations,
            IProgress<GenerationProgress> progress = null,
            CancellationToken cancellation = default)
        {
            IEnumerable<Operation> chosen = operations ?? new List<Operation>();

            return Run(chosen.Select(o => new Item(job, o)).ToList(), progress, cancellation);
        }

        /// <summary>Generates a whole job, in tree order.</summary>
        public GenerationResult Generate(
            Job job,
            IProgress<GenerationProgress> progress = null,
            CancellationToken cancellation = default)
        {
            IEnumerable<Operation> operations = job?.Operations ?? new List<Operation>();

            return Run(operations.Select(o => new Item(job, o)).ToList(), progress, cancellation);
        }

        /// <summary>Generates every job in the part, in order.</summary>
        public GenerationResult GenerateAll(
            JobDocument document,
            IProgress<GenerationProgress> progress = null,
            CancellationToken cancellation = default)
        {
            var items = new List<Item>();

            foreach (Job job in document?.Jobs ?? new List<Job>())
            {
                items.AddRange(
                    (job.Operations ?? new List<Operation>()).Select(o => new Item(job, o)));
            }

            return Run(items, progress, cancellation);
        }

        private GenerationResult Run(
            IReadOnlyList<Item> items,
            IProgress<GenerationProgress> progress,
            CancellationToken cancellation)
        {
            int generated = 0;
            int failed = 0;
            int skipped = 0;
            bool cancelled = false;
            var failures = new List<Operation>();

            for (int i = 0; i < items.Count; i++)
            {
                Item item = items[i];
                Operation operation = item.Operation;

                if (operation == null)
                {
                    continue;
                }

                if (!operation.Enabled)
                {
                    // Suppressed: it keeps whatever toolpath it had and posts nothing.
                    skipped++;
                    continue;
                }

                if (cancellation.IsCancellationRequested)
                {
                    cancelled = true;
                    break;
                }

                Report(progress, operation, i, items.Count, 0);

                Outcome outcome = GenerateOne(item, i, items.Count, progress, cancellation);

                switch (outcome)
                {
                    case Outcome.Generated:
                        generated++;
                        break;

                    case Outcome.Failed:
                        failed++;
                        failures.Add(operation);
                        break;

                    case Outcome.Cancelled:
                        cancelled = true;
                        break;
                }

                if (outcome == Outcome.Cancelled)
                {
                    break;
                }
            }

            Report(progress, null, items.Count, items.Count, 1);

            return new GenerationResult(generated, failed, skipped, cancelled, failures);
        }

        private Outcome GenerateOne(
            Item item,
            int index,
            int total,
            IProgress<GenerationProgress> progress,
            CancellationToken cancellation)
        {
            Operation operation = item.Operation;
            OperationState before = operation.State;

            operation.State = OperationState.Generating;
            operation.StateMessage = null;

            try
            {
                if (!_catalog.TryGet(operation.Strategy, out StrategyDescriptor descriptor))
                {
                    throw new GCamUserException(
                        $"This version of G-CAM does not have a '{operation.Strategy}' strategy.");
                }

                if (!descriptor.HasStrategy)
                {
                    throw new GCamUserException(
                        $"'{descriptor.DisplayName}' cannot generate toolpaths yet.");
                }

                GenerationContext context = _contexts.Create(item.Job, operation);
                IToolpathStrategy strategy = descriptor.CreateStrategy();

                var inner = new ForwardProgress(
                    fraction => Report(progress, operation, index, total, fraction));

                Toolpath path = strategy.Generate(context, inner, cancellation)
                                ?? new Toolpath();

                cancellation.ThrowIfCancellationRequested();

                operation.Toolpath = path;

                if (path.IsEmpty)
                {
                    // Not a failure: an operation can legitimately have nothing to cut.
                    // Silence here would look like success with an invisible result.
                    operation.State = OperationState.Warning;
                    operation.StateMessage = "Generated nothing to cut.";
                }
                else
                {
                    operation.State = OperationState.Generated;
                }

                return Outcome.Generated;
            }
            catch (OperationCanceledException)
            {
                // A path that was already there is still there - it is just out of date
                // again, which is what it was before this run started.
                operation.State = operation.Toolpath != null
                    ? OperationState.Stale
                    : OperationState.NotGenerated;
                operation.StateMessage = null;

                return Outcome.Cancelled;
            }
            catch (GCamUserException ex)
            {
                Fail(operation, ex.Message);
                _log.Warn("Generating '{0}' failed: {1}", operation.Name, ex.Message);

                return Outcome.Failed;
            }
            catch (Exception ex)
            {
                // A bug in a strategy, not something the user did. Say so plainly rather
                // than showing a stack trace, and put the detail in the log.
                Fail(operation, "Generation failed unexpectedly. See the log for details.");
                _log.Error(ex, "Unhandled exception generating '{0}'", operation.Name);

                return Outcome.Failed;
            }
            finally
            {
                if (operation.State == OperationState.Generating)
                {
                    // Nothing above claimed it. Leaving it "generating" forever would be
                    // the one state the UI can never clear.
                    operation.State = before;
                }
            }
        }

        private static void Fail(Operation operation, string message)
        {
            // The previous toolpath stays exactly where it is.
            operation.State = OperationState.Failed;
            operation.StateMessage = message;
        }

        private static void Report(
            IProgress<GenerationProgress> progress,
            Operation operation,
            int completed,
            int total,
            double fraction)
        {
            progress?.Report(new GenerationProgress(operation, completed, total, fraction));
        }

        /// <summary>
        /// Passes a strategy's progress straight out, on the thread that reported it.
        /// </summary>
        /// <remarks>
        /// Deliberately not <see cref="Progress{T}"/>, which posts to whatever
        /// synchronisation context captured it when it was constructed. Here that is the
        /// worker thread the queue runs on, so reports would arrive late, out of order, or
        /// after the run they describe had finished. Marshalling to the UI thread is the
        /// caller's decision and belongs in the caller's own <see cref="IProgress{T}"/>.
        /// </remarks>
        private sealed class ForwardProgress : IProgress<double>
        {
            private readonly Action<double> _onReport;

            public ForwardProgress(Action<double> onReport) => _onReport = onReport;

            public void Report(double value) => _onReport(value);
        }

        private enum Outcome
        {
            Generated,
            Failed,
            Cancelled,
        }

        private struct Item
        {
            public Item(Job job, Operation operation)
            {
                Job = job;
                Operation = operation;
            }

            public Job Job { get; }

            public Operation Operation { get; }
        }
    }
}
