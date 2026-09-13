using System;
using System.Collections.Generic;
using System.Linq;
using GCam.Core.Model;
using GCam.Core.Model.Heights;
using GCam.Core.Strategies;
using GCam.Core.Strategies.Contour2d;
using Xunit;

namespace GCam.Core.Tests.Model
{
    public class OperationTests
    {
        // Stock from Z -5 to Z 30, model from Z 0 to Z 25.
        private static HeightContext Context() => new HeightContext(30, -5, 25, 0);

        private static Operation Usable()
        {
            var settings = new Contour2dSettings();
            settings.Contours.Add(new GeometryRef { PersistentId = "edge-1", DisplayName = "Edge1" });

            return new Operation(settings)
            {
                Name = "2D Contour1",
                ToolId = "tool-4",
            };
        }

        [Fact]
        public void An_operation_cannot_exist_without_a_strategy()
        {
            // The strategy is fixed at construction, so there is no window in which an
            // operation has no settings and something has to cope with null.
            Assert.Throws<ArgumentNullException>(() => new Operation(null));
        }

        [Fact]
        public void The_strategy_is_read_from_the_settings_so_the_two_cannot_disagree()
        {
            Assert.Equal(StrategyId.Contour2d, new Operation(new Contour2dSettings()).Strategy);
        }

        [Fact]
        public void A_new_operation_is_enabled_and_ungenerated()
        {
            var operation = new Operation(new Contour2dSettings());

            Assert.True(operation.Enabled);
            Assert.Equal(OperationState.NotGenerated, operation.State);
            Assert.False(operation.HasUsableToolpath);
            Assert.NotEqual(string.Empty, operation.Id);
        }

        [Theory]
        [InlineData(OperationState.Generated, true)]
        [InlineData(OperationState.Warning, true)]
        [InlineData(OperationState.Stale, false)]
        [InlineData(OperationState.Failed, false)]
        [InlineData(OperationState.NotGenerated, false)]
        [InlineData(OperationState.Generating, false)]
        public void Only_a_generated_or_warned_operation_has_a_usable_toolpath(
            OperationState state, bool usable)
        {
            // Stale is the interesting one: there is a toolpath, and it must not be
            // treated as current.
            var operation = new Operation(new Contour2dSettings()) { State = state };

            Assert.Equal(usable, operation.HasUsableToolpath);
        }

        [Fact]
        public void A_usable_operation_has_no_problems()
        {
            Assert.Empty(Usable().Validate(Context()));
        }

        [Fact]
        public void An_operation_with_no_name_or_tool_says_so()
        {
            var operation = new Operation(new Contour2dSettings());

            IReadOnlyList<string> problems = operation.Validate();

            Assert.Contains(problems, p => p.Contains("name"));
            Assert.Contains(problems, p => p.Contains("tool"));
        }

        [Fact]
        public void A_tolerance_of_zero_is_reported()
        {
            Operation operation = Usable();
            operation.Tolerance = 0;

            Assert.Contains(operation.Validate(Context()), p => p.Contains("Tolerance"));
        }

        [Fact]
        public void Validation_includes_the_strategys_own_problems()
        {
            // An empty contour selection is a contour2d rule, not a base one, and it still
            // has to reach the user through the operation.
            var operation = new Operation(new Contour2dSettings())
            {
                Name = "2D Contour1",
                ToolId = "tool-4",
            };

            Assert.Contains(operation.Validate(Context()), p => p.Contains("contour"));
        }

        [Fact]
        public void Heights_are_checked_when_the_extents_are_known()
        {
            Operation operation = Usable();
            operation.Heights.Clearance = new HeightSetting(HeightMode.FromStockTop, -50);

            Assert.Contains(operation.Validate(Context()), p => p.Contains("Clearance height"));
        }

        [Fact]
        public void Heights_are_skipped_when_the_extents_are_not_available_yet()
        {
            // A page being edited before geometry has been resolved is a normal state, not
            // a broken operation - and inventing extents would report invented problems.
            Operation operation = Usable();
            operation.Heights.Clearance = new HeightSetting(HeightMode.FromStockTop, -50);

            Assert.Empty(operation.Validate());
        }

        [Fact]
        public void Cloning_keeps_the_id_and_copies_everything_else()
        {
            Operation original = Usable();
            original.Comment = "Finish pass";
            original.Cutting.SpindleRpm = 7500;
            original.Heights.Bottom.Offset = -0.5;
            original.Frame.InheritFromJob = false;
            original.State = OperationState.Generated;
            original.Extra["hsm.origin"] = "imported";

            Operation copy = original.Clone();

            Assert.Equal(original.Id, copy.Id);
            Assert.Equal(OperationState.Generated, copy.State);
            Assert.Equal("imported", copy.Extra["hsm.origin"]);

            copy.Cutting.SpindleRpm = 1;
            copy.Heights.Bottom.Offset = 1;
            copy.Frame.InheritFromJob = true;
            copy.Extra["hsm.origin"] = "changed";
            ((Contour2dSettings)copy.Settings).Contours.Clear();

            Assert.Equal(7500, original.Cutting.SpindleRpm, 9);
            Assert.Equal(-0.5, original.Heights.Bottom.Offset, 9);
            Assert.False(original.Frame.InheritFromJob);
            Assert.Equal("imported", original.Extra["hsm.origin"]);
            Assert.Single(((Contour2dSettings)original.Settings).Contours);
        }

        [Fact]
        public void A_copy_that_stands_on_its_own_gets_a_new_id_and_no_toolpath()
        {
            // The copy has not been generated, whatever the original claims - otherwise it
            // would inherit a result that was computed for something else.
            Operation original = Usable();
            original.State = OperationState.Generated;
            original.StateMessage = "took 4s";

            Operation copy = original.CloneAsNew();

            Assert.NotEqual(original.Id, copy.Id);
            Assert.Equal(OperationState.NotGenerated, copy.State);
            Assert.Null(copy.StateMessage);
            Assert.Equal(original.Name, copy.Name);
        }

        [Fact]
        public void A_duplicated_job_still_copies_its_operations_deeply()
        {
            // Job.Clone goes through Operation.Clone, so the strategy settings have to
            // survive the trip.
            var job = new Job { Name = "Roughing" };
            job.Operations.Add(Usable());

            Job copy = job.CloneAsNew();
            var copied = (Contour2dSettings)copy.Operations.Single().Settings;
            copied.StockToLeave = 5;

            Assert.Equal(0, ((Contour2dSettings)job.Operations.Single().Settings).StockToLeave, 9);
            Assert.NotEqual(job.Operations.Single().Id, copy.Operations.Single().Id);
        }

        [Fact]
        public void The_frame_inherits_the_job_until_it_is_told_not_to()
        {
            var operation = new Operation(new Contour2dSettings());

            Assert.True(operation.Frame.InheritFromJob);
            Assert.Equal("From job", operation.Frame.DisplayName);

            operation.Frame.InheritFromJob = false;
            Assert.Equal("Part origin", operation.Frame.DisplayName);

            operation.Frame.CoordinateSystemName = "Coordinate System2";
            Assert.Equal("Coordinate System2", operation.Frame.DisplayName);
        }
    }
}
