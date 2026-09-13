using GCam.Core.Geometry.Primitives;
using GCam.Core.Model;
using GCam.Core.Strategies.Contour2d;
using Xunit;

namespace GCam.Core.Tests.Model
{
    public class ToolpathTests
    {
        private static Vec3 P(double x, double y, double z = 0) => new Vec3(x, y, z);

        [Fact]
        public void A_path_with_one_move_is_empty_because_it_goes_nowhere()
        {
            // The first move only says where the tool starts.
            var path = new Toolpath().Add(Move.Rapid(P(0, 0, 10)));

            Assert.True(path.IsEmpty);
            Assert.Equal(P(0, 0, 10), path.Start.Value);
        }

        [Fact]
        public void A_path_with_somewhere_to_go_is_not_empty()
        {
            var path = new Toolpath()
                .Add(Move.Rapid(P(0, 0, 10)))
                .Add(Move.Cut(P(10, 0, 0), 500));

            Assert.False(path.IsEmpty);
            Assert.Equal(2, path.Moves.Count);
        }

        [Fact]
        public void An_empty_path_has_no_start_and_no_extent()
        {
            var path = new Toolpath();

            Assert.Null(path.Start);
            Assert.Null(path.Extent);
            Assert.True(path.IsEmpty);
        }

        [Fact]
        public void The_extent_covers_every_move_endpoint()
        {
            var path = new Toolpath()
                .Add(Move.Rapid(P(0, 0, 10)))
                .Add(Move.Cut(P(10, 5, -2), 500))
                .Add(Move.Cut(P(-3, 8, 1), 500));

            Bounds extent = path.Extent.Value;

            Assert.Equal(-3, extent.Min.X, 9);
            Assert.Equal(0, extent.Min.Y, 9);
            Assert.Equal(-2, extent.Min.Z, 9);
            Assert.Equal(10, extent.Max.X, 9);
            Assert.Equal(8, extent.Max.Y, 9);
            Assert.Equal(10, extent.Max.Z, 9);
        }

        [Fact]
        public void Nulls_are_not_added()
        {
            Assert.Throws<System.ArgumentNullException>(() => new Toolpath().Add(null));
            Assert.Empty(new Toolpath(new Move[] { null, null }).Moves);
        }

        [Fact]
        public void A_move_carries_its_destination_not_its_start()
        {
            // The tool is wherever the previous move left it, so a move cannot disagree
            // with the one before it about where the tool is.
            Move move = Move.Cut(P(10, 0), 500);

            Assert.Equal(P(10, 0), move.End);
            Assert.Equal(500, move.Feed, 9);
            Assert.Equal(MoveKind.Cutting, move.Kind);
            Assert.False(move.IsArc);
            Assert.True(move.IsFeedMove);
        }

        [Fact]
        public void A_rapid_has_no_feed_and_is_not_a_feed_move()
        {
            Move rapid = Move.Rapid(P(0, 0, 10));

            Assert.Equal(0, rapid.Feed, 9);
            Assert.False(rapid.IsFeedMove);
        }

        [Fact]
        public void An_arc_move_keeps_its_centre_and_direction()
        {
            Move move = Move.CutArc(P(10, 10), 500, P(10, 0), clockwise: false);

            Assert.True(move.IsArc);
            Assert.Equal(P(10, 0), move.Arc.Centre);
            Assert.False(move.Arc.Clockwise);
            Assert.Equal(ArcPlane.XY, move.Arc.Plane);
        }

        [Theory]
        [InlineData(ArcPlane.XY, 0, 0, 1)]
        [InlineData(ArcPlane.ZX, 0, 1, 0)]
        [InlineData(ArcPlane.YZ, 1, 0, 0)]
        public void Each_arc_plane_knows_its_normal(ArcPlane plane, double x, double y, double z)
        {
            var arc = new ArcData(Vec3.Zero, clockwise: true, plane: plane);

            Assert.Equal(new Vec3(x, y, z), arc.Normal);
        }

        [Fact]
        public void Arc_planes_are_numbered_as_G_codes()
        {
            // So a post maps them without a lookup table.
            Assert.Equal(17, (int)ArcPlane.XY);
            Assert.Equal(18, (int)ArcPlane.ZX);
            Assert.Equal(19, (int)ArcPlane.YZ);
        }

        [Fact]
        public void Cloning_a_path_does_not_let_the_copy_change_the_original()
        {
            var original = new Toolpath()
                .Add(Move.Rapid(P(0, 0, 10)))
                .Add(Move.Cut(P(10, 0), 500));

            Toolpath copy = original.Clone();
            copy.Add(Move.Cut(P(20, 0), 500));

            Assert.Equal(2, original.Moves.Count);
            Assert.Equal(3, copy.Moves.Count);
        }

        [Fact]
        public void An_operation_carries_its_toolpath_through_a_duplicate()
        {
            var operation = new Operation(new Contour2dSettings())
            {
                Name = "2D Contour1",
                State = OperationState.Generated,
                Toolpath = new Toolpath()
                    .Add(Move.Rapid(P(0, 0, 10)))
                    .Add(Move.Cut(P(10, 0), 500)),
            };

            Operation clone = operation.Clone();

            Assert.Equal(2, clone.Toolpath.Moves.Count);
            Assert.NotSame(operation.Toolpath, clone.Toolpath);
        }

        [Fact]
        public void A_copy_that_stands_on_its_own_starts_with_no_toolpath()
        {
            // Keeping the path while resetting the state would leave a stored result that
            // nothing admits to having produced.
            var operation = new Operation(new Contour2dSettings())
            {
                State = OperationState.Generated,
                Toolpath = new Toolpath().Add(Move.Rapid(P(0, 0, 10))),
            };

            Operation copy = operation.CloneAsNew();

            Assert.Null(copy.Toolpath);
            Assert.Equal(OperationState.NotGenerated, copy.State);
        }
    }
}
