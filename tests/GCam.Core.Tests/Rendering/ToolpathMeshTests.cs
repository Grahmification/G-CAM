using System;
using System.Collections.Generic;
using System.Linq;
using GCam.Core.Geometry.Primitives;
using GCam.Core.Model;
using GCam.Core.Rendering;
using Xunit;

namespace GCam.Core.Tests.Rendering
{
    public class ToolpathMeshTests
    {
        private static Vec3 P(double x, double y, double z = 0) => new Vec3(x, y, z);

        private static Toolpath Square()
        {
            return new Toolpath()
                .Add(Move.Rapid(P(0, 0, 10)))
                .Add(Move.Plunge(P(0, 0, -1), 200))
                .Add(Move.Cut(P(10, 0, -1), 500))
                .Add(Move.Cut(P(10, 10, -1), 500))
                .Add(Move.Retract(P(10, 10, 10), 1000));
        }

        [Fact]
        public void Nothing_to_draw_yields_no_batches()
        {
            Assert.Empty(ToolpathMesh.Build(null));
            Assert.Empty(ToolpathMesh.Build(new Toolpath()));
            Assert.Empty(ToolpathMesh.Build(new Toolpath().Add(Move.Rapid(P(0, 0, 10)))));
        }

        [Fact]
        public void Consecutive_moves_of_one_kind_become_a_single_strip()
        {
            // Two cutting moves are one strip of three points, not two strips of two.
            IReadOnlyList<RenderBatch> batches = ToolpathMesh.Build(Square());

            RenderBatch cutting = batches.Single(
                b => b.Colour.Equals(ToolpathMesh.ColourFor(MoveKind.Cutting)));

            Assert.Equal(PrimitiveKind.LineStrip, cutting.Kind);
            Assert.Equal(3, cutting.Vertices.Count);
        }

        [Fact]
        public void Each_kind_of_move_gets_its_own_colour()
        {
            IReadOnlyList<RenderBatch> batches = ToolpathMesh.Build(Square());

            // Plunge, cut, retract. The opening rapid goes nowhere - it only says where
            // the tool starts - so it contributes no segment.
            Assert.Equal(3, batches.Count);
            Assert.Equal(3, batches.Select(b => b.Colour).Distinct().Count());
        }

        [Fact]
        public void A_run_starts_where_the_previous_one_ended()
        {
            // Otherwise the path shows a gap at every change of kind, which reads as a
            // bug in the strategy rather than in the drawing.
            IReadOnlyList<RenderBatch> batches = ToolpathMesh.Build(Square());

            for (int i = 1; i < batches.Count; i++)
            {
                Vec3 endOfPrevious = batches[i - 1].Vertices.Last();
                Assert.Equal(endOfPrevious, batches[i].Vertices.First());
            }
        }

        [Fact]
        public void Rapids_can_be_left_out()
        {
            var path = new Toolpath()
                .Add(Move.Rapid(P(0, 0, 10)))
                .Add(Move.Cut(P(10, 0), 500))
                .Add(Move.Rapid(P(50, 50, 10)))
                .Add(Move.Cut(P(60, 50), 500));

            Assert.Equal(3, ToolpathMesh.Build(path).Count);

            IReadOnlyList<RenderBatch> without = ToolpathMesh.Build(path, showRapids: false);

            Assert.Equal(2, without.Count);
            Assert.DoesNotContain(without,
                b => b.Colour.Equals(ToolpathMesh.ColourFor(MoveKind.Rapid)));
        }

        [Fact]
        public void A_stale_path_is_faded_rather_than_hidden()
        {
            IReadOnlyList<RenderBatch> batches = ToolpathMesh.Build(Square(), stale: true);

            Assert.NotEmpty(batches);
            Assert.All(batches, b => Assert.Equal(ToolpathMesh.StaleAlpha, b.Colour.Alpha, 6));
            Assert.All(batches, b => Assert.True(b.Colour.IsTransparent));
        }

        [Fact]
        public void Cutting_is_drawn_heavier_than_the_moves_that_just_get_there()
        {
            IReadOnlyList<RenderBatch> batches = ToolpathMesh.Build(
                new Toolpath()
                    .Add(Move.Rapid(P(0, 0, 10)))
                    .Add(Move.Rapid(P(5, 0, 10)))
                    .Add(Move.Cut(P(10, 0), 500)));

            RenderBatch rapid = batches.Single(
                b => b.Colour.Equals(ToolpathMesh.ColourFor(MoveKind.Rapid)));
            RenderBatch cut = batches.Single(
                b => b.Colour.Equals(ToolpathMesh.ColourFor(MoveKind.Cutting)));

            Assert.True(cut.LineWidth > rapid.LineWidth);
        }

        [Fact]
        public void A_quarter_arc_is_tessellated_onto_the_arc()
        {
            // Quarter circle of radius 10 about the origin, counter-clockwise from (10,0)
            // to (0,10). Every point has to sit on the circle, not on the chord.
            var path = new Toolpath()
                .Add(Move.Rapid(P(10, 0)))
                .Add(Move.CutArc(P(0, 10), 500, Vec3.Zero, clockwise: false));

            RenderBatch batch = Assert.Single(ToolpathMesh.Build(path));

            Assert.True(batch.Vertices.Count > 4);
            Assert.All(batch.Vertices, v => Assert.Equal(10, new Vec3(v.X, v.Y, 0).Length, 3));

            // Counter-clockwise means it goes up through (7.07, 7.07), not down through
            // (7.07, -7.07).
            Assert.Contains(batch.Vertices, v => v.X > 0 && v.Y > 0 && v.Y < 10);
            Assert.DoesNotContain(batch.Vertices, v => v.Y < -Precision.Epsilon);
        }

        [Fact]
        public void A_clockwise_arc_goes_the_other_way_round()
        {
            var path = new Toolpath()
                .Add(Move.Rapid(P(10, 0)))
                .Add(Move.CutArc(P(0, 10), 500, Vec3.Zero, clockwise: true));

            RenderBatch batch = Assert.Single(ToolpathMesh.Build(path));

            // The long way round: through the bottom of the circle.
            Assert.Contains(batch.Vertices, v => v.Y < -1);
            Assert.All(batch.Vertices, v => Assert.Equal(10, new Vec3(v.X, v.Y, 0).Length, 3));
        }

        [Fact]
        public void An_arc_ends_exactly_where_it_was_told_to()
        {
            // Landing on an approximation would leave the next move starting somewhere the
            // previous one did not finish.
            var path = new Toolpath()
                .Add(Move.Rapid(P(10, 0)))
                .Add(Move.CutArc(P(0, 10), 500, Vec3.Zero, clockwise: false));

            Assert.Equal(P(0, 10), Assert.Single(ToolpathMesh.Build(path)).Vertices.Last());
        }

        [Fact]
        public void A_full_circle_sweeps_all_the_way_round_rather_than_standing_still()
        {
            // Start and end coincide, so the raw angle between them is zero.
            var path = new Toolpath()
                .Add(Move.Rapid(P(10, 0)))
                .Add(Move.CutArc(P(10, 0), 500, Vec3.Zero, clockwise: false));

            RenderBatch batch = Assert.Single(ToolpathMesh.Build(path));

            Assert.True(batch.Vertices.Count > 8);
            Assert.Contains(batch.Vertices, v => v.X < -9);
        }

        [Fact]
        public void A_helical_arc_climbs_as_it_turns()
        {
            var path = new Toolpath()
                .Add(Move.Rapid(P(10, 0, 0)))
                .Add(Move.CutArc(P(10, 0, -5), 500, Vec3.Zero, clockwise: false));

            IReadOnlyList<Vec3> vertices = Assert.Single(ToolpathMesh.Build(path)).Vertices;

            Assert.Equal(-5, vertices.Last().Z, 9);
            Assert.All(vertices, v => Assert.Equal(10, new Vec3(v.X, v.Y, 0).Length, 3));

            // Monotonically down: a flat circle followed by a plunge would be wrong.
            for (int i = 1; i < vertices.Count; i++)
            {
                Assert.True(vertices[i].Z <= vertices[i - 1].Z + Precision.Epsilon);
            }
        }

        [Fact]
        public void A_tighter_tolerance_uses_more_points()
        {
            var path = new Toolpath()
                .Add(Move.Rapid(P(10, 0)))
                .Add(Move.CutArc(P(0, 10), 500, Vec3.Zero, clockwise: false));

            int coarse = ToolpathMesh.Build(path, arcTolerance: 0.5).Single().Vertices.Count;
            int fine = ToolpathMesh.Build(path, arcTolerance: 0.001).Single().Vertices.Count;

            Assert.True(fine > coarse);
        }

        [Fact]
        public void A_degenerate_arc_is_drawn_as_a_straight_line_rather_than_vanishing()
        {
            // Nothing drawn looks like a gap in the path, and sends someone hunting for a
            // bug in the strategy instead of in the arc.
            var path = new Toolpath()
                .Add(Move.Rapid(P(0, 0)))
                .Add(Move.CutArc(P(10, 0), 500, Vec3.Zero, clockwise: false));

            RenderBatch batch = Assert.Single(ToolpathMesh.Build(path));

            Assert.Equal(2, batch.Vertices.Count);
            Assert.Equal(P(10, 0), batch.Vertices.Last());
        }

        [Fact]
        public void An_arc_in_the_ZX_plane_turns_about_Y()
        {
            var path = new Toolpath()
                .Add(Move.Rapid(P(10, 0, 0)))
                .Add(Move.CutArc(P(0, 0, 10), 500, Vec3.Zero, clockwise: false, plane: ArcPlane.ZX));

            IReadOnlyList<Vec3> vertices = Assert.Single(ToolpathMesh.Build(path)).Vertices;

            Assert.All(vertices, v => Assert.Equal(0, v.Y, 9));
            Assert.All(vertices, v => Assert.Equal(10, new Vec3(v.X, 0, v.Z).Length, 3));
        }

        [Fact]
        public void Layer_names_are_one_per_operation()
        {
            string name = ToolpathMesh.LayerName("op-1");

            Assert.StartsWith(ToolpathMesh.LayerPrefix, name);
            Assert.NotEqual(name, ToolpathMesh.LayerName("op-2"));
        }

        [Fact]
        public void Every_move_kind_has_a_colour()
        {
            foreach (MoveKind kind in Enum.GetValues(typeof(MoveKind)).Cast<MoveKind>())
            {
                Assert.False(ToolpathMesh.ColourFor(kind).IsTransparent);
            }
        }
    }
}
