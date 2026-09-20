using GCam.Core.Geometry;
using GCam.Core.Geometry.Primitives;
using Xunit;

namespace GCam.Core.Tests.Geometry
{
    public class TangentialExtensionTests
    {
        private static Vec3 P(double x, double y) => new Vec3(x, y, 0);

        /// <summary>A 10mm run along +X, then 10mm along +Y.</summary>
        private static Polyline Corner() =>
            new Polyline(new[] { P(0, 0), P(10, 0), P(10, 10) });

        [Fact]
        public void Both_ends_run_on_along_their_own_tangents()
        {
            Polyline extended = TangentialExtension.Apply(Corner(), 2);

            // Out along -X from the start, and on along +Y past the end: the corner
            // between them is untouched.
            Assert.Equal(P(-2, 0), extended.Points[0]);
            Assert.Equal(P(10, 12), extended.Points[extended.Count - 1]);
            Assert.Equal(24, extended.Length, 6);
        }

        [Fact]
        public void A_closed_contour_is_left_alone()
        {
            var loop = new Polyline(
                new[] { P(0, 0), P(10, 0), P(10, 10), P(0, 10) }, closed: true);

            Assert.Same(loop, TangentialExtension.Apply(loop, 5));
        }

        [Fact]
        public void A_negative_distance_shortens_from_both_ends()
        {
            Polyline shortened = TangentialExtension.Apply(Corner(), -2);

            Assert.Equal(P(2, 0), shortened.Points[0]);
            Assert.Equal(P(10, 8), shortened.Points[shortened.Count - 1]);
            Assert.Equal(16, shortened.Length, 6);
        }

        [Fact]
        public void Shortening_walks_back_through_whole_segments()
        {
            // 10mm along +X and 20mm along +Y, with 12mm taken off each end - so the
            // start runs past the corner and the corner itself is gone.
            var longer = new Polyline(new[] { P(0, 0), P(10, 0), P(10, 20) });

            Polyline shortened = TangentialExtension.Apply(longer, -12);

            Assert.Equal(2, shortened.Count);
            Assert.Equal(P(10, 2), shortened.Points[0]);
            Assert.Equal(P(10, 8), shortened.Points[1]);
        }

        [Fact]
        public void Shortening_past_what_is_there_leaves_nothing()
        {
            // Both ends take 11mm out of a 20mm contour.
            Assert.Null(TangentialExtension.Apply(Corner(), -11));
        }
    }
}
