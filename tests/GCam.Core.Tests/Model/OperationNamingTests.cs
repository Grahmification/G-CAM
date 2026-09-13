using GCam.Core.Model;
using GCam.Core.Strategies.Contour2d;
using Xunit;

namespace GCam.Core.Tests.Model
{
    public class OperationNamingTests
    {
        private static Operation Named(string name) =>
            new Operation(new Contour2dSettings()) { Name = name };

        [Fact]
        public void The_first_operation_is_numbered_one()
        {
            Assert.Equal("2D Contour1", new Job().NextOperationName("2D Contour"));
        }

        [Fact]
        public void Names_climb_past_the_ones_in_use()
        {
            var job = new Job();
            job.Operations.Add(Named("2D Contour1"));
            job.Operations.Add(Named("2D Contour2"));

            Assert.Equal("2D Contour3", job.NextOperationName("2D Contour"));
        }

        [Fact]
        public void Gaps_are_filled_rather_than_climbed_past()
        {
            // Deleting the second of three and making a new one gives 2 back, not 4 - the
            // same rule JobDocument uses for jobs and ToolLibrary for tool numbers.
            var job = new Job();
            job.Operations.Add(Named("2D Contour1"));
            job.Operations.Add(Named("2D Contour3"));

            Assert.Equal("2D Contour2", job.NextOperationName("2D Contour"));
        }

        [Fact]
        public void A_different_strategy_numbers_separately()
        {
            var job = new Job();
            job.Operations.Add(Named("2D Contour1"));

            Assert.Equal("Drill1", job.NextOperationName("Drill"));
        }

        [Fact]
        public void Case_does_not_let_a_name_repeat()
        {
            var job = new Job();
            job.Operations.Add(Named("2d contour1"));

            Assert.Equal("2D Contour2", job.NextOperationName("2D Contour"));
        }

        [Fact]
        public void A_blank_stem_still_produces_a_name()
        {
            Assert.Equal("Operation1", new Job().NextOperationName(null));
            Assert.Equal("Operation1", new Job().NextOperationName("  "));
        }
    }
}
