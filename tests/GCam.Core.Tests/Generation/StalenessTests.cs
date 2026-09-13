using System.Collections.Generic;
using System.Linq;
using GCam.Core.Generation;
using GCam.Core.Model;
using GCam.Core.Strategies;
using GCam.Core.Strategies.Contour2d;
using GCam.Core.Tooling;
using Xunit;

namespace GCam.Core.Tests.Generation
{
    public class StalenessTests
    {
        /// <summary>Stands in for adaptive clearing, which machines what is left.</summary>
        private sealed class RestMachiningSettings : StrategySettings
        {
            public override StrategyId Strategy => new StrategyId("rest");

            public override bool DependsOnPrecedingStock => true;

            public override StrategySettings Clone() => new RestMachiningSettings();

            public override void WriteParameters(ParameterBag bag) { }

            public override void ReadParameters(ParameterBag bag) { }
        }

        private static Operation Generated(string name, StrategySettings settings = null)
        {
            return new Operation(settings ?? new Contour2dSettings())
            {
                Name = name,
                State = OperationState.Generated,
                Toolpath = new Toolpath(),
            };
        }

        [Fact]
        public void A_generated_operation_goes_stale()
        {
            Operation operation = Generated("2D Contour1");

            Assert.True(Staleness.MarkStale(operation));
            Assert.Equal(OperationState.Stale, operation.State);
        }

        [Theory]
        [InlineData(OperationState.NotGenerated)]
        [InlineData(OperationState.Failed)]
        [InlineData(OperationState.Generating)]
        [InlineData(OperationState.Stale)]
        public void Only_an_operation_with_a_result_can_go_stale(OperationState state)
        {
            // "Stale" would be a downgrade from "failed" and a lie about the other two.
            var operation = new Operation(new Contour2dSettings()) { State = state };

            Assert.False(Staleness.MarkStale(operation));
            Assert.Equal(state, operation.State);
        }

        [Fact]
        public void A_warning_still_goes_stale_because_it_has_a_toolpath()
        {
            var operation = new Operation(new Contour2dSettings())
            {
                State = OperationState.Warning,
            };

            Assert.True(Staleness.MarkStale(operation));
        }

        [Fact]
        public void Editing_an_operation_marks_the_rest_machining_below_it()
        {
            var job = new Job { Name = "Job 1" };
            Operation roughing = Generated("Roughing");
            Operation rest = Generated("Rest", new RestMachiningSettings());
            Operation contour = Generated("Contour");
            job.Operations.AddRange(new[] { roughing, rest, contour });

            Staleness.OperationEdited(job, roughing);

            Assert.Equal(OperationState.Stale, roughing.State);
            Assert.Equal(OperationState.Stale, rest.State);

            // A plain contour cuts a fixed profile; what came before it is irrelevant.
            Assert.Equal(OperationState.Generated, contour.State);
        }

        [Fact]
        public void Dependents_above_the_edited_operation_are_untouched()
        {
            // Order is the dependency. What runs first cannot depend on what runs later.
            var job = new Job { Name = "Job 1" };
            Operation restAbove = Generated("Rest above", new RestMachiningSettings());
            Operation edited = Generated("Edited");
            job.Operations.AddRange(new[] { restAbove, edited });

            Staleness.OperationEdited(job, edited);

            Assert.Equal(OperationState.Generated, restAbove.State);
        }

        [Fact]
        public void Editing_a_disabled_operation_does_not_disturb_what_follows()
        {
            // It removes no material, so it changes nothing for whatever machines after.
            var job = new Job { Name = "Job 1" };
            Operation disabled = Generated("Disabled");
            disabled.Enabled = false;
            Operation rest = Generated("Rest", new RestMachiningSettings());
            job.Operations.AddRange(new[] { disabled, rest });

            Staleness.OperationEdited(job, disabled);

            Assert.Equal(OperationState.Stale, disabled.State);
            Assert.Equal(OperationState.Generated, rest.State);
        }

        [Fact]
        public void Enabling_or_disabling_an_operation_disturbs_what_follows_but_not_itself()
        {
            var job = new Job { Name = "Job 1" };
            Operation toggled = Generated("Roughing");
            Operation rest = Generated("Rest", new RestMachiningSettings());
            job.Operations.AddRange(new[] { toggled, rest });

            Staleness.OperationEnabledChanged(job, toggled);

            // Nothing about the operation itself changed, so its own path is still good.
            Assert.Equal(OperationState.Generated, toggled.State);
            Assert.Equal(OperationState.Stale, rest.State);
        }

        [Fact]
        public void Reordering_marks_every_dependent_in_the_job()
        {
            var job = new Job { Name = "Job 1" };
            Operation contour = Generated("Contour");
            Operation firstRest = Generated("Rest 1", new RestMachiningSettings());
            Operation secondRest = Generated("Rest 2", new RestMachiningSettings());
            job.Operations.AddRange(new[] { contour, firstRest, secondRest });

            Staleness.OperationOrderChanged(job);

            Assert.Equal(OperationState.Generated, contour.State);
            Assert.Equal(OperationState.Stale, firstRest.State);
            Assert.Equal(OperationState.Stale, secondRest.State);
        }

        [Fact]
        public void Changing_the_job_marks_everything_in_it()
        {
            // Stock, coordinate system, work offset, bodies - all of them reach every
            // operation's heights or extents.
            var job = new Job { Name = "Job 1" };
            job.Operations.AddRange(new[] { Generated("A"), Generated("B") });

            Staleness.JobChanged(job);

            Assert.All(job.Operations, o => Assert.Equal(OperationState.Stale, o.State));
        }

        [Fact]
        public void Editing_a_tool_reaches_every_operation_using_it_across_jobs()
        {
            var document = new JobDocument();
            Tool tool = document.AddTool(new Tool { Id = "tool-4", Number = 4 });
            Tool other = document.AddTool(new Tool { Id = "tool-7", Number = 7 });

            Job first = document.AddNew();
            Job second = document.AddNew();

            Operation a = Generated("A");
            Operation b = Generated("B");
            Operation untouched = Generated("Untouched");
            a.UseTool(tool);
            b.UseTool(tool);
            untouched.UseTool(other);

            first.Operations.Add(a);
            second.Operations.Add(b);
            second.Operations.Add(untouched);

            Staleness.ToolChanged(document, "tool-4");

            Assert.Equal(OperationState.Stale, a.State);
            Assert.Equal(OperationState.Stale, b.State);
            Assert.Equal(OperationState.Generated, untouched.State);
        }

        [Fact]
        public void A_rebuild_marks_everything_everywhere()
        {
            // The bluntest rule, deliberately: a rebuild can move any face, and working
            // out which costs as much as regenerating.
            var document = new JobDocument();
            Job first = document.AddNew();
            Job second = document.AddNew();
            first.Operations.Add(Generated("A"));
            second.Operations.Add(Generated("B"));

            Staleness.ModelRebuilt(document);

            Assert.All(
                document.Jobs.SelectMany(j => j.Operations),
                o => Assert.Equal(OperationState.Stale, o.State));
        }

        [Fact]
        public void Dependents_can_be_asked_for_without_marking_anything()
        {
            var job = new Job { Name = "Job 1" };
            Operation first = Generated("First");
            Operation rest = Generated("Rest", new RestMachiningSettings());
            job.Operations.AddRange(new[] { first, rest });

            IReadOnlyList<Operation> dependents = Staleness.DependentsBelow(job, first);

            Assert.Same(rest, Assert.Single(dependents));
            Assert.Equal(OperationState.Generated, rest.State);
        }

        [Fact]
        public void An_operation_that_is_not_in_the_job_has_no_dependents()
        {
            var job = new Job { Name = "Job 1" };
            job.Operations.Add(Generated("Rest", new RestMachiningSettings()));

            Assert.Empty(Staleness.DependentsBelow(job, Generated("Stranger")));
        }

        [Fact]
        public void Nulls_are_ignored_rather_than_throwing()
        {
            Staleness.MarkStale(null);
            Staleness.OperationEdited(null, null);
            Staleness.JobChanged(null);
            Staleness.ToolChanged(null, null);
            Staleness.ModelRebuilt(null);
            Staleness.OperationOrderChanged(null);

            Assert.Empty(Staleness.DependentsBelow(null, null));
        }
    }
}
