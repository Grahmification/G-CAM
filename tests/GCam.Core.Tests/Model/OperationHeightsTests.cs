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
        public void A_retract_below_the_feed_height_is_lifted_to_it_rather_than_refused()
        {
            var heights = new OperationHeights
            {
                Retract = new HeightSetting(HeightMode.FromStockTop, 1),   // 31
                Feed = new HeightSetting(HeightMode.FromStockTop, 2),      // 32
            };

            Assert.Empty(heights.Validate(Context()));
            Assert.True(heights.TryResolve(Context(), out ResolvedHeights z));

            Assert.Equal(32, z.Retract, 9);
            Assert.Equal(31, z.RetractLiftedFrom.Value, 9);

            string warning = OperationHeights.DescribeCorrections(z);
            Assert.Contains("31mm", warning);
            Assert.Contains("32mm", warning);
        }

        [Fact]
        public void A_retract_at_or_above_the_feed_height_is_used_as_entered()
        {
            Assert.True(new OperationHeights().TryResolve(Context(), out ResolvedHeights z));

            Assert.Null(z.RetractLiftedFrom);
            Assert.Null(OperationHeights.DescribeCorrections(z));
        }

        [Fact]
        public void A_clearance_measured_from_the_retract_follows_it_when_it_is_lifted()
        {
            // Retract 31 lifted to 32; the default clearance is 5 above whatever retract is
            // used, so 37 - not the 36 it would have been from the retract as entered.
            var heights = new OperationHeights
            {
                Retract = new HeightSetting(HeightMode.FromStockTop, 1),
                Feed = new HeightSetting(HeightMode.FromStockTop, 2),
            };

            heights.TryResolve(Context(), out ResolvedHeights z);
            Assert.Equal(37, z.Clearance, 9);

            // And the Heights tab, which resolves one at a time, draws the same planes.
            Assert.True(heights.TryResolve(HeightKind.Retract, Context(), out double retract));
            Assert.True(heights.TryResolve(HeightKind.Clearance, Context(), out double clearance));
            Assert.Equal(32, retract, 9);
            Assert.Equal(37, clearance, 9);
        }

        [Fact]
        public void A_fixed_clearance_below_the_lifted_retract_is_still_refused()
        {
            // Nothing lifts the clearance: a clearance that does not follow the retract and
            // ends up below it is a different mistake, and still stops generation.
            var heights = new OperationHeights
            {
                Clearance = new HeightSetting(HeightMode.FromStockTop, 3),   // 33
                Retract = new HeightSetting(HeightMode.FromStockTop, 1),     // 31, lifted to 35
                Feed = new HeightSetting(HeightMode.FromStockTop, 5),        // 35
            };

            string problem = Assert.Single(heights.Validate(Context()));

            Assert.Contains("Clearance height", problem);
            Assert.Contains("35mm", problem);
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
        public void Top_and_bottom_measured_from_the_contour_resolve_for_that_contour()
        {
            var heights = new OperationHeights
            {
                Top = new HeightSetting(HeightMode.FromContour, 3),
                Bottom = new HeightSetting(HeightMode.FromContour, -2),
            };

            Assert.True(heights.IsContourRelative);
            Assert.True(heights.TryResolve(Context().ForContour(10), out ResolvedHeights z));
            Assert.Equal(13, z.Top, 9);
            Assert.Equal(8, z.Bottom, 9);

            // The rest are not contour-relative, so they do not move with it.
            Assert.Equal(40, z.Clearance, 9);
            Assert.Empty(heights.Validate(Context().ForContour(10)));
        }

        [Fact]
        public void Contour_heights_do_not_resolve_for_the_operation_as_a_whole()
        {
            var heights = new OperationHeights
            {
                Bottom = new HeightSetting(HeightMode.FromContour),
            };

            Assert.False(heights.TryResolve(Context(), out ResolvedHeights _));
            Assert.Contains("Bottom height", Assert.Single(heights.Validate(Context())));
        }

        [Fact]
        public void The_same_heights_can_be_in_order_for_one_contour_and_not_another()
        {
            // Ordering is per contour now: a chain above the stock top puts the top above
            // a feed height fixed to the stock, and that has to condemn that chain, not
            // all of them.
            var heights = new OperationHeights
            {
                Feed = new HeightSetting(HeightMode.FromStockTop, 2),
                Top = new HeightSetting(HeightMode.FromContour),
                Bottom = new HeightSetting(HeightMode.FromModelBottom),
            };

            Assert.Empty(heights.Validate(Context().ForContour(20)));
            Assert.Contains("Feed height", Assert.Single(heights.Validate(Context().ForContour(33))));
        }

        [Theory]
        [InlineData(HeightKind.Clearance)]
        [InlineData(HeightKind.Retract)]
        [InlineData(HeightKind.Feed)]
        public void Only_the_cutting_heights_may_be_measured_from_the_contour(HeightKind kind)
        {
            var heights = new OperationHeights();
            var fromContour = new HeightSetting(HeightMode.FromContour, 50);

            switch (kind)
            {
                case HeightKind.Clearance: heights.Clearance = fromContour; break;
                case HeightKind.Retract: heights.Retract = fromContour; break;
                case HeightKind.Feed: heights.Feed = fromContour; break;
            }

            string problem = Assert.Single(heights.Validate(Context().ForContour(0)));

            Assert.Contains("only the top and bottom", problem);
            Assert.False(heights.IsContourRelative);
        }

        [Fact]
        public void The_feed_height_is_measured_from_the_top_by_default()
        {
            HeightSetting feed = new OperationHeights().Feed;

            Assert.Equal(HeightMode.FromTop, feed.Mode);
            Assert.Equal(2, feed.Offset, 9);
        }

        [Fact]
        public void A_feed_height_measured_from_the_top_follows_it()
        {
            // Move where cutting starts, and where the plunge slows down moves with it.
            var heights = new OperationHeights
            {
                Top = new HeightSetting(HeightMode.FromModelTop),
            };

            Assert.True(heights.TryResolve(Context(), out ResolvedHeights z));
            Assert.Equal(25, z.Top, 9);
            Assert.Equal(27, z.Feed, 9);
            Assert.Empty(heights.Validate(Context()));
        }

        [Fact]
        public void One_height_resolves_on_its_own_including_a_feed_measured_from_the_top()
        {
            // What the Heights tab's planes ask: one height at a time.
            var heights = new OperationHeights();

            Assert.True(heights.TryResolve(HeightKind.Feed, Context(), out double feed));
            Assert.Equal(32, feed, 9);
        }

        [Fact]
        public void A_feed_height_measured_from_the_contours_top_moves_with_each_contour()
        {
            var heights = new OperationHeights
            {
                Top = new HeightSetting(HeightMode.FromContour),
                Bottom = new HeightSetting(HeightMode.FromContour, -5),
            };

            heights.TryResolve(Context().ForContour(10), out ResolvedHeights low);
            heights.TryResolve(Context().ForContour(20), out ResolvedHeights high);

            Assert.Equal(12, low.Feed, 9);
            Assert.Equal(22, high.Feed, 9);

            // Retract is still one plane for every contour.
            Assert.Equal(low.Retract, high.Retract, 9);
        }

        [Fact]
        public void A_top_that_cannot_be_worked_out_is_reported_once_not_again_for_the_feed()
        {
            // The feed height fails only because the top did, and naming it too would
            // send someone to the wrong row to fix it.
            var heights = new OperationHeights
            {
                Top = new HeightSetting(HeightMode.FromSelection),
            };

            string problem = Assert.Single(heights.Validate(Context()));

            Assert.Contains("Top height", problem);
            Assert.False(heights.TryResolve(HeightKind.Feed, Context(), out double _));
        }

        [Theory]
        [InlineData(HeightKind.Clearance)]
        [InlineData(HeightKind.Retract)]
        [InlineData(HeightKind.Top)]
        [InlineData(HeightKind.Bottom)]
        public void Only_the_feed_height_may_be_measured_from_the_top(HeightKind kind)
        {
            var heights = new OperationHeights();
            var fromTop = new HeightSetting(HeightMode.FromTop, 1);

            switch (kind)
            {
                case HeightKind.Clearance: heights.Clearance = fromTop; break;
                case HeightKind.Retract: heights.Retract = fromTop; break;
                case HeightKind.Top: heights.Top = fromTop; break;
                case HeightKind.Bottom: heights.Bottom = fromTop; break;
            }

            string problem = Assert.Single(heights.Validate(Context()));

            Assert.Contains("only the feed height", problem);
        }

        [Fact]
        public void The_clearance_height_is_measured_from_the_retract_by_default()
        {
            HeightSetting clearance = new OperationHeights().Clearance;

            Assert.Equal(HeightMode.FromRetract, clearance.Mode);
            Assert.Equal(5, clearance.Offset, 9);
        }

        [Fact]
        public void A_clearance_measured_from_the_retract_follows_it()
        {
            // Raise the retract and the clearance goes up with it, so the two cannot cross.
            var heights = new OperationHeights
            {
                Retract = new HeightSetting(HeightMode.FromStockTop, 20),
            };

            Assert.True(heights.TryResolve(Context(), out ResolvedHeights z));
            Assert.Equal(50, z.Retract, 9);
            Assert.Equal(55, z.Clearance, 9);
            Assert.Empty(heights.Validate(Context()));

            Assert.True(heights.TryResolve(HeightKind.Clearance, Context(), out double clearance));
            Assert.Equal(55, clearance, 9);
        }

        [Fact]
        public void A_retract_that_cannot_be_worked_out_is_reported_once_not_again_for_the_clearance()
        {
            var heights = new OperationHeights
            {
                Retract = new HeightSetting(HeightMode.FromSelection),
            };

            string problem = Assert.Single(heights.Validate(Context()));

            Assert.Contains("Retract height", problem);
            Assert.False(heights.TryResolve(HeightKind.Clearance, Context(), out double _));
        }

        [Fact]
        public void The_clearance_stays_one_plane_when_the_cutting_heights_follow_the_contour()
        {
            var heights = new OperationHeights
            {
                Top = new HeightSetting(HeightMode.FromContour),
                Bottom = new HeightSetting(HeightMode.FromContour, -5),
            };

            heights.TryResolve(Context().ForContour(10), out ResolvedHeights low);
            heights.TryResolve(Context().ForContour(20), out ResolvedHeights high);

            Assert.Equal(40, low.Clearance, 9);
            Assert.Equal(40, high.Clearance, 9);
        }

        [Theory]
        [InlineData(HeightKind.Retract)]
        [InlineData(HeightKind.Feed)]
        [InlineData(HeightKind.Top)]
        [InlineData(HeightKind.Bottom)]
        public void Only_the_clearance_height_may_be_measured_from_the_retract(HeightKind kind)
        {
            var heights = new OperationHeights();
            var fromRetract = new HeightSetting(HeightMode.FromRetract, 1);

            switch (kind)
            {
                case HeightKind.Retract: heights.Retract = fromRetract; break;
                case HeightKind.Feed: heights.Feed = fromRetract; break;
                case HeightKind.Top: heights.Top = fromRetract; break;
                case HeightKind.Bottom: heights.Bottom = fromRetract; break;
            }

            string problem = Assert.Single(heights.Validate(Context()));

            Assert.Contains("only the clearance height", problem);
        }

        [Fact]
        public void Cloning_copies_every_height_rather_than_sharing_them()
        {
            var original = new OperationHeights();

            OperationHeights copy = original.Clone();
            copy.Clearance.Offset = 99;
            copy.Bottom.Mode = HeightMode.FromStockBottom;

            Assert.Equal(5, original.Clearance.Offset, 9);
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
