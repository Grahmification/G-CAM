using System.Collections.Generic;
using System.Linq;
using GCam.Core.Strategies.Shared;
using Xunit;

namespace GCam.Core.Tests.Strategies
{
    public class MultipleDepthsSettingsTests
    {
        [Fact]
        public void Disabled_means_no_passes_whatever_the_depth()
        {
            var depths = new MultipleDepthsSettings { MaximumStepdown = 2 };

            Assert.Equal(0, depths.PassCount(10));
            Assert.Empty(depths.Stepdowns(10));
        }

        [Fact]
        public void A_depth_that_divides_exactly_does_not_gain_a_extra_pass()
        {
            // 10 / 2 must be five passes, not six with a final one of zero depth.
            var depths = new MultipleDepthsSettings { Enabled = true, MaximumStepdown = 2 };

            Assert.Equal(5, depths.PassCount(10));
        }

        [Fact]
        public void A_remainder_gets_its_own_pass()
        {
            var depths = new MultipleDepthsSettings { Enabled = true, MaximumStepdown = 3 };

            Assert.Equal(4, depths.PassCount(10));
        }

        [Fact]
        public void Even_stepdowns_share_the_depth_out_equally()
        {
            var depths = new MultipleDepthsSettings
            {
                Enabled = true,
                MaximumStepdown = 3,
                UseEvenStepdowns = true,
            };

            IReadOnlyList<double> steps = depths.Stepdowns(10);

            Assert.Equal(4, steps.Count);
            Assert.All(steps, s => Assert.Equal(2.5, s, 9));
            Assert.Equal(10, steps.Sum(), 9);
        }

        [Fact]
        public void Uneven_stepdowns_take_full_steps_and_a_remainder()
        {
            var depths = new MultipleDepthsSettings
            {
                Enabled = true,
                MaximumStepdown = 3,
                UseEvenStepdowns = false,
            };

            IReadOnlyList<double> steps = depths.Stepdowns(10);

            Assert.Equal(new[] { 3.0, 3.0, 3.0, 1.0 }, steps.Select(s => System.Math.Round(s, 9)));
            Assert.Equal(10, steps.Sum(), 9);
        }

        [Fact]
        public void The_steps_always_add_up_to_the_depth_asked_for()
        {
            // Whichever mode: a pass list that does not reach the bottom leaves material,
            // and one that overshoots cuts air below it.
            foreach (bool even in new[] { true, false })
            {
                var depths = new MultipleDepthsSettings
                {
                    Enabled = true,
                    MaximumStepdown = 1.7,
                    UseEvenStepdowns = even,
                };

                Assert.Equal(9.3, depths.Stepdowns(9.3).Sum(), 9);
            }
        }

        [Fact]
        public void No_depth_means_no_passes()
        {
            var depths = new MultipleDepthsSettings { Enabled = true, MaximumStepdown = 2 };

            Assert.Equal(0, depths.PassCount(0));
            Assert.Empty(depths.Stepdowns(0));
        }

        [Fact]
        public void A_stepdown_deeper_than_the_cut_is_one_pass()
        {
            var depths = new MultipleDepthsSettings { Enabled = true, MaximumStepdown = 50 };

            Assert.Equal(1, depths.PassCount(10));
            Assert.Equal(10, Assert.Single(depths.Stepdowns(10)), 9);
        }

        [Fact]
        public void Cloning_does_not_share_state()
        {
            var original = new MultipleDepthsSettings { Enabled = true, MaximumStepdown = 2 };

            MultipleDepthsSettings copy = original.Clone();
            copy.MaximumStepdown = 99;

            Assert.Equal(2, original.MaximumStepdown, 9);
        }
    }
}
