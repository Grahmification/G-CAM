using System.Collections.Generic;
using System.Linq;
using GCam.Core.Geometry;
using GCam.Core.Geometry.Primitives;
using Xunit;

namespace GCam.Core.Tests.Geometry
{
    public class EdgePropagationTests
    {
        private static Vec3 P(double x, double y, double z = 0) => new Vec3(x, y, z);

        /// <summary>
        /// A graph of straight edges, tangency stated outright.
        /// </summary>
        /// <remarks>
        /// Junctions are worked out from the endpoints, as the real topology's vertices
        /// do, so a test only has to lay out points.
        /// </remarks>
        private sealed class Edges : IEdgeTopology
        {
            private readonly List<Vec3> _starts = new List<Vec3>();
            private readonly List<Vec3> _ends = new List<Vec3>();
            private readonly HashSet<string> _tangent = new HashSet<string>();

            public int Add(Vec3 start, Vec3 end)
            {
                _starts.Add(start);
                _ends.Add(end);
                return _starts.Count - 1;
            }

            public Edges Tangent(int a, int b)
            {
                _tangent.Add(Key(a, b));
                return this;
            }

            public Vec3 StartOf(int edge) => _starts[edge];

            public Vec3 EndOf(int edge) => _ends[edge];

            public IReadOnlyList<int> Joining(int edge, bool atEnd)
            {
                Vec3 point = atEnd ? _ends[edge] : _starts[edge];

                return Enumerable.Range(0, _starts.Count)
                    .Where(i => i != edge && (Near(_starts[i], point) || Near(_ends[i], point)))
                    .ToList();
            }

            public bool AreTangent(int edge, int other) => _tangent.Contains(Key(edge, other));

            public bool LiesAt(int edge, double z) =>
                Flat(_starts[edge].Z, z) && Flat(_ends[edge].Z, z);

            private static string Key(int a, int b) => a < b ? a + "-" + b : b + "-" + a;

            private static bool Near(Vec3 a, Vec3 b) => (a - b).Length < 1e-6;

            private static bool Flat(double a, double b) => (a - b) < 1e-6 && (b - a) < 1e-6;
        }

        /// <summary>
        /// Three edges in a row at Z 0, each tangent to the next, and a fourth that turns
        /// a corner off the end of the third.
        /// </summary>
        private static Edges Row(out int first, out int second, out int third, out int corner)
        {
            var edges = new Edges();

            first = edges.Add(P(0, 0), P(10, 0));
            second = edges.Add(P(10, 0), P(20, 0));
            third = edges.Add(P(20, 0), P(30, 0));
            corner = edges.Add(P(30, 0), P(30, 10));

            edges.Tangent(first, second).Tangent(second, third);

            return edges;
        }

        [Fact]
        public void Tangential_propagation_runs_on_until_the_tangency_does()
        {
            Edges edges = Row(out int first, out int second, out int third, out _);

            IReadOnlyList<int> walked = EdgePropagation.Walk(
                first, edges, tangent: true, alongZ: false);

            Assert.Equal(new[] { first, second, third }, walked);
        }

        [Fact]
        public void Tangential_propagation_leaves_what_lies_behind_the_pick()
        {
            // The whole row is tangent, but the pick is in the middle of it: only what is
            // ahead of the arrow belongs to the cut.
            Edges edges = Row(out _, out int second, out int third, out _);

            IReadOnlyList<int> walked = EdgePropagation.Walk(
                second, edges, tangent: true, alongZ: false);

            Assert.Equal(new[] { second, third }, walked);
        }

        [Fact]
        public void Reversing_the_pick_propagates_the_other_way()
        {
            Edges edges = Row(out int first, out _, out int third, out _);

            IReadOnlyList<int> walked = EdgePropagation.Walk(
                third, edges, tangent: true, alongZ: false, forwards: false);

            Assert.Equal(3, walked.Count);
            Assert.Contains(first, walked);
        }

        [Fact]
        public void Propagating_along_Z_runs_both_ways_and_only_at_the_level()
        {
            var edges = new Edges();
            int left = edges.Add(P(0, 0), P(10, 0));
            int middle = edges.Add(P(10, 0), P(20, 0));
            int right = edges.Add(P(20, 0), P(30, 0));
            edges.Add(P(30, 0), P(30, 0, 10));

            IReadOnlyList<int> walked = EdgePropagation.Walk(
                middle, edges, tangent: false, alongZ: true);

            // No tangency is stated anywhere, so the level alone carried it - and the
            // riser off the far end is not at that level.
            Assert.Equal(new[] { middle, right, left }, walked);
        }

        [Fact]
        public void Either_rule_alone_carries_the_walk_across_a_junction()
        {
            // A tangent pair that climbs away from the picked level and comes back to it,
            // then a square corner that does not.
            var edges = new Edges();
            int picked = edges.Add(P(0, 0), P(10, 0));
            int up = edges.Add(P(10, 0), P(20, 0, 5));
            int down = edges.Add(P(20, 0, 5), P(30, 0));
            int corner = edges.Add(P(30, 0), P(30, 10));
            edges.Tangent(picked, up).Tangent(up, down);

            Assert.Equal(
                new[] { picked, up, down },
                EdgePropagation.Walk(picked, edges, tangent: true, alongZ: false));

            // Neither rule gets there alone: the level rule cannot leave the picked edge,
            // and tangency stops at the corner. Together they cross all three junctions.
            Assert.Equal(
                new[] { picked },
                EdgePropagation.Walk(picked, edges, tangent: false, alongZ: true));

            Assert.Equal(
                new[] { picked, up, down, corner },
                EdgePropagation.Walk(picked, edges, tangent: true, alongZ: true));
        }

        [Fact]
        public void A_tangent_continuation_beats_an_edge_that_is_merely_at_the_level()
        {
            // The junction a fillet makes on a face: something runs smoothly on, and
            // something else happens to lie at the same height. Counting that as a branch
            // stopped the walk at the very corners it exists to get round.
            var edges = new Edges();
            int picked = edges.Add(P(0, 0), P(10, 0));
            int smooth = edges.Add(P(10, 0), P(20, 0));
            edges.Add(P(10, 0), P(10, 10));
            edges.Tangent(picked, smooth);

            Assert.Equal(
                new[] { picked, smooth },
                EdgePropagation.Walk(picked, edges, tangent: true, alongZ: true));
        }

        [Fact]
        public void A_branch_stops_the_walk()
        {
            var edges = new Edges();
            int picked = edges.Add(P(0, 0), P(10, 0));
            edges.Add(P(10, 0), P(20, 0));
            edges.Add(P(10, 0), P(10, 10));

            Assert.Equal(
                new[] { picked },
                EdgePropagation.Walk(picked, edges, tangent: false, alongZ: true));
        }

        [Fact]
        public void A_closed_loop_comes_back_with_every_edge_once()
        {
            var edges = new Edges();
            int first = edges.Add(P(0, 0), P(10, 0));
            edges.Add(P(10, 0), P(10, 10));
            edges.Add(P(10, 10), P(0, 10));
            edges.Add(P(0, 10), P(0, 0));

            IReadOnlyList<int> walked = EdgePropagation.Walk(
                first, edges, tangent: false, alongZ: true);

            Assert.Equal(4, walked.Count);
            Assert.Equal(4, walked.Distinct().Count());
        }
    }
}
