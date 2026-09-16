using GCam.Core.Model;
using GCam.Core.Strategies.Contour2d;
using Xunit;

namespace GCam.Core.Tests.Model
{
    /// <summary>
    /// Which badge a state earns, and what the job tree's tooltip says about it.
    /// </summary>
    public class OperationStatusTests
    {
        private static Operation With(OperationState state, string message = null) =>
            new Operation(new Contour2dSettings())
            {
                Name = "2D Contour1",
                State = state,
                StateMessage = message,
            };

        [Fact]
        public void A_generated_operation_earns_no_badge()
        {
            Assert.Equal(OperationBadge.None, OperationStatus.Badge(OperationState.Generated));
        }

        [Fact]
        public void A_warning_earns_the_warning_badge()
        {
            Assert.Equal(OperationBadge.Warning, OperationStatus.Badge(OperationState.Warning));
        }

        [Theory]
        [InlineData(OperationState.NotGenerated)]
        [InlineData(OperationState.Stale)]
        [InlineData(OperationState.Failed)]
        public void Anything_without_a_toolpath_to_trust_earns_the_error_badge(OperationState state)
        {
            Assert.Equal(OperationBadge.Error, OperationStatus.Badge(state));
        }

        [Fact]
        public void Generating_keeps_the_error_badge_rather_than_clearing_it()
        {
            // Blanking the badge for the length of a run would read as "done" while the
            // operation still has nothing anyone should believe.
            Assert.Equal(OperationBadge.Error, OperationStatus.Badge(OperationState.Generating));
        }

        [Fact]
        public void Every_state_reads_differently_from_every_other()
        {
            // The point of keeping the wording in one place: six states, six answers, and
            // nothing that leaves the user unable to tell two of them apart. A state added
            // later with no case of its own falls through to its enum name, which this
            // cannot catch - pinning the words below is what covers that.
            var labels = new System.Collections.Generic.HashSet<string>();

            foreach (OperationState state in System.Enum.GetValues(typeof(OperationState)))
            {
                string label = OperationStatus.Label(state);

                Assert.False(string.IsNullOrWhiteSpace(label));
                Assert.True(labels.Add(label), "Two states read the same: " + label);
            }
        }

        [Theory]
        [InlineData(OperationState.NotGenerated, "Not generated")]
        [InlineData(OperationState.Generating, "Generating…")]
        [InlineData(OperationState.Generated, "Generated")]
        [InlineData(OperationState.Stale, "Out of date — generate it again")]
        [InlineData(OperationState.Warning, "Generated, with a warning")]
        [InlineData(OperationState.Failed, "Generation failed")]
        public void The_words_for_each_state_are_pinned(OperationState state, string expected)
        {
            // Pinned rather than merely non-empty because this vocabulary is meant to be
            // the one the tooltip, the posting warning and the generation report all use.
            Assert.Equal(expected, OperationStatus.Label(state));
        }

        [Fact]
        public void The_tooltip_carries_the_reason_a_generate_failed()
        {
            // For a failure this is the only place the reason appears outside the log.
            string text = OperationStatus.Describe(
                With(OperationState.Failed, "This version of G-CAM has no 'drill' strategy."));

            Assert.Contains("Generation failed", text);
            Assert.Contains("no 'drill' strategy", text);
        }

        [Fact]
        public void A_state_with_nothing_to_add_says_only_the_state()
        {
            Assert.Equal("Generated", OperationStatus.Describe(With(OperationState.Generated)));
        }

        [Fact]
        public void A_blank_message_does_not_leave_a_dangling_line()
        {
            Assert.Equal(
                "Generated", OperationStatus.Describe(With(OperationState.Generated, "   ")));
        }

        [Fact]
        public void A_suppressed_operation_says_so_before_its_state()
        {
            // Its state cannot change however often the job is generated, which is worth
            // saying outright rather than leaving someone to regenerate repeatedly.
            Operation operation = With(OperationState.Stale);
            operation.Enabled = false;

            Assert.StartsWith("Suppressed", OperationStatus.Describe(operation));
        }

        [Fact]
        public void There_is_nothing_to_say_about_no_operation()
        {
            // Null, not empty: WPF suppresses a null tooltip and shows an empty box for "".
            Assert.Null(OperationStatus.Describe(null));
        }
    }
}
