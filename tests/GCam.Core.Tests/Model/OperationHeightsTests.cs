using System.Collections.Generic;
using System.Linq;
using GCam.Core.Model;
using GCam.Core.Model.Heights;
using Xunit;

namespace GCam.Core.Tests.Model
{
    public class OperationHeightsTests
    {
        // Stock from Z -5 to Z 30, model from Z 0 to Z 25.
        private static HeightContext Context(
            IReadOnlyDictionary<string, double> selections = null)
        {
            return new HeightContext(30, -5, 25, 0, selections);
        }

        [Fact]
        public void The_defaults_resolve_in_order_and_cut_the_whole_model()
        {
            var heights = new OperationHeights();

            Assert.True(heights.TryResolve(Context(), out ResolvedHeights z));

            Assert.Equal(40, z.Clearance, 9);
            Assert.Equal(35, z.Retract, 9);
            Assert.Equal(32, z.Feed, 9);
            Assert.Equal(30, z.Top, 9);
            Assert.Equal(0, z.Bottom, 9);
            Assert.Equal(30, z.DepthOfCut, 9);
            Assert.Empty(heights.Validate(Context()));
        }

        [Fact]
        public void Heights_follow_the_stock()
        {
            // The whole point of storing a mode and an offset instead of a number: raise
            // the stock and every height measured from it moves, with nothing re-entered.
            var heights = new OperationHeights();

            heights.TryResolve(new HeightContext(30, -5, 25, 0), out ResolvedHeights before);
            heights.TryResolve(new HeightContext(50, -5, 25, 0), out ResolvedHeights after);

            Assert.Equal(20, after.Clearance - before.Clearance, 9);
            Assert.Equal(20, after.Top - before.Top, 9);

            // The bottom is measured from the model, so it must not have moved.
            Assert.Equal(before.Bottom, after.Bottom, 9);
        }

        [Fact]
        public void A_retract_below_the_feed_height_is_reported()
        {
            var heights = new OperationHeights
            {
                Retract = new HeightSetting(HeightMode.FromStockTop, 1),
                Feed = new HeightSetting(HeightMode.FromStockTop, 2),
            };

            string problem = Assert.Single(heights.Validate(Context()));

            Assert.Contains("Retract height", problem);
            Assert.Contains("feed height", problem);
        }

        [Fact]
        public void A_clearance_below_the_retract_height_is_reported()
        {
            var heights = new OperationHeights
            {
                Clearance = new HeightSetting(HeightMode.FromStockTop, 4),
            };

            string problem = Assert.Single(heights.Validate(Context()));

            Assert.Contains("Clearance height", problem);
        }

        [Fact]
        public void A_feed_height_below_the_top_is_reported()
        {
            var heights = new OperationHeights
            {
                Feed = new HeightSetting(HeightMode.FromStockTop, -1),
            };

            string problem = Assert.Single(heights.Validate(Context()));

            Assert.Contains("Feed height", problem);
            Assert.Contains("top height", problem);
        }

        [Fact]
        public void Equal_heights_are_allowed()
        {
            // Retracting to exactly the feed height is a legitimate way to keep the tool
            // down between passes, so the rule is "at or above", not "above".
            var heights = new OperationHeights
            {
                Clearance = new HeightSetting(HeightMode.FromStockTop, 5),
                Retract = new HeightSetting(HeightMode.FromStockTop, 5),
                Feed = new HeightSetting(HeightMode.FromStockTop, 5),
            };

            Assert.Empty(heights.Validate(Context()));
        }

        [Fact]
        public void Two_different_modes_landing_on_the_same_plane_are_allowed()
        {
            // Validation has to run on resolved numbers: these two modes are unrelated,
            // and on this stock they resolve to the same Z.
            var heights = new OperationHeights
            {
                Top = new HeightSetting(HeightMode.FromModelTop, 5),   // 30
                Feed = new HeightSetting(HeightMode.FromStockTop),     // 30
            };

            Assert.Empty(heights.Validate(Context()));
        }

        [Fact]
        public void A_top_at_the_bottom_is_reported_as_nothing_to_cut()
        {
            var heights = new OperationHeights
            {
                Top = new HeightSetting(HeightMode.FromModelBottom),
                Bottom = new HeightSetting(HeightMode.FromModelBottom),
            };

            string problem = Assert.Single(heights.Validate(Context()));

            Assert.Contains("nothing to cut", problem);
        }

        [Fact]
        public void A_top_below_the_bottom_is_reported()
        {
            var heights = new OperationHeights
            {
                Top = new HeightSetting(HeightMode.FromModelBottom, -10),
                Bottom = new HeightSetting(HeightMode.FromModelBottom),
            };

            Assert.NotEmpty(heights.Validate(Context()));
        }

        [Fact]
        public void An_unresolvable_height_is_named_and_stops_the_ordering_check()
        {
            // Ordering cannot be judged on numbers that do not exist, and reporting a
            // spurious ordering error alongside the real cause would bury it.
            var heights = new OperationHeights
            {
                Bottom = new HeightSetting(HeightMode.FromSelection),
            };

            IReadOnlyList<string> problems = heights.Validate(Context());

            string problem = Assert.Single(problems);
            Assert.Contains("Bottom height", problem);
            Assert.False(heights.TryResolve(Context(), out ResolvedHeights _));
        }

        [Fact]
        public void Every_unresolvable_height_is_reported_not_just_the_first()
        {
            var heights = new OperationHeights
            {
                Top = new HeightSetting(HeightMode.FromSelection),
                Bottom = new HeightSetting(HeightMode.FromSelection),
            };

            Assert.Equal(2, heights.Validate(Context()).Count);
        }

        [Fact]
        public void A_height_measured_from_a_selection_resolves_with_the_rest()
        {
            var heights = new OperationHeights
            {
                Bottom = new HeightSetting(HeightMode.FromSelection)
                {
                    Reference = new GeometryRef { PersistentId = "face-1", DisplayName = "Floor" },
                },
            };

            var selections = new Dictionary<string, double> { ["face-1"] = 8 };

            Assert.True(heights.TryResolve(Context(selections), out ResolvedHeights z));
            Assert.Equal(8, z.Bottom, 9);
            Assert.Equal(22, z.DepthOfCut, 9);
            Assert.Empty(heights.Validate(Context(selections)));
        }

        [Fact]
        public void Cloning_copies_every_height_rather_than_sharing_them()
        {
            var original = new OperationHeights();

            OperationHeights copy = original.Clone();
            copy.Clearance.Offset = 99;
            copy.Bottom.Mode = HeightMode.FromStockBottom;

            Assert.Equal(10, original.Clearance.Offset, 9);
            Assert.Equal(HeightMode.FromModelBottom, original.Bottom.Mode);
        }

        [Fact]
        public void Problems_name_the_offending_heights_with_their_values()
        {
            // The message has to say which pair and what they resolved to - "heights are
            // out of order" sends someone back to the page to work out which.
            var heights = new OperationHeights
            {
                Clearance = new HeightSetting(HeightMode.FromStockTop, 1),
            };

            string problem = heights.Validate(Context()).Single();

            Assert.Contains("31mm", problem);
            Assert.Contains("35mm", problem);
        }
    }
}
