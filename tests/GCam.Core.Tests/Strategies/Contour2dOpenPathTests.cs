using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using GCam.Core.Geometry.Primitives;
using GCam.Core.Model;
using GCam.Core.Model.Heights;
using GCam.Core.Strategies;
using GCam.Core.Strategies.Contour2d;
using GCam.Core.Strategies.Shared;
using GCam.Core.Tooling;
using Xunit;

namespace GCam.Core.Tests.Strategies
{
    /// <summary>
    /// Contouring a profile that does not close, and choosing which side of it to cut.
    /// </summary>
    /// <remarks>
    /// Open profiles were refused until 2026-09-13 because the offsetter could not do
    /// them. The side is the part worth pinning: a closed contour carries it in its
    /// orientation, but an open one has no inside, so it is decided by the cut direction
    /// and flipped per contour by <see cref="ResolvedContour.Reversed"/> - which is what
    /// the Reverse button on the Geometry tab sets.
    /// </remarks>
    public class Contour2dOpenPathTests
    {
        private const double ToolDiameter = 10;

        /// <summary>A straight 100mm run along +X at Z 0, open.</summary>
        private static Polyline Line() =>
            new Polyline(new[] { new Vec3(0, 0, 0), new Vec3(100, 0, 0) });

        private static GenerationContext Context(
            ResolvedContour contour, Contour2dSettings settings = null)
        {
            settings = settings ?? new Contour2dSettings();

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
                Geometry = { Diameter = ToolDiameter, FluteLength = 30 },
            };

            return new GenerationContext(
                new Job { Name = "Job 1" },
                operation,
                tool,
                new ResolvedHeights(40, 35, 32, 30, 0),
                new Bounds(new Vec3(-20, -20, 0), new Vec3(120, 20, 30)),
                new[] { contour });
        }

        private static Toolpath Generate(GenerationContext context) =>
            new Contour2dStrategy().Generate(context, null, CancellationToken.None);

        /// <summary>The Y the cutting moves run at, which says which side was cut.</summary>
        private static IReadOnlyList<double> CuttingYs(Toolpath path) =>
            path.Moves.Where(m => m.Kind == MoveKind.Cutting).Select(m => m.End.Y).ToList();

        [Fact]
        public void An_open_profile_is_cut_rather_than_refused()
        {
            Toolpath path = Generate(Context(new ResolvedContour(Line())));

            Assert.NotEmpty(CuttingYs(path));
        }

        [Fact]
        public void A_climb_cut_puts_the_cutter_to_the_right_of_travel()
        {
            // Travelling +X with the material on the left is climb milling for a cutter
            // turning clockwise seen from above, so the cutter centre sits at -Y.
            var settings = new Contour2dSettings { Direction = CutDirection.Climb };

            Toolpath path = Generate(Context(new ResolvedContour(Line()), settings));

            Assert.All(CuttingYs(path), y => Assert.Equal(-ToolDiameter / 2, y, 2));
        }

        [Fact]
        public void A_conventional_cut_keeps_the_side_and_reverses_the_travel()
        {
            // It swapped sides until 2026-09-19, which is not what the words mean: with
            // the cutter on a given side, reversing the feed is exactly what turns a climb
            // cut into a conventional one. Choosing the side is the Reverse button's job.
            var settings = new Contour2dSettings { Direction = CutDirection.Conventional };

            Toolpath path = Generate(Context(new ResolvedContour(Line()), settings));

            Assert.All(CuttingYs(path), y => Assert.Equal(-ToolDiameter / 2, y, 2));

            // The line is picked running +X, so a conventional cut runs -X. Measured from
            // where the tool comes down to where the cut ends: a two-point offset is a
            // single cutting move, so comparing cutting moves to each other compares one
            // move with itself.
            double from = path.Moves.First(m => m.Kind == MoveKind.Plunge).End.X;
            double to = path.Moves.Last(m => m.Kind == MoveKind.Cutting).End.X;

            Assert.True(
                to < from,
                $"a conventional cut should run back down the edge; it went {from:0.#} to {to:0.#}");
        }

        [Fact]
        public void Reversing_a_contour_swaps_the_side_it_is_cut_on()
        {
            // What the Reverse button is for: the same geometry, the same settings, the
            // other side - without re-picking anything.
            var settings = new Contour2dSettings { Direction = CutDirection.Climb };

            Toolpath forward = Generate(Context(new ResolvedContour(Line()), settings));
            Toolpath reversed = Generate(
                Context(new ResolvedContour(Line(), reversed: true), settings));

            Assert.All(CuttingYs(forward), y => Assert.Equal(-ToolDiameter / 2, y, 2));
            Assert.All(CuttingYs(reversed), y => Assert.Equal(ToolDiameter / 2, y, 2));
        }

        [Fact]
        public void Stock_to_leave_moves_the_cutter_further_off_an_open_profile()
        {
            var settings = new Contour2dSettings
            {
                Direction = CutDirection.Climb,
                StockToLeave = 2,
            };

            Toolpath path = Generate(Context(new ResolvedContour(Line()), settings));

            Assert.All(CuttingYs(path), y => Assert.Equal(-((ToolDiameter / 2) + 2), y, 2));
        }

        [Fact]
        public void An_open_profile_is_not_closed_back_to_its_start()
        {
            // The bug this guards: a closed contour gets a final move back to its first
            // point. Doing that to an open one would cut a straight line back across the
            // job from the far end.
            Toolpath path = Generate(Context(new ResolvedContour(Line())));

            IReadOnlyList<Move> cuts =
                path.Moves.Where(m => m.Kind == MoveKind.Cutting).ToList();

            Assert.True(cuts.Count > 0);
            Assert.Equal(100, cuts[cuts.Count - 1].End.X, 1);
        }

        [Fact]
        public void Several_fragments_each_get_their_own_pass()
        {
            // Two separate runs, not continuous. Each is cut on its own, with its own
            // retract between - the tool does not stay down to link them.
            var first = new ResolvedContour(
                new Polyline(new[] { new Vec3(0, 0, 0), new Vec3(40, 0, 0) }));
            var second = new ResolvedContour(
                new Polyline(new[] { new Vec3(60, 0, 0), new Vec3(100, 0, 0) }));

            var settings = new Contour2dSettings();
            GenerationContext context = Context(first, settings);

            var both = new GenerationContext(
                context.Job,
                context.Operation,
                context.Tool,
                context.Heights,
                context.Stock,
                new[] { first, second });

            Toolpath path = Generate(both);

            Assert.Equal(2, path.Moves.Count(m => m.Kind == MoveKind.Retract));
            Assert.Equal(2, path.Moves.Count(m => m.Kind == MoveKind.Plunge));
        }
    }
}
