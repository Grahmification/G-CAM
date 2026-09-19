using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using GCam.Core.Geometry.Offset;
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
    /// The cut-side arrows shown while an operation is being edited.
    /// </summary>
    /// <remarks>
    /// <b>Every test here compares the arrow against a generated toolpath</b> rather than
    /// against an expected direction written down by hand. The arrow's whole job is to say
    /// where the cut will be before it exists; a test that agreed with it about the rule
    /// while both were wrong would be worse than none. Comparing against the path that is
    /// actually produced is the only check that means anything.
    /// </remarks>
    public class Contour2dCutSideTests
    {
        private const double ToolDiameter = 10;

        /// <summary>A 100 x 60 rectangle at Z 0, counter-clockwise.</summary>
        private static Polyline Rectangle() => new Polyline(
            new[]
            {
                new Vec3(0, 0, 0),
                new Vec3(100, 0, 0),
                new Vec3(100, 60, 0),
                new Vec3(0, 60, 0),
            },
            closed: true);

        /// <summary>An open L, so the open-path rule is exercised as well as the closed one.</summary>
        private static Polyline OpenPath() => new Polyline(
            new[]
            {
                new Vec3(0, 0, 0),
                new Vec3(100, 0, 0),
                new Vec3(100, 40, 0),
            });

        private static GenerationContext Context(Contour2dSettings settings, Polyline contour)
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
                Geometry = { Diameter = ToolDiameter, FluteLength = 30 },
            };

            return new GenerationContext(
                new Job { Name = "Job 1" },
                operation,
                tool,
                new ResolvedHeights(40, 35, 32, 30, 0),
                new Bounds(new Vec3(-20, -20, 0), new Vec3(120, 80, 30)),
                new[] { contour });
        }

        [Theory]
        [InlineData(CutDirection.Climb, false)]
        [InlineData(CutDirection.Climb, true)]
        [InlineData(CutDirection.Conventional, false)]
        [InlineData(CutDirection.Conventional, true)]
        public void The_arrow_points_at_the_side_the_cutter_actually_runs(
            CutDirection direction, bool open)
        {
            var settings = new Contour2dSettings { Direction = direction };
            GenerationContext context = Context(settings, open ? OpenPath() : Rectangle());

            CutSideMarker marker = Markers(context, settings).Single();

            Vec3 towards = (NearestCut(context, marker.Anchor).Point - Flatten(marker.Anchor))
                .Normalised();

            // The cutter sits a radius away along the side vector, so the two should be
            // the same direction, not merely on the same half of the plane.
            Assert.True(
                towards.Dot(marker.Side) > 0.99,
                $"arrow points {marker.Side}, the cut is {towards} away");
        }

        [Theory]
        [InlineData(CutDirection.Climb)]
        [InlineData(CutDirection.Conventional)]
        public void The_arrow_points_the_way_the_cutter_travels(CutDirection direction)
        {
            var settings = new Contour2dSettings { Direction = direction };
            GenerationContext context = Context(settings, Rectangle());

            CutSideMarker marker = Markers(context, settings).Single();

            Assert.True(
                NearestCut(context, marker.Anchor).Direction.Dot(marker.Travel) > 0.99,
                $"arrow points along {marker.Travel}, the cut runs the other way");
        }

        [Fact]
        public void Reversing_a_contour_turns_the_arrow_round()
        {
            var settings = new Contour2dSettings();
            GenerationContext context = Context(settings, Rectangle());

            CutSideMarker forwards = Markers(context, settings).Single();

            var reversedContours = new[] { new ResolvedContour(Rectangle(), reversed: true) };

            CutSideMarker back = Contour2dCutSide
                .Markers(reversedContours, settings, new Clipper2Offsetter())
                .Single();

            Assert.True(
                back.Travel.Dot(forwards.Travel) < -0.99,
                "reversing should send the cutter the other way round");
        }

        private static IReadOnlyList<CutSideMarker> Markers(
            GenerationContext context, Contour2dSettings settings) =>
            Contour2dCutSide.Markers(context.Contours, settings, new Clipper2Offsetter());

        private static Toolpath Generate(GenerationContext context) =>
            new Contour2dStrategy().Generate(context, null, CancellationToken.None);

        /// <summary>
        /// Where the cut passes closest to a point, and which way it is running there.
        /// </summary>
        /// <remarks>
        /// The nearest point <i>on</i> the path, not its nearest vertex. A straight side
        /// of the offset rectangle has vertices only at its corners, so the nearest vertex
        /// to the middle of an edge is 55mm away along it - and the direction to it says
        /// nothing about which side the cut is on.
        /// </remarks>
        private static Cut NearestCut(GenerationContext context, Vec3 to)
        {
            Vec3 from = Flatten(to);

            var nearest = default(Cut);
            double away = double.MaxValue;

            foreach (var segment in CutSegments(context))
            {
                Vec3 step = segment.Value - segment.Key;
                double squared = step.Dot(step);

                if (squared <= 1e-12)
                {
                    continue;
                }

                double along = Math.Min(1, Math.Max(0, (from - segment.Key).Dot(step) / squared));
                Vec3 on = segment.Key + (step * along);

                if ((on - from).Length < away)
                {
                    away = (on - from).Length;
                    nearest = new Cut { Point = on, Direction = step.Normalised() };
                }
            }

            return nearest;
        }

        /// <summary>
        /// The cutting moves as segments, in plan.
        /// </summary>
        /// <remarks>
        /// From each cutting move's predecessor, because a move records only where it
        /// ends. Taking the ends alone loses the first cut of an open profile entirely -
        /// which is the whole of its longest run, and exactly where the arrow is.
        /// </remarks>
        private static IEnumerable<KeyValuePair<Vec3, Vec3>> CutSegments(GenerationContext context)
        {
            IReadOnlyList<Move> moves = Generate(context).Moves;

            for (int i = 1; i < moves.Count; i++)
            {
                if (moves[i].Kind == MoveKind.Cutting)
                {
                    yield return new KeyValuePair<Vec3, Vec3>(
                        Flatten(moves[i - 1].End), Flatten(moves[i].End));
                }
            }
        }

        private struct Cut
        {
            public Vec3 Point;

            public Vec3 Direction;
        }

        /// <summary>Contouring is a plan-view question; the depths only get in the way.</summary>
        private static Vec3 Flatten(Vec3 v) => new Vec3(v.X, v.Y, 0);
    }
}
