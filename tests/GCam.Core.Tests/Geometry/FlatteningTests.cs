using System.Linq;
using GCam.Core.Geometry;
using GCam.Core.Geometry.Primitives;
using Xunit;

namespace GCam.Core.Tests.Geometry
{
    public class FlatteningTests
    {
        private static Vec3 P(double x, double y, double z = 0) => new Vec3(x, y, z);

        [Fact]
        public void Every_point_lands_on_the_level()
        {
            var ramp = new Polyline(new[] { P(0, 0, 0), P(10, 0, 5), P(20, 0, 2) });

            Polyline flat = Flattening.Onto(ramp, 3);

            Assert.Equal(3, flat.Count);
            Assert.All(flat.Points, p => Assert.Equal(3, p.Z, 9));
            Assert.Equal(20, flat.Points[2].X, 9);
        }

        [Fact]
        public void A_riser_in_the_middle_of_a_chain_is_merged_away()
        {
            // Step profile: across, straight up, across. The riser projects to a single
            // point, and leaving it in would be a zero-length segment for the offsetter.
            var step = new Polyline(new[] { P(0, 0, 0), P(10, 0, 0), P(10, 0, 5), P(20, 0, 5) });

            Polyline flat = Flattening.Onto(step, 0);

            Assert.Equal(new[] { 0.0, 10.0, 20.0 }, flat.Points.Select(p => p.X).ToArray());
        }

        [Fact]
        public void A_vertical_edge_flattens_to_nothing()
        {
            var riser = new Polyline(new[] { P(5, 5, 0), P(5, 5, 10) });

            Assert.Null(Flattening.Onto(riser, 0));
        }

        [Fact]
        public void The_ends_stay_where_they_were_when_a_short_last_segment_is_merged()
        {
            // The end is what the next piece chains on to, so the interior point is the
            // one that goes.
            var chain = new Polyline(new[] { P(0, 0, 0), P(10, 0, 0), P(10.005, 0, 1) });

            Polyline flat = Flattening.Onto(chain, 0);

            Assert.Equal(2, flat.Count);
            Assert.Equal(10.005, flat.Points[1].X, 9);
        }

        [Fact]
        public void A_closed_chain_stays_closed_and_does_not_repeat_its_first_point()
        {
            // A square with one corner lifted: the first and last points differ only in Z
            // before, and must not become a duplicate after.
            var square = new Polyline(
                new[] { P(0, 0, 0), P(10, 0, 0), P(10, 10, 4), P(0, 10, 4), P(0, 0, 4) },
                closed: true);

            Polyline flat = Flattening.Onto(square, 0);

            Assert.True(flat.IsClosed);
            Assert.Equal(4, flat.Count);
        }

        [Fact]
        public void Lying_at_a_level_is_judged_on_every_point()
        {
            var flat = new Polyline(new[] { P(0, 0, 2), P(10, 0, 2.00001) });
            var sloped = new Polyline(new[] { P(0, 0, 2), P(10, 0, 3) });

            Assert.True(Flattening.LiesAt(flat, 2, 1e-4));
            Assert.False(Flattening.LiesAt(sloped, 2, 1e-4));
        }
    }
}
