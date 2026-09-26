using System.Collections.Generic;
using System.Linq;
using GCam.Core.Geometry.Offset;
using GCam.Core.Geometry.Primitives;
using Xunit;

namespace GCam.Core.Tests.Geometry
{
    /// <summary>
    /// The region operations merging is built from: offsets as regions, bands, and
    /// clipping a path against them.
    /// </summary>
    public class ContourClippingTests
    {
        private static readonly Clipper2Offsetter Offsetter = new Clipper2Offsetter();

        private static Vec3 P(double x, double y) => new Vec3(x, y, 0);

        private static Polyline Square(bool counterClockwise) =>
            new Polyline(new[] { P(0, 0), P(100, 0), P(100, 100), P(0, 100) }, closed: true)
                .WithDirection(counterClockwise);

        [Theory]
        [InlineData(true, 5)]
        [InlineData(false, 5)]
        [InlineData(true, -5)]
        [InlineData(false, -5)]
        public void An_offset_outline_runs_counter_clockwise_whichever_way_the_contour_ran(bool ccw, double distance)
        {
            // Clipper2 hands back the orientation it is given; left like that, two regions
            // offset from contours running opposite ways cancel where they overlap.
            Polyline outline = Assert.Single(Offsetter.Offset(Square(ccw), distance, 0.01));

            Assert.True(outline.IsCounterClockwise);
        }

        [Fact]
        public void A_path_the_region_does_not_touch_comes_back_as_itself()
        {
            Polyline path = Square(true);
            IReadOnlyList<Polyline> region = Offsetter.Offset(
                new Polyline(new[] { P(200, 0), P(210, 0), P(210, 10), P(200, 10) }, closed: true), 1, 0.01);

            Assert.Same(path, Assert.Single(Offsetter.Outside(path, region)));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void What_is_left_runs_the_same_way_as_the_path(bool ccw)
        {
            // A square over one corner takes a bite out of it, leaving one piece - which
            // must still travel the way the cutter was going to.
            Polyline path = Square(ccw);
            IReadOnlyList<Polyline> region = new[]
            {
                new Polyline(new[] { P(80, 80), P(120, 80), P(120, 120), P(80, 120) }, closed: true),
            };

            IReadOnlyList<Polyline> pieces = Offsetter.Outside(path, region);

            Polyline rejoined = new Polyline(pieces.SelectMany(p => p.Points).Distinct().ToList(), closed: true);

            Assert.DoesNotContain(pieces.SelectMany(p => p.Points), p => p.X > 80 && p.Y > 80);
            Assert.Equal(ccw, rejoined.IsCounterClockwise);
        }

        [Fact]
        public void A_region_that_covers_the_path_leaves_nothing()
        {
            IReadOnlyList<Polyline> region = new[]
            {
                new Polyline(new[] { P(-10, -10), P(110, -10), P(110, 110), P(-10, 110) }, closed: true),
            };

            Assert.Empty(Offsetter.Outside(Square(true), region));
        }

        [Fact]
        public void An_open_path_s_band_is_rounded_past_its_ends()
        {
            // A selected edge ends at a corner of the part, and a cutter centred just past
            // it cuts the corner.
            Polyline band = Assert.Single(
                Offsetter.Band(new Polyline(new[] { P(0, 0), P(100, 0) }), 5, 0.01));

            // To within the arc tolerance: the round end is facets inside the true arc.
            Assert.InRange(band.Points.Min(p => p.X), -5 - 1e-3, -5 + 0.01);
            Assert.InRange(band.Points.Max(p => p.X), 105 - 0.01, 105 + 1e-3);
            Assert.InRange(band.Points.Max(p => p.Y), 5 - 1e-3, 5 + 1e-3);
        }
    }
}
