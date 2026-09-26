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
    /// <summary>
    /// A pass whose lead would cut into a selected wall is not cut, and the operation says
    /// so.
    /// </summary>
    /// <remarks>
    /// With a Ø10 cutter. Every selected wall at the same depths counts, the pass's own
    /// included.
    /// </remarks>
    public class Contour2dLeadCollisionTests
    {
        private static Polyline Rectangle(double x0, double y0, double x1, double y1) =>
            new Polyline(
                new[] { new Vec3(x0, y0, 0), new Vec3(x1, y0, 0), new Vec3(x1, y1, 0), new Vec3(x0, y1, 0) },
                closed: true);

        private static Polyline Line(double x0, double y0, double x1, double y1) =>
            new Polyline(new[] { new Vec3(x0, y0, 0), new Vec3(x1, y1, 0) });

        private static GenerationContext Context(double leadRadius, params ResolvedContour[] contours)
        {
            var settings = new Contour2dSettings();
            settings.LeadIn.Radius = leadRadius;

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
                Geometry = { Diameter = 10, FluteLength = 30 },
            };

            return new GenerationContext(
                new Job { Name = "Job 1" },
                operation,
                tool,
                new ResolvedHeights(40, 35, 32, 30, 0),
                new Bounds(new Vec3(-100, -100, 0), new Vec3(200, 200, 30)),
                contours);
        }

        private static Toolpath Generate(GenerationContext context) =>
            new Contour2dStrategy().Generate(context, null, CancellationToken.None);

        private static int PassCount(Toolpath path) => path.Moves.Count(m => m.Kind == MoveKind.Plunge);

        /// <summary>
        /// A 16mm slot: the cutter path inside it is 6mm wide, so a lead of more than 6mm
        /// swings into the far wall.
        /// </summary>
        private static ResolvedContour Slot() => new ResolvedContour(Rectangle(0, 0, 16, 100), reversed: true);

        [Fact]
        public void A_lead_that_swings_into_the_far_wall_of_its_own_pocket_drops_the_pass()
        {
            GenerationContext context = Context(7, Slot());

            Assert.Equal(0, PassCount(Generate(context)));
            Assert.Contains(context.Warnings, w => w.StartsWith("One toolpath was not cut"));
        }

        [Fact]
        public void A_lead_that_fits_its_pocket_is_cut_without_a_warning()
        {
            GenerationContext context = Context(2, Slot());

            Assert.Equal(1, PassCount(Generate(context)));
            Assert.Empty(context.Warnings);
        }

        [Fact]
        public void A_lead_that_swings_into_another_selected_wall_drops_only_its_own_pass()
        {
            // Two walls facing away from each other, their cutter paths 1mm apart: x = 25
            // for the first, whose leads swing out to the right towards the second, and
            // x = 36 for the second, beyond it.
            GenerationContext context = Context(
                2,
                new ResolvedContour(Line(20, 0, 20, 100)),
                new ResolvedContour(Line(31, -20, 31, 120)));

            Toolpath path = Generate(context);

            Assert.Equal(1, PassCount(path));
            Assert.All(
                path.Moves.Where(m => m.Kind == MoveKind.Cutting),
                m => Assert.Equal(36, m.End.X, 3));
            Assert.Contains(context.Warnings, w => w.StartsWith("One toolpath was not cut"));
        }

        [Fact]
        public void The_same_walls_with_a_lead_small_enough_to_clear_are_both_cut()
        {
            GenerationContext context = Context(
                0.5,
                new ResolvedContour(Line(20, 0, 20, 100)),
                new ResolvedContour(Line(31, -20, 31, 120)));

            Assert.Equal(2, PassCount(Generate(context)));
            Assert.Empty(context.Warnings);
        }

        [Fact]
        public void Several_dropped_passes_are_counted_in_one_warning()
        {
            GenerationContext context = Context(
                7, Slot(), new ResolvedContour(Rectangle(50, 0, 66, 100), reversed: true));

            Assert.Equal(0, PassCount(Generate(context)));
            Assert.Contains(context.Warnings, w => w.StartsWith("2 toolpaths were not cut"));
        }

        [Fact]
        public void A_lead_across_where_a_merged_pocket_s_wall_would_have_been_is_not_a_collision()
        {
            // Two overlapping pockets cut as one. Their walls inside each other are gone,
            // so a lead crossing them is in the air of the pocket they make together. The
            // merged path starts mid-way along its longest edge, the top one at y = 45,
            // travelling +X; a 6mm lead-in touches down at (41.5, 39) - within the cutter's
            // reach of the first pocket's right-hand wall at x = 40, which the second
            // pocket has taken away.
            GenerationContext context = Context(
                6,
                new ResolvedContour(Rectangle(0, 0, 40, 40), reversed: true),
                new ResolvedContour(Rectangle(25, 10, 70, 50), reversed: true));

            Assert.Equal(1, PassCount(Generate(context)));
            Assert.Empty(context.Warnings);
        }

        [Fact]
        public void With_no_leads_nothing_is_dropped()
        {
            GenerationContext context = Context(7, Slot());
            context.SettingsAs<Contour2dSettings>().LeadIn.Enabled = false;

            Assert.Equal(1, PassCount(Generate(context)));
            Assert.Empty(context.Warnings);
        }
    }
}
