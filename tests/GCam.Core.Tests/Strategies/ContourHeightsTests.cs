using System.Collections.Generic;
using System.Linq;
using System.Threading;
using GCam.Core.Geometry.Primitives;
using GCam.Core.Model;
using GCam.Core.Model.Heights;
using GCam.Core.Strategies;
using GCam.Core.Strategies.Contour2d;
using GCam.Core.Tooling;
using Xunit;

namespace GCam.Core.Tests.Strategies
{
    public class ContourHeightsTests
    {
        // Stock from Z -5 to Z 30, model from Z 0 to Z 25.
        private static HeightContext Context() => new HeightContext(30, -5, 25, 0);

        /// <summary>A 20 x 20 square at the given Z, counter-clockwise.</summary>
        private static ResolvedContour Square(double x, double z)
        {
            return new ResolvedContour(new Polyline(
                new[]
                {
                    new Vec3(x, 0, z),
                    new Vec3(x + 20, 0, z),
                    new Vec3(x + 20, 20, z),
                    new Vec3(x, 20, z),
                },
                closed: true));
        }

        /// <summary>
        /// Cutting from 2mm above each contour to 5mm below it, under a feed height fixed
        /// at 32 - so a contour high enough puts its top above the feed.
        /// </summary>
        private static OperationHeights FromTheContour()
        {
            return new OperationHeights
            {
                Feed = new HeightSetting(HeightMode.FromStockTop, 2),
                Top = new HeightSetting(HeightMode.FromContour, 2),
                Bottom = new HeightSetting(HeightMode.FromContour, -5),
            };
        }

        private static IReadOnlyList<ResolvedContour> Resolve(
            OperationHeights heights,
            IReadOnlyList<ResolvedContour> contours,
            List<string> warnings,
            out string failure)
        {
            return ContourHeights.Resolve(heights, Context(), contours, warnings, out failure);
        }

        [Fact]
        public void Each_contour_gets_heights_measured_from_its_own_level()
        {
            var warnings = new List<string>();

            IReadOnlyList<ResolvedContour> resolved = Resolve(
                FromTheContour(), new[] { Square(0, 10), Square(50, 20) }, warnings, out string failure);

            Assert.Equal(2, resolved.Count);
            Assert.Equal(12, resolved[0].Heights.Top, 9);
            Assert.Equal(5, resolved[0].Heights.Bottom, 9);
            Assert.Equal(22, resolved[1].Heights.Top, 9);
            Assert.Equal(15, resolved[1].Heights.Bottom, 9);

            // Clearance is the operation's, whatever the contour.
            Assert.Equal(resolved[0].Heights.Clearance, resolved[1].Heights.Clearance, 9);
            Assert.Empty(warnings);
            Assert.Null(failure);
        }

        [Fact]
        public void A_contour_whose_heights_are_out_of_order_is_left_out_and_the_rest_are_kept()
        {
            // The second square sits at 31, so its top lands at 33 - above the feed height
            // of 32. That chain is not cut; the first one still is.
            var warnings = new List<string>();

            IReadOnlyList<ResolvedContour> resolved = Resolve(
                FromTheContour(), new[] { Square(0, 10), Square(50, 31) }, warnings, out string _);

            ResolvedContour kept = Assert.Single(resolved);
            Assert.Equal(10, kept.Level, 9);

            string warning = Assert.Single(warnings);
            Assert.Contains("31mm", warning);
            Assert.Contains("Feed height", warning);
        }

        [Fact]
        public void When_every_contour_fails_there_is_a_failure_and_no_warnings()
        {
            // A warning per contour saying the same thing would bury the one message that
            // matters: nothing was generated, and why.
            var warnings = new List<string>();

            IReadOnlyList<ResolvedContour> resolved = Resolve(
                FromTheContour(), new[] { Square(0, 31), Square(50, 40) }, warnings, out string failure);

            Assert.Empty(resolved);
            Assert.Empty(warnings);
            Assert.Contains("Feed height", failure);
        }

        [Fact]
        public void A_retract_below_one_contours_feed_is_lifted_to_one_plane_for_all_of_them()
        {
            // Feed follows the contour's top here. The high square's feed is 36, above the
            // retract of 35, so the retract goes to 36 - for the low square as well.
            var heights = new OperationHeights
            {
                Top = new HeightSetting(HeightMode.FromContour),
                Bottom = new HeightSetting(HeightMode.FromContour, -5),
            };

            var warnings = new List<string>();

            IReadOnlyList<ResolvedContour> resolved = Resolve(
                heights, new[] { Square(0, 10), Square(50, 34) }, warnings, out string failure);

            Assert.Equal(2, resolved.Count);
            Assert.All(resolved, c => Assert.Equal(36, c.Heights.Retract, 9));
            Assert.All(resolved, c => Assert.Equal(41, c.Heights.Clearance, 9));

            // Each contour keeps its own feed height; only the retract is shared.
            Assert.Equal(12, resolved[0].Heights.Feed, 9);

            string warning = Assert.Single(warnings);
            Assert.Contains("35mm", warning);
            Assert.Contains("36mm", warning);
            Assert.Null(failure);
        }

        [Fact]
        public void A_contour_left_out_does_not_lift_the_retract_for_the_rest()
        {
            // The high square's feed is 62, and lifting the retract to it would put it
            // above a clearance fixed at 50 - so that square is refused, and its feed must
            // not drag the retract up over the one contour that is cut.
            var heights = new OperationHeights
            {
                Clearance = new HeightSetting(HeightMode.FromStockTop, 20),
                Top = new HeightSetting(HeightMode.FromContour),
                Bottom = new HeightSetting(HeightMode.FromModelBottom),
            };

            var warnings = new List<string>();

            IReadOnlyList<ResolvedContour> resolved = Resolve(
                heights, new[] { Square(0, 10), Square(50, 60) }, warnings, out string _);

            ResolvedContour kept = Assert.Single(resolved);
            Assert.Equal(10, kept.Level, 9);
            Assert.Equal(35, kept.Heights.Retract, 9);
            Assert.Null(kept.Heights.RetractLiftedFrom);
            Assert.Contains("Clearance height", Assert.Single(warnings));
        }

        [Fact]
        public void No_contours_at_all_is_reported_as_a_failure()
        {
            IReadOnlyList<ResolvedContour> resolved = Resolve(
                FromTheContour(), new ResolvedContour[0], new List<string>(), out string failure);

            Assert.Empty(resolved);
            Assert.Contains("contour", failure);
        }

        [Fact]
        public void Two_contours_at_different_levels_are_cut_at_different_depths_in_one_operation()
        {
            IReadOnlyList<ResolvedContour> resolved = Resolve(
                FromTheContour(), new[] { Square(0, 10), Square(50, 20) }, null, out string _);

            Toolpath path = Generate(new Contour2dSettings(), resolved);

            // Multiple depths are off, so each contour is one pass at its own bottom.
            List<double> cutDepths = path.Moves
                .Where(m => m.Kind == MoveKind.Cutting)
                .Select(m => m.End.Z)
                .Distinct()
                .OrderBy(z => z)
                .ToList();

            Assert.Equal(new[] { 5.0, 15.0 }, cutDepths);

            // The links between them are not contour-relative: every rapid across is at
            // the one clearance plane.
            Assert.All(
                path.Moves.Where(m => m.Kind == MoveKind.Rapid && m.End.Z > 32),
                m => Assert.Equal(resolved[0].Heights.Clearance, m.End.Z, 9));
        }

        [Fact]
        public void A_contour_keeps_its_heights_through_a_tangential_extension()
        {
            // Extending replaces the contour with a longer one, which is exactly where a
            // per-contour height could be dropped on the floor.
            var line = new ResolvedContour(new Polyline(new[] { new Vec3(0, 0, 10), new Vec3(40, 0, 10) }));

            IReadOnlyList<ResolvedContour> resolved = Resolve(
                FromTheContour(), new[] { line }, null, out string _);

            Toolpath path = Generate(
                new Contour2dSettings { TangentialExtensionDistance = 3 }, resolved);

            Assert.All(
                path.Moves.Where(m => m.Kind == MoveKind.Cutting),
                m => Assert.Equal(5, m.End.Z, 9));
        }

        private static Toolpath Generate(
            Contour2dSettings settings, IReadOnlyList<ResolvedContour> contours)
        {
            var operation = new Operation(settings)
            {
                Name = "2D Contour1",
                ToolId = "tool-4",
                Cutting = { SpindleRpm = 7500, CuttingFeed = 1800, PlungeFeed = 600 },
            };

            var tool = new Tool
            {
                Id = "tool-4",
                Number = 4,
                Type = ToolType.FlatEndMill,
                Geometry = { Diameter = 6, FluteLength = 30 },
            };

            var context = new GenerationContext(
                new Job { Name = "Job 1" },
                operation,
                tool,
                contours[0].Heights,
                new Bounds(new Vec3(-5, -5, -5), new Vec3(105, 65, 30)),
                contours);

            return new Contour2dStrategy().Generate(context, null, CancellationToken.None);
        }
    }
}
