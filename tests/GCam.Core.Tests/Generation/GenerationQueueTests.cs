using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using GCam.Core.Diagnostics;
using GCam.Core.Generation;
using GCam.Core.Geometry.Primitives;
using GCam.Core.Model;
using GCam.Core.Model.Heights;
using GCam.Core.Strategies;
using GCam.Core.Strategies.Contour2d;
using GCam.Core.Tooling;
using Xunit;

namespace GCam.Core.Tests.Generation
{
    public class GenerationQueueTests
    {
        private static readonly StrategyId FakeId = new StrategyId("fake");

        private sealed class FakeSettings : StrategySettings
        {
            public override StrategyId Strategy => FakeId;

            public override StrategySettings Clone() => new FakeSettings();

            public override void WriteParameters(ParameterBag bag) { }

            public override void ReadParameters(ParameterBag bag) { }
        }

        /// <summary>A strategy that does whatever the test tells it to.</summary>
        private sealed class FakeStrategy : IToolpathStrategy
        {
            public StrategyId Id => FakeId;

            public Func<GenerationContext, Toolpath> OnGenerate { get; set; }

            public int Calls { get; private set; }

            public Toolpath Generate(
                GenerationContext context, IProgress<double> progress, CancellationToken cancellation)
            {
                Calls++;
                progress?.Report(0.5);
                cancellation.ThrowIfCancellationRequested();

                return OnGenerate?.Invoke(context) ?? TwoMovePath();
            }
        }

        private sealed class FakeContexts : IGenerationContextFactory
        {
            public Func<Job, Operation, GenerationContext> OnCreate { get; set; }

            public GenerationContext Create(Job job, Operation operation)
            {
                if (OnCreate != null)
                {
                    return OnCreate(job, operation);
                }

                return new GenerationContext(
                    job,
                    operation,
                    new Tool { Id = "tool-4", Number = 4 },
                    new ResolvedHeights(40, 35, 32, 30, 0),
                    new Bounds(new Vec3(0, 0, 0), new Vec3(100, 60, 30)));
            }
        }

        private static Toolpath TwoMovePath()
        {
            return new Toolpath()
                .Add(Move.Rapid(new Vec3(0, 0, 10)))
                .Add(Move.Cut(new Vec3(10, 0, 0), 500));
        }

        private sealed class Fixture
        {
            public FakeStrategy Strategy { get; } = new FakeStrategy();

            public FakeContexts Contexts { get; } = new FakeContexts();

            public StrategyCatalog Catalog { get; } = new StrategyCatalog();

            public JobDocument Document { get; } = new JobDocument();

            public Fixture(bool registerStrategy = true)
            {
                Catalog.Register(new StrategyDescriptor(
                    FakeId,
                    "Fake",
                    () => new FakeSettings(),
                    registerStrategy ? (Func<IToolpathStrategy>)(() => Strategy) : null));
            }

            public GenerationQueue Queue() => new GenerationQueue(Catalog, Contexts, NullLog.Instance);

            public Operation AddOperation(Job job, string name)
            {
                var operation = new Operation(new FakeSettings()) { Name = name, ToolId = "tool-4" };
                job.Operations.Add(operation);
                return operation;
            }
        }

        [Fact]
        public void A_generated_operation_gets_its_toolpath_and_says_so()
        {
            var f = new Fixture();
            Job job = f.Document.AddNew();
            Operation operation = f.AddOperation(job, "Op 1");

            GenerationResult result = f.Queue().Generate(job, operation);

            Assert.Equal(OperationState.Generated, operation.State);
            Assert.Equal(2, operation.Toolpath.Moves.Count);
            Assert.Equal(1, result.Generated);
            Assert.True(result.AllSucceeded);
        }

        [Fact]
        public void A_whole_job_runs_in_tree_order()
        {
            var f = new Fixture();
            Job job = f.Document.AddNew();
            f.AddOperation(job, "First");
            f.AddOperation(job, "Second");
            f.AddOperation(job, "Third");

            var order = new List<string>();
            f.Strategy.OnGenerate = context =>
            {
                order.Add(context.Operation.Name);
                return TwoMovePath();
            };

            f.Queue().Generate(job);

            Assert.Equal(new[] { "First", "Second", "Third" }, order);
        }

        [Fact]
        public void Every_job_in_the_part_can_be_generated_at_once()
        {
            var f = new Fixture();
            Job first = f.Document.AddNew();
            Job second = f.Document.AddNew();
            f.AddOperation(first, "A");
            f.AddOperation(second, "B");

            GenerationResult result = f.Queue().GenerateAll(f.Document);

            Assert.Equal(2, result.Generated);
        }

        [Fact]
        public void A_disabled_operation_is_skipped_and_keeps_what_it_had()
        {
            var f = new Fixture();
            Job job = f.Document.AddNew();
            Operation operation = f.AddOperation(job, "Suppressed");
            operation.Enabled = false;
            operation.State = OperationState.Generated;
            Toolpath existing = operation.Toolpath = TwoMovePath();

            GenerationResult result = f.Queue().Generate(job);

            Assert.Equal(1, result.Skipped);
            Assert.Equal(0, f.Strategy.Calls);
            Assert.Same(existing, operation.Toolpath);
            Assert.Equal(OperationState.Generated, operation.State);
        }

        [Fact]
        public void A_failure_is_recorded_on_the_operation_and_the_run_carries_on()
        {
            // Generating a whole job is exactly when failures come in groups.
            var f = new Fixture();
            Job job = f.Document.AddNew();
            Operation bad = f.AddOperation(job, "Bad");
            Operation good = f.AddOperation(job, "Good");

            f.Strategy.OnGenerate = context =>
                context.Operation.Name == "Bad"
                    ? throw new GCamUserException("The tool is too big for this pocket.")
                    : TwoMovePath();

            GenerationResult result = f.Queue().Generate(job);

            Assert.Equal(OperationState.Failed, bad.State);
            Assert.Contains("too big", bad.StateMessage);
            Assert.Equal(OperationState.Generated, good.State);
            Assert.Equal(1, result.Failed);
            Assert.Equal(1, result.Generated);
            Assert.Same(bad, Assert.Single(result.Failures));
        }

        [Fact]
        public void A_failed_retry_keeps_the_toolpath_that_was_already_proven()
        {
            var f = new Fixture();
            Job job = f.Document.AddNew();
            Operation operation = f.AddOperation(job, "Op 1");

            f.Queue().Generate(job, operation);
            Toolpath proven = operation.Toolpath;

            f.Strategy.OnGenerate = _ => throw new GCamUserException("No longer possible.");
            f.Queue().Generate(job, operation);

            Assert.Equal(OperationState.Failed, operation.State);
            Assert.Same(proven, operation.Toolpath);
        }

        [Fact]
        public void A_bug_in_a_strategy_is_reported_plainly_rather_than_as_a_stack_trace()
        {
            var f = new Fixture();
            Job job = f.Document.AddNew();
            Operation operation = f.AddOperation(job, "Op 1");

            f.Strategy.OnGenerate = _ => throw new InvalidOperationException("index out of range");

            GenerationResult result = f.Queue().Generate(job, operation);

            Assert.Equal(OperationState.Failed, operation.State);
            Assert.DoesNotContain("index out of range", operation.StateMessage);
            Assert.Contains("log", operation.StateMessage);
            Assert.Equal(1, result.Failed);
        }

        [Fact]
        public void A_strategy_with_no_implementation_yet_fails_with_a_readable_message()
        {
            // Settings, a page and persistence all exist before an algorithm does.
            var f = new Fixture(registerStrategy: false);
            Job job = f.Document.AddNew();
            Operation operation = f.AddOperation(job, "Op 1");

            f.Queue().Generate(job, operation);

            Assert.Equal(OperationState.Failed, operation.State);
            Assert.Contains("cannot generate toolpaths yet", operation.StateMessage);
        }

        [Fact]
        public void An_unknown_strategy_fails_that_operation_and_nothing_else()
        {
            var f = new Fixture();
            Job job = f.Document.AddNew();
            var stranger = new Operation(new Contour2dSettings()) { Name = "Stranger" };
            job.Operations.Add(stranger);
            Operation good = f.AddOperation(job, "Good");

            f.Queue().Generate(job);

            Assert.Equal(OperationState.Failed, stranger.State);
            Assert.Equal(OperationState.Generated, good.State);
        }

        [Fact]
        public void A_context_that_cannot_be_built_fails_the_operation_with_its_message()
        {
            var f = new Fixture();
            Job job = f.Document.AddNew();
            Operation operation = f.AddOperation(job, "Op 1");

            f.Contexts.OnCreate = (_, __) =>
                throw new GCamUserException("Choose a tool for this operation.");

            f.Queue().Generate(job, operation);

            Assert.Equal(OperationState.Failed, operation.State);
            Assert.Contains("Choose a tool", operation.StateMessage);
            Assert.Equal(0, f.Strategy.Calls);
        }

        [Fact]
        public void An_empty_result_is_a_warning_rather_than_silent_success()
        {
            var f = new Fixture();
            Job job = f.Document.AddNew();
            Operation operation = f.AddOperation(job, "Op 1");

            f.Strategy.OnGenerate = _ => new Toolpath();

            f.Queue().Generate(job, operation);

            Assert.Equal(OperationState.Warning, operation.State);
            Assert.Contains("nothing to cut", operation.StateMessage);
        }

        [Fact]
        public void Cancelling_stops_the_run_and_leaves_the_rest_alone()
        {
            var f = new Fixture();
            Job job = f.Document.AddNew();
            Operation first = f.AddOperation(job, "First");
            Operation second = f.AddOperation(job, "Second");

            var cancellation = new CancellationTokenSource();
            f.Strategy.OnGenerate = _ =>
            {
                cancellation.Cancel();
                return TwoMovePath();
            };

            GenerationResult result = f.Queue().Generate(job, null, cancellation.Token);

            Assert.True(result.Cancelled);
            Assert.False(result.AllSucceeded);
            Assert.Equal(OperationState.NotGenerated, first.State);
            Assert.Equal(OperationState.NotGenerated, second.State);
            Assert.Equal(1, f.Strategy.Calls);
        }

        [Fact]
        public void Cancelling_a_regeneration_leaves_the_previous_path_stale_not_missing()
        {
            // There is still a toolpath; it is out of date, which is what it was before
            // the cancelled run started.
            var f = new Fixture();
            Job job = f.Document.AddNew();
            Operation operation = f.AddOperation(job, "Op 1");

            f.Queue().Generate(job, operation);
            Toolpath existing = operation.Toolpath;

            var cancellation = new CancellationTokenSource();
            f.Strategy.OnGenerate = _ =>
            {
                cancellation.Cancel();
                return TwoMovePath();
            };

            f.Queue().Generate(job, operation, null, cancellation.Token);

            Assert.Equal(OperationState.Stale, operation.State);
            Assert.Same(existing, operation.Toolpath);
        }

        [Fact]
        public void Progress_spans_the_whole_run_not_each_operation_in_turn()
        {
            var f = new Fixture();
            Job job = f.Document.AddNew();
            f.AddOperation(job, "First");
            f.AddOperation(job, "Second");

            var reports = new List<GenerationProgress>();
            f.Queue().Generate(job, new SynchronousProgress(reports.Add));

            Assert.NotEmpty(reports);
            Assert.Equal(2, reports.Max(r => r.Total));
            Assert.Equal(1.0, reports.Last().Fraction, 6);

            // Halfway through the first of two operations is a quarter of the run.
            Assert.Contains(reports, r => Math.Abs(r.Fraction - 0.25) < 1e-6);
        }

        [Fact]
        public void Progress_carries_the_operation_percentage_for_the_tree()
        {
            var f = new Fixture();
            Job job = f.Document.AddNew();
            f.AddOperation(job, "Op 1");

            var reports = new List<GenerationProgress>();
            f.Queue().Generate(job, new SynchronousProgress(reports.Add));

            Assert.Contains(reports, r => r.OperationPercent == 50 && r.Operation?.Name == "Op 1");
        }

        [Fact]
        public void A_job_with_no_operations_succeeds_at_doing_nothing()
        {
            var f = new Fixture();
            GenerationResult result = f.Queue().Generate(f.Document.AddNew());

            Assert.True(result.AllSucceeded);
            Assert.Equal(0, result.Generated);
        }

        [Fact]
        public void The_queue_needs_a_catalogue_and_a_context_factory()
        {
            Assert.Throws<ArgumentNullException>(
                () => new GenerationQueue(null, new FakeContexts()));
            Assert.Throws<ArgumentNullException>(
                () => new GenerationQueue(new StrategyCatalog(), null));
        }

        [Fact]
        public void Settings_are_handed_to_the_strategy_already_typed()
        {
            var f = new Fixture();
            Job job = f.Document.AddNew();
            Operation operation = f.AddOperation(job, "Op 1");

            f.Strategy.OnGenerate = context =>
            {
                Assert.IsType<FakeSettings>(context.SettingsAs<FakeSettings>());
                Assert.Throws<InvalidOperationException>(() => context.SettingsAs<Contour2dSettings>());
                return TwoMovePath();
            };

            f.Queue().Generate(job, operation);

            Assert.Equal(OperationState.Generated, operation.State);
        }

        /// <summary>
        /// Reports on the calling thread. <see cref="Progress{T}"/> posts to a
        /// synchronisation context, which in a test means the callbacks arrive after the
        /// assertions.
        /// </summary>
        private sealed class SynchronousProgress : IProgress<GenerationProgress>
        {
            private readonly Action<GenerationProgress> _onReport;

            public SynchronousProgress(Action<GenerationProgress> onReport) => _onReport = onReport;

            public void Report(GenerationProgress value) => _onReport(value);
        }
    }
}
