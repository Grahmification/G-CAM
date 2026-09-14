using System.Collections.Generic;
using System.Linq;
using GCam.Core.Geometry;
using GCam.Core.Geometry.Primitives;
using Xunit;

namespace GCam.Core.Tests.Geometry
{
    public class ChainingTests
    {
        private static Vec3 P(double x, double y, double z = 0) => new Vec3(x, y, z);

        private static Polyline Segment(Vec3 from, Vec3 to) => new Polyline(new[] { from, to });

        /// <summary>The four sides of a 10 x 6 rectangle, in order, all pointing the same way.</summary>
        private static List<Polyline> RectangleSides()
        {
            return new List<Polyline>
            {
                Segment(P(0, 0), P(10, 0)),
                Segment(P(10, 0), P(10, 6)),
                Segment(P(10, 6), P(0, 6)),
                Segment(P(0, 6), P(0, 0)),
            };
        }

        [Fact]
        public void Segments_that_meet_end_to_end_become_one_closed_loop()
        {
            Polyline loop = Assert.Single(Chaining.ChainIntoLoops(RectangleSides()));

            Assert.True(loop.IsClosed);

            // Closed means the first point is not repeated, so four sides are four points.
            Assert.Equal(4, loop.Count);
            Assert.Equal(32, loop.Length, 6);
        }

        [Fact]
        public void The_order_they_arrive_in_does_not_matter()
        {
            // SOLIDWORKS hands back a selection in no particular order.
            List<Polyline> shuffled = RectangleSides();
            shuffled = new List<Polyline> { shuffled[2], shuffled[0], shuffled[3], shuffled[1] };

            Polyline loop = Assert.Single(Chaining.ChainIntoLoops(shuffled));

            Assert.True(loop.IsClosed);
            Assert.Equal(4, loop.Count);
        }

        [Fact]
        public void A_segment_pointing_the_wrong_way_is_turned_round()
        {
            // Nor does SOLIDWORKS promise a consistent direction.
            var sides = new List<Polyline>
            {
                Segment(P(0, 0), P(10, 0)),
                Segment(P(10, 6), P(10, 0)),   // reversed
                Segment(P(10, 6), P(0, 6)),
                Segment(P(0, 0), P(0, 6)),     // reversed
            };

            Polyline loop = Assert.Single(Chaining.ChainIntoLoops(sides));

            Assert.True(loop.IsClosed);
            Assert.Equal(4, loop.Count);
            Assert.Equal(32, loop.Length, 6);
        }

        [Fact]
        public void Ends_within_the_tolerance_are_treated_as_meeting()
        {
            // Tessellating two edges separately does not give bit-identical endpoints even
            // when the edges genuinely meet.
            var sides = new List<Polyline>
            {
                Segment(P(0, 0), P(10, 0)),
                Segment(P(10.000001, 0), P(10, 6)),
                Segment(P(10, 6), P(0, 6)),
                Segment(P(0, 6), P(0, 0)),
            };

            Assert.True(Assert.Single(Chaining.ChainIntoLoops(sides)).IsClosed);
        }

        [Fact]
        public void A_gap_wider_than_the_tolerance_leaves_the_chain_open()
        {
            var sides = RectangleSides();
            sides[3] = Segment(P(0, 6), P(0, 1));   // stops short of the start

            Polyline chain = Assert.Single(Chaining.ChainIntoLoops(sides));

            Assert.False(chain.IsClosed);
        }

        [Fact]
        public void Two_separate_profiles_come_back_as_two_loops()
        {
            List<Polyline> sides = RectangleSides();
            sides.AddRange(new[]
            {
                Segment(P(50, 50), P(60, 50)),
                Segment(P(60, 50), P(60, 56)),
                Segment(P(60, 56), P(50, 56)),
                Segment(P(50, 56), P(50, 50)),
            });

            IReadOnlyList<Polyline> loops = Chaining.ChainIntoLoops(sides);

            Assert.Equal(2, loops.Count);
            Assert.All(loops, l => Assert.True(l.IsClosed));
        }

        [Fact]
        public void A_piece_that_only_fits_after_another_one_still_gets_joined()
        {
            // The greedy sweep has to keep going: joining one piece on can make an earlier
            // one fit that did not before.
            var sides = new List<Polyline>
            {
                Segment(P(0, 0), P(10, 0)),
                Segment(P(10, 6), P(0, 6)),    // does not touch the first
                Segment(P(10, 0), P(10, 6)),   // joins them once it is placed
                Segment(P(0, 6), P(0, 0)),
            };

            Assert.True(Assert.Single(Chaining.ChainIntoLoops(sides)).IsClosed);
        }

        [Fact]
        public void Multi_point_segments_chain_the_same_way()
        {
            // A tessellated arc arrives as many points, not two.
            var sides = new List<Polyline>
            {
                new Polyline(new[] { P(0, 0), P(5, 0), P(10, 0) }),
                new Polyline(new[] { P(10, 0), P(10, 3), P(10, 6) }),
                new Polyline(new[] { P(10, 6), P(0, 6) }),
                new Polyline(new[] { P(0, 6), P(0, 0) }),
            };

            Polyline loop = Assert.Single(Chaining.ChainIntoLoops(sides));

            Assert.True(loop.IsClosed);
            Assert.Equal(6, loop.Count);
            Assert.Equal(32, loop.Length, 6);
        }

        [Fact]
        public void Shared_points_are_not_duplicated_where_pieces_meet()
        {
            IReadOnlyList<Vec3> points = Chaining
                .ChainIntoLoops(RectangleSides()).Single().Points;

            Assert.Equal(points.Count, points.Distinct().Count());
        }

        [Fact]
        public void A_single_open_segment_comes_back_as_itself()
        {
            Polyline chain = Assert.Single(
                Chaining.ChainIntoLoops(new[] { Segment(P(0, 0), P(10, 0)) }));

            Assert.False(chain.IsClosed);
            Assert.Equal(2, chain.Count);
        }

        [Fact]
        public void Nothing_in_means_nothing_out()
        {
            Assert.Empty(Chaining.ChainIntoLoops(null));
            Assert.Empty(Chaining.ChainIntoLoops(new Polyline[0]));
            Assert.Empty(Chaining.ChainIntoLoops(new[] { new Polyline(new[] { P(0, 0) }) }));
        }

        [Fact]
        public void A_chained_loop_can_be_offset_and_cut()
        {
            // The end of the extraction path meets the start of the strategy path: what
            // comes out of here has to be what Contour2dStrategy expects.
            Polyline loop = Chaining.ChainIntoLoops(RectangleSides()).Single();

            Assert.True(loop.IsClosed);
            Assert.NotEqual(0, loop.SignedAreaXy2);
            Assert.Equal(4, loop.SegmentCount);
        }

        [Fact]
        public void A_chain_remembers_every_segment_that_went_into_it()
        {
            // What makes the Reverse button work: four separate picks become one loop, and
            // reversing any of them has to reverse that loop - so the loop has to know
            // which picks it came from. By the time it is a Polyline they are unrecognisable.
            Chaining.Chain chain = Assert.Single(Chaining.ChainWithSources(RectangleSides()));

            Assert.Equal(new[] { 0, 1, 2, 3 }, chain.Sources);
        }

        [Fact]
        public void Separate_chains_keep_their_own_segments_apart()
        {
            var segments = new List<Polyline>
            {
                Segment(P(0, 0), P(10, 0)),
                Segment(P(50, 0), P(60, 0)),
                Segment(P(10, 0), P(10, 5)),
            };

            IReadOnlyList<Chaining.Chain> chains = Chaining.ChainWithSources(segments);

            Assert.Equal(2, chains.Count);
            Assert.Equal(new[] { 0, 2 }, chains[0].Sources);
            Assert.Equal(new[] { 1 }, chains[1].Sources);
        }

        [Fact]
        public void Source_indices_count_the_segments_that_were_skipped()
        {
            // Indices are into the caller's own list, so a null or degenerate entry still
            // takes its place. Anything looking a source back up would otherwise read the
            // wrong pick - and silently, since the numbers stay in range.
            var segments = new List<Polyline>
            {
                null,
                new Polyline(new[] { P(0, 0) }),
                Segment(P(0, 0), P(10, 0)),
            };

            Chaining.Chain chain = Assert.Single(Chaining.ChainWithSources(segments));

            Assert.Equal(new[] { 2 }, chain.Sources);
        }
    }
}
