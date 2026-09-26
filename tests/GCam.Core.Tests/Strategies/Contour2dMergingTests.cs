using System;
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
    /// Several contours in one operation, whose cutter paths would cut into one another.
    /// </summary>
    /// <remarks>
    /// HSMWorks merges them - the inner of two concentric bosses is not cut, and two
    /// overlapping bosses are cut as one - and so does this. All through the strategy,
    /// with a Ø10 cutter and no leads, so every cutting move lands on a cutter path.
    /// </remarks>
    public class Contour2dMergingTests
    {
        private const double Radius = 5;

        /// <summary>How far a cutter point may sit inside a circle's true offset, mm.</summary>
        /// <remarks>
        /// The circles are 360-gons, so their offsets cut the corners of the true circle by
        /// a little under R(1 - cos 0.5°).
        /// </remarks>
        private const double Faceting = 0.05;

        private static Polyline Circle(double cx, double cy, double r, double z = 0) =>
            new Polyline(
                Enumerable.Range(0, 360).Select(i =>
                {
                    double a = i * Math.PI / 180;
                    return new Vec3(cx + (r * Math.Cos(a)), cy + (r * Math.Sin(a)), z);
                }),
                closed: true);

        private static Polyline Rectangle(double x0, double y0, double x1, double y1) =>
            new Polyline(
                new[] { new Vec3(x0, y0, 0), new Vec3(x1, y0, 0), new Vec3(x1, y1, 0), new Vec3(x0, y1, 0) },
                closed: true);

        private static Polyline Line(double x0, double y0, double x1, double y1) =>
            new Polyline(new[] { new Vec3(x0, y0, 0), new Vec3(x1, y1, 0) });

        /// <summary>Cut outside, with the default climb.</summary>
        private static ResolvedContour Boss(Polyline path, ResolvedHeights heights = null) =>
            new ResolvedContour(path, reversed: false, heights: heights);

        /// <summary>Cut inside, with the default climb.</summary>
        private static ResolvedContour Pocket(Polyline path) => new ResolvedContour(path, reversed: true);

        private static IReadOnlyList<IReadOnlyList<Vec3>> Passes(params ResolvedContour[] contours)
        {
            var settings = new Contour2dSettings();
            settings.LeadIn.Enabled = false;

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
                Geometry = { Diameter = Radius * 2, FluteLength = 30 },
            };

            var context = new GenerationContext(
                new Job { Name = "Job 1" },
                operation,
                tool,
                new ResolvedHeights(40, 35, 32, 30, 0),
                new Bounds(new Vec3(-100, -100, 0), new Vec3(200, 200, 30)),
                contours);

            Toolpath path = new Contour2dStrategy().Generate(context, null, CancellationToken.None);

            // One pass per plunge: where it lands, then every cut until the retract.
            var passes = new List<IReadOnlyList<Vec3>>();
            List<Vec3> current = null;

            foreach (Move move in path.Moves)
            {
                if (move.Kind == MoveKind.Plunge)
                {
                    current = new List<Vec3> { move.End };
                    passes.Add(current);
                }
                else if (move.Kind == MoveKind.Cutting)
                {
                    current?.Add(move.End);
                }
            }

            return passes;
        }

        private static double DistanceXy(Vec3 a, double x, double y) =>
            Math.Sqrt(((a.X - x) * (a.X - x)) + ((a.Y - y) * (a.Y - y)));

        [Fact]
        public void The_inner_of_two_concentric_bosses_is_not_cut()
        {
            // Its path would run at R15, through the material inside the outer boss.
            IReadOnlyList<IReadOnlyList<Vec3>> passes = Passes(Boss(Circle(0, 0, 20)), Boss(Circle(0, 0, 10)));

            IReadOnlyList<Vec3> only = Assert.Single(passes);
            Assert.All(only, p => Assert.InRange(DistanceXy(p, 0, 0), 25 - Faceting, 25 + Faceting));
        }

        [Fact]
        public void The_inner_boss_is_dropped_whichever_order_they_were_picked_in()
        {
            IReadOnlyList<IReadOnlyList<Vec3>> passes = Passes(Boss(Circle(0, 0, 10)), Boss(Circle(0, 0, 20)));

            IReadOnlyList<Vec3> only = Assert.Single(passes);
            Assert.All(only, p => Assert.InRange(DistanceXy(p, 0, 0), 25 - Faceting, 25 + Faceting));
        }

        [Fact]
        public void Two_overlapping_bosses_are_cut_as_one_path_round_the_outside_of_both()
        {
            IReadOnlyList<IReadOnlyList<Vec3>> passes = Passes(Boss(Circle(0, 0, 10)), Boss(Circle(15, 0, 10)));

            IReadOnlyList<Vec3> only = Assert.Single(passes);

            // Never inside either grown circle - which is where each path would gouge
            // the other boss - and reaching round the far side of both.
            Assert.All(only, p => Assert.True(DistanceXy(p, 0, 0) >= 15 - Faceting, $"{p} is inside the first"));
            Assert.All(only, p => Assert.True(DistanceXy(p, 15, 0) >= 15 - Faceting, $"{p} is inside the second"));
            Assert.InRange(only.Min(p => p.X), -15 - Faceting, -15 + Faceting);
            Assert.InRange(only.Max(p => p.X), 30 - Faceting, 30 + Faceting);
        }

        [Fact]
        public void The_merged_path_is_closed_and_keeps_climbing_counter_clockwise()
        {
            IReadOnlyList<Vec3> only = Assert.Single(Passes(Boss(Circle(0, 0, 10)), Boss(Circle(15, 0, 10))));

            Assert.True(DistanceXy(only[only.Count - 1], only[0].X, only[0].Y) <= 1e-6, "it does not come back to its start");
            Assert.True(new Polyline(only, closed: true).IsCounterClockwise);
        }

        [Fact]
        public void Bosses_whose_paths_do_not_meet_are_each_cut_as_they_are()
        {
            IReadOnlyList<IReadOnlyList<Vec3>> passes = Passes(Boss(Circle(0, 0, 10)), Boss(Circle(40, 0, 10)));

            Assert.Equal(2, passes.Count);
            Assert.All(passes[0], p => Assert.InRange(DistanceXy(p, 0, 0), 15 - Faceting, 15 + Faceting));
            Assert.All(passes[1], p => Assert.InRange(DistanceXy(p, 40, 0), 15 - Faceting, 15 + Faceting));
        }

        [Fact]
        public void Contours_cut_at_different_depths_are_not_merged()
        {
            // Heights measured from the contour, with the chains at different Z: the inner
            // boss stands proud of the outer one's floor, and is cut.
            var deep = new ResolvedHeights(40, 35, 32, 30, 0);
            var shallow = new ResolvedHeights(40, 35, 32, 30, 10);

            IReadOnlyList<IReadOnlyList<Vec3>> passes = Passes(
                Boss(Circle(0, 0, 20), deep), Boss(Circle(0, 0, 10, z: 10), shallow));

            Assert.Equal(2, passes.Count);
        }

        [Fact]
        public void An_island_well_clear_of_its_pocket_wall_is_cut_as_well_as_the_pocket()
        {
            IReadOnlyList<IReadOnlyList<Vec3>> passes = Passes(
                Pocket(Rectangle(0, 0, 100, 100)), Boss(Circle(50, 50, 10)));

            Assert.Equal(2, passes.Count);
        }

        [Fact]
        public void An_island_too_close_to_its_pocket_wall_merges_with_the_pocket_path()
        {
            // A 6mm gap to the floor edge, narrower than the Ø10 cutter.
            IReadOnlyList<IReadOnlyList<Vec3>> passes = Passes(
                Pocket(Rectangle(0, 0, 100, 100)), Boss(Circle(50, 16, 10)));

            IReadOnlyList<Vec3> only = Assert.Single(passes);

            Assert.All(only, p => Assert.True(DistanceXy(p, 50, 16) >= 15 - Faceting, $"{p} gouges the island"));
            Assert.All(only, p => Assert.True(
                p.X >= 5 - 1e-3 && p.X <= 95 + 1e-3 && p.Y >= 5 - 1e-3 && p.Y <= 95 + 1e-3,
                $"{p} gouges the pocket wall"));

            // The pocket path detours over the island rather than stopping at it.
            Assert.Contains(only, p => p.Y > 25);
            Assert.Contains(only, p => Math.Abs(p.Y - 5) < 1e-3 && p.X < 30);
            Assert.Contains(only, p => Math.Abs(p.Y - 5) < 1e-3 && p.X > 70);
        }

        [Fact]
        public void Two_overlapping_pockets_are_cut_as_one_path_round_their_union()
        {
            // Staggered, so no wall of one lines up with a wall of the other.
            IReadOnlyList<IReadOnlyList<Vec3>> passes = Passes(
                Pocket(Rectangle(0, 0, 40, 40)), Pocket(Rectangle(25, 10, 70, 50)));

            IReadOnlyList<Vec3> only = Assert.Single(passes);

            Assert.InRange(only.Min(p => p.X), 5 - 1e-3, 5 + 1e-3);
            Assert.InRange(only.Max(p => p.X), 65 - 1e-3, 65 + 1e-3);

            // Nothing is cut where either pocket's own wall runs through the other.
            Assert.DoesNotContain(only, p => Math.Abs(p.X - 35) < 1e-3 && p.Y > 15 + 1e-3 && p.Y < 35 - 1e-3);
            Assert.DoesNotContain(only, p => Math.Abs(p.X - 30) < 1e-3 && p.Y > 15 + 1e-3 && p.Y < 35 - 1e-3);
        }

        [Fact]
        public void A_pocket_inside_another_is_not_cut()
        {
            IReadOnlyList<IReadOnlyList<Vec3>> passes = Passes(
                Pocket(Rectangle(0, 0, 100, 100)), Pocket(Rectangle(30, 30, 70, 70)));

            IReadOnlyList<Vec3> only = Assert.Single(passes);
            Assert.InRange(only.Min(p => p.X), 5 - 1e-3, 5 + 1e-3);
        }

        [Fact]
        public void A_boss_elsewhere_on_the_part_is_not_kept_out_by_a_pocket()
        {
            // A pocket's material is the wall round it, not everything outside it.
            IReadOnlyList<IReadOnlyList<Vec3>> passes = Passes(
                Pocket(Rectangle(0, 0, 40, 40)), Boss(Circle(100, 20, 10)));

            Assert.Equal(2, passes.Count);
        }

        [Fact]
        public void Crossing_open_profiles_turn_the_corner_they_share_and_stop_short_of_each_other()
        {
            // Climb puts each cutter on the right: below the first, right of the second.
            IReadOnlyList<IReadOnlyList<Vec3>> passes = Passes(
                new ResolvedContour(Line(0, 0, 50, 0)), new ResolvedContour(Line(30, -20, 30, 30)));

            // Up the second to the corner below the first, then along the first: one pass.
            Assert.Contains(passes, pass =>
                pass.Any(p => DistanceXy(p, 35, -20) < 1e-3)
                && pass.Any(p => DistanceXy(p, 35, -5) < 1e-3)
                && pass.Any(p => DistanceXy(p, 50, -5) < 1e-3));

            // Nowhere does the cutter cross either line.
            foreach (Vec3 p in passes.SelectMany(pass => pass))
            {
                bool acrossFirst = p.X > 0 && p.X < 50 && Math.Abs(p.Y) < Radius - 1e-3;
                bool acrossSecond = p.Y > -20 && p.Y < 30 && Math.Abs(p.X - 30) < Radius - 1e-3;

                Assert.False(acrossFirst || acrossSecond, $"{p} puts the cutter across a line");
            }
        }

        [Fact]
        public void Facing_edges_of_two_bosses_closer_than_the_cutter_cuts_nothing()
        {
            // The facing walls of two bosses 5mm apart, for the Ø10 cutter - each path runs
            // along the other wall. The right-hand one overhangs the left by 3mm at each
            // end, and that overhang was cut until the bands got round ends: a selected
            // edge ends at a corner of its boss, not in air.
            IReadOnlyList<IReadOnlyList<Vec3>> passes = Passes(
                new ResolvedContour(Line(0, 0, 0, 20)), new ResolvedContour(Line(5, 23, 5, -3)));

            Assert.Empty(passes);
        }

        [Fact]
        public void A_facing_edge_that_runs_well_past_the_other_is_cut_clear_of_its_corners()
        {
            // The same, overhanging by 10mm: past the cutter's reach from the shorter
            // edge's ends there is nothing to keep it off. Clear to within the arc
            // tolerance, which the round ends are faceted to like any rounded corner.
            IReadOnlyList<IReadOnlyList<Vec3>> passes = Passes(
                new ResolvedContour(Line(0, 0, 0, 20)), new ResolvedContour(Line(5, 30, 5, -10)));

            Assert.Equal(2, passes.Count);
            Assert.All(passes.SelectMany(pass => pass), p =>
                Assert.True(
                    DistanceXy(p, 0, 0) >= Radius - 0.01 && DistanceXy(p, 0, 20) >= Radius - 0.01,
                    $"{p} cuts a corner of the shorter boss"));
        }

        [Fact]
        public void A_cavity_a_grown_boss_closes_off_is_cut_the_other_way_round()
        {
            // A C-shaped boss whose 4mm mouth the Ø10 cutter cannot enter: its grown shape
            // has a hole where the cavity is, and the cutter in there has the wall on its
            // other hand - clockwise, like any pocket climbed.
            var c = new Polyline(
                new[]
                {
                    new Vec3(0, 0, 0), new Vec3(60, 0, 0), new Vec3(60, 28, 0), new Vec3(50, 28, 0),
                    new Vec3(50, 10, 0), new Vec3(10, 10, 0), new Vec3(10, 50, 0), new Vec3(50, 50, 0),
                    new Vec3(50, 32, 0), new Vec3(60, 32, 0), new Vec3(60, 60, 0), new Vec3(0, 60, 0),
                },
                closed: true);

            IReadOnlyList<IReadOnlyList<Vec3>> passes = Passes(Boss(c));

            Assert.Equal(2, passes.Count);

            IReadOnlyList<Vec3> outside = passes.Single(pass => pass.Min(p => p.X) < 0);
            IReadOnlyList<Vec3> cavity = passes.Single(pass => pass.Min(p => p.X) > 0);

            Assert.True(new Polyline(outside, closed: true).IsCounterClockwise);
            Assert.False(new Polyline(cavity, closed: true).IsCounterClockwise);
        }

        [Fact]
        public void Every_piece_of_a_pinched_pocket_is_cut()
        {
            // Two 30mm rooms joined by a 4mm corridor the Ø10 cutter cannot enter. Only
            // the larger room was cut until 2026-09-26.
            var dumbbell = new Polyline(
                new[]
                {
                    new Vec3(0, 0, 0), new Vec3(30, 0, 0), new Vec3(30, 13, 0), new Vec3(50, 13, 0),
                    new Vec3(50, 0, 0), new Vec3(80, 0, 0), new Vec3(80, 30, 0), new Vec3(50, 30, 0),
                    new Vec3(50, 17, 0), new Vec3(30, 17, 0), new Vec3(30, 30, 0), new Vec3(0, 30, 0),
                },
                closed: true);

            IReadOnlyList<IReadOnlyList<Vec3>> passes = Passes(Pocket(dumbbell));

            Assert.Equal(2, passes.Count);
            Assert.Contains(passes, pass => pass.All(p => p.X < 40));
            Assert.Contains(passes, pass => pass.All(p => p.X > 40));
        }
    }
}
