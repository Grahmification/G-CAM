using System;
using GCam.Core.Geometry.Primitives;
using Xunit;

namespace GCam.Core.Tests.Geometry
{
    public class Matrix4Tests
    {
        [Fact]
        public void Identity_leaves_a_point_where_it_is()
        {
            var point = new Vec3(3, -7, 11);

            Assert.True(Matrix4.Identity.Transform(point).Equals(point));
        }

        [Fact]
        public void FromAxes_puts_the_axes_in_the_columns()
        {
            // The contract the SOLIDWORKS side depends on: measure where the origin and
            // the three unit axes land, hand them to FromAxes, and get the transform that
            // does it. If this ever stops holding, coordinate systems silently transpose.
            var origin = new Vec3(10, 20, 30);
            var x = new Vec3(0, 1, 0);
            var y = new Vec3(-1, 0, 0);
            var z = new Vec3(0, 0, 1);

            Matrix4 frame = Matrix4.FromAxes(origin, x, y, z);

            Assert.True(frame.Transform(Vec3.Zero).Equals(origin));
            Assert.True(frame.Transform(new Vec3(1, 0, 0)).Equals(origin + x));
            Assert.True(frame.Transform(new Vec3(0, 1, 0)).Equals(origin + y));
            Assert.True(frame.Transform(new Vec3(0, 0, 1)).Equals(origin + z));
        }

        [Fact]
        public void A_quarter_turn_about_Z_takes_X_onto_Y()
        {
            Matrix4 rotate = QuarterTurnAboutZ();

            Assert.True(rotate.Transform(new Vec3(1, 0, 0)).Equals(new Vec3(0, 1, 0)));
            Assert.True(rotate.Transform(new Vec3(0, 1, 0)).Equals(new Vec3(-1, 0, 0)));
            Assert.True(rotate.Transform(new Vec3(0, 0, 1)).Equals(new Vec3(0, 0, 1)));
        }

        [Fact]
        public void TransformDirection_rotates_without_moving()
        {
            // The distinction that goes wrong quietly: a direction must not pick up the
            // origin offset, or every normal and search direction ends up pointing at the
            // coordinate system instead of along it.
            Matrix4 moved = Matrix4.FromAxes(
                new Vec3(100, 200, 300),
                new Vec3(1, 0, 0), new Vec3(0, 1, 0), new Vec3(0, 0, 1));

            var direction = new Vec3(0, 0, 1);

            Assert.True(moved.TransformDirection(direction).Equals(direction));
            Assert.True(moved.Transform(direction).Equals(new Vec3(100, 200, 301)));
        }

        [Fact]
        public void Then_applies_this_transform_first()
        {
            // Reads left to right, whatever the matrix multiplication underneath does.
            Matrix4 rotate = QuarterTurnAboutZ();
            Matrix4 shift = Matrix4.FromAxes(
                new Vec3(5, 0, 0), new Vec3(1, 0, 0), new Vec3(0, 1, 0), new Vec3(0, 0, 1));

            var start = new Vec3(1, 0, 0);

            // Rotate onto +Y, then shift along +X.
            Assert.True(rotate.Then(shift).Transform(start).Equals(new Vec3(5, 1, 0)));

            // Shift to (6, 0, 0) first, then rotate it onto +Y.
            Assert.True(shift.Then(rotate).Transform(start).Equals(new Vec3(0, 6, 0)));
        }

        [Fact]
        public void Then_matches_applying_the_two_transforms_in_turn()
        {
            Matrix4 first = QuarterTurnAboutZ();
            Matrix4 second = Matrix4.FromAxes(
                new Vec3(1, 2, 3), new Vec3(0, 0, 1), new Vec3(1, 0, 0), new Vec3(0, 1, 0));

            var point = new Vec3(7, -3, 2);

            Assert.True(
                first.Then(second).Transform(point)
                    .Equals(second.Transform(first.Transform(point))));
        }

        /// <summary>90 degrees about Z: X goes to Y, Y goes to -X.</summary>
        private static Matrix4 QuarterTurnAboutZ() => Matrix4.FromAxes(
            Vec3.Zero, new Vec3(0, 1, 0), new Vec3(-1, 0, 0), new Vec3(0, 0, 1));
    }
}
