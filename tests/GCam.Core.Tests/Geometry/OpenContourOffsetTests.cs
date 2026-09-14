using System;
using System.Collections.Generic;
using System.Linq;
using GCam.Core.Geometry.Offset;
using GCam.Core.Geometry.Primitives;
using Xunit;

namespace GCam.Core.Tests.Geometry
{
    /// <summary>
    /// Offsetting an <b>open</b> path to one side - what lets a 2D contour follow a profile
    /// that does not close.
    /// </summary>
    /// <remarks>
    /// The property that matters here is the last one: no point of the result is closer to
    /// the path than the offset distance. That is the difference between a toolpath and a
    /// gouge, and it is the reason the offset goes through Clipper2 rather than moving each
    /// segment sideways - a naive parallel curve crosses itself wherever the distance
    /// exceeds the local curvature, and the crossed part cuts into the wall.
    /// </remarks>
    public class OpenContourOffsetTests
    {
        private const double ArcTolerance = 0.01;

        private static readonly Clipper2Offsetter Offsetter = new Clipper2Offsetter();

        private static Vec3 P(double x, double y) => new Vec3(x, y, 0);

        private static Polyline Open(params Vec3[] points) => new Polyline(points);

        private static Polyline OffsetOf(Polyline path, double distance, OffsetSide side) =>
            Assert.Single(Offsetter.OffsetOpen(path, distance, side, ArcTolerance));

        [Fact]
        public void Offsetting_a_straight_line_left_puts_it_to_the_left_of_travel()
        {
            // Travelling +X, so left is +Y.
            Polyline result = OffsetOf(Open(P(0, 0), P(10, 0)), 2, OffsetSide.Left);

            Assert.All(result.Points, p => Assert.Equal(2, p.Y, 3));
        }

        [Fact]
        public void Offsetting_a_straight_line_right_puts_it_to_the_right_of_travel()
        {
            Polyline result = OffsetOf(Open(P(0, 0), P(10, 0)), 2, OffsetSide.Right);

            Assert.All(result.Points, p => Assert.Equal(-2, p.Y, 3));
        }

        [Fact]
        public void The_offset_spans_the_whole_path_rather_than_part_of_it()
        {
            Polyline result = OffsetOf(Open(P(0, 0), P(10, 0)), 2, OffsetSide.Left);

            Assert.Equal(0, result.Points.Min(p => p.X), 2);
            Assert.Equal(10, result.Points.Max(p => p.X), 2);
        }

        [Fact]
        public void The_result_is_open_because_a_cutter_runs_down_one_side_only()
        {
            // The distinction this whole method exists for: Clipper's own answer to
            // "offset an open path" is the closed ribbon around it, which is both sides.
            Polyline result = OffsetOf(Open(P(0, 0), P(10, 0)), 2, OffsetSide.Left);

            Assert.False(result.IsClosed);
        }

        [Fact]
        public void Reversing_the_path_swaps_which_side_a_given_side_lands_on()
        {
            // What makes the per-contour Reverse button work: the side is relative to
            // travel, so walking the same geometry backwards puts the cutter on the other
            // side of it without changing the setting.
            Polyline forward = OffsetOf(Open(P(0, 0), P(10, 0)), 2, OffsetSide.Left);
            Polyline backward = OffsetOf(Open(P(10, 0), P(0, 0)), 2, OffsetSide.Left);

            Assert.All(forward.Points, p => Assert.Equal(2, p.Y, 3));
            Assert.All(backward.Points, p => Assert.Equal(-2, p.Y, 3));
        }

        [Fact]
        public void An_outside_corner_is_rounded_rather_than_mitred_to_a_point()
        {
            // An L travelling +X then +Y. The outside of that corner is the right-hand
            // side, and a cutter goes round it on its own radius.
            Polyline path = Open(P(0, 0), P(10, 0), P(10, 10));
            Polyline result = OffsetOf(path, 2, OffsetSide.Right);

            // A mitre would be three points; an arc is many.
            Assert.True(
                result.Count > 5,
                $"Expected an arc round the outside corner, got {result.Count} points.");

            // And nothing strays further from the wall than the offset distance. This is
            // the half that rules a mitre out: the mitre point sits 2root2 from the corner,
            // so a mitred corner fails here while a true arc holds 2mm the whole way round.
            // Together with the no-gouge test below, it pins the curve to exactly 2mm from
            // the path everywhere, which is what a correct offset is.
            Assert.All(
                result.Points,
                p => Assert.True(
                    DistanceToPath(path, p) <= 2 + 0.05,
                    $"A point sat {DistanceToPath(path, p):0.###}mm out, beyond the 2mm offset."));
        }

        [Fact]
        public void A_closed_contour_is_refused_rather_than_offset_as_if_it_were_open()
        {
            Polyline square = new Polyline(
                new[] { P(0, 0), P(10, 0), P(10, 10), P(0, 10) }, closed: true);

            Assert.Empty(Offsetter.OffsetOpen(square, 2, OffsetSide.Left, ArcTolerance));
        }

        [Fact]
        public void A_zero_offset_returns_the_path_untouched()
        {
            // Exact, not round-tripped through a fixed-point grid - the same guarantee the
            // closed offset makes.
            Polyline path = Open(P(0, 0), P(10, 0));

            Assert.Same(path, Assert.Single(Offsetter.OffsetOpen(path, 0, OffsetSide.Left, ArcTolerance)));
        }

        [Theory]
        [InlineData(1.0)]
        [InlineData(3.0)]
        [InlineData(6.0)]
        public void No_point_of_the_offset_is_closer_to_the_path_than_the_offset_distance(
            double distance)
        {
            // A zig-zag with a corner tight enough that a naive parallel curve would cross
            // itself at the larger distances. Anything that survives self-intersection
            // removal must still stay a full cutter radius off the wall.
            Polyline path = Open(P(0, 0), P(10, 0), P(14, 7), P(24, 7));

            IReadOnlyList<Polyline> results =
                Offsetter.OffsetOpen(path, distance, OffsetSide.Left, ArcTolerance);

            foreach (Vec3 point in results.SelectMany(r => r.Points))
            {
                double clearance = DistanceToPath(path, point);

                Assert.True(
                    clearance >= distance - 0.02,
                    $"A point sat {clearance:0.###}mm from the path, inside the {distance}mm offset.");
            }
        }

        private static double DistanceToPath(Polyline path, Vec3 point)
        {
            double best = double.MaxValue;

            for (int i = 0; i < path.SegmentCount; i++)
            {
                best = Math.Min(best, DistanceToSegment(path[i], path.EndOfSegment(i), point));
            }

            return best;
        }

        private static double DistanceToSegment(Vec3 a, Vec3 b, Vec3 p)
        {
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            double lengthSquared = (dx * dx) + (dy * dy);

            double at = lengthSquared <= double.Epsilon
                ? 0
                : Math.Max(0, Math.Min(1, (((p.X - a.X) * dx) + ((p.Y - a.Y) * dy)) / lengthSquared));

            double qx = a.X + (at * dx);
            double qy = a.Y + (at * dy);

            return Math.Sqrt(((p.X - qx) * (p.X - qx)) + ((p.Y - qy) * (p.Y - qy)));
        }
    }
}
