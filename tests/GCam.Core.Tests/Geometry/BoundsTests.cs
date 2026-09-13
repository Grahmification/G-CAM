using System;
using GCam.Core.Geometry.Primitives;
using Xunit;

namespace GCam.Core.Tests.Geometry
{
    public class BoundsTests
    {
        [Fact]
        public void Size_is_the_extent_on_each_axis()
        {
            var box = new Bounds(new Vec3(1, 2, 3), new Vec3(11, 22, 33));

            Assert.Equal(10, box.Size.X, 6);
            Assert.Equal(20, box.Size.Y, 6);
            Assert.Equal(30, box.Size.Z, 6);
        }

        [Fact]
        public void Centre_is_halfway_along_each_axis()
        {
            var box = new Bounds(new Vec3(0, 0, 0), new Vec3(10, 20, 30));

            Assert.Equal(5, box.Centre.X, 6);
            Assert.Equal(10, box.Centre.Y, 6);
            Assert.Equal(15, box.Centre.Z, 6);
        }

        [Fact]
        public void A_box_with_no_thickness_on_one_axis_is_empty()
        {
            // A flat face has area but nothing to machine out of, so callers need to be
            // able to tell.
            var flat = new Bounds(new Vec3(0, 0, 0), new Vec3(10, 10, 0));

            Assert.True(flat.IsEmpty);
        }

        [Fact]
        public void FromPoints_spans_every_point_given()
        {
            Bounds box = Bounds.FromPoints(new[]
            {
                new Vec3(5, 5, 5),
                new Vec3(-1, 10, 2),
                new Vec3(3, 0, 7),
            });

            Assert.Equal(-1, box.Min.X, 6);
            Assert.Equal(0, box.Min.Y, 6);
            Assert.Equal(2, box.Min.Z, 6);
            Assert.Equal(5, box.Max.X, 6);
            Assert.Equal(10, box.Max.Y, 6);
            Assert.Equal(7, box.Max.Z, 6);
        }

        [Fact]
        public void FromPoints_refuses_an_empty_set_rather_than_returning_a_box_at_the_origin()
        {
            // A silent (0,0,0) box would put stock in the wrong place rather than fail.
            Assert.Throws<ArgumentException>(() => Bounds.FromPoints(new Vec3[0]));
        }

        [Fact]
        public void Union_covers_both_boxes()
        {
            var a = new Bounds(new Vec3(0, 0, 0), new Vec3(10, 10, 10));
            var b = new Bounds(new Vec3(-5, 2, 2), new Vec3(4, 20, 4));

            Bounds both = a.Union(b);

            Assert.Equal(-5, both.Min.X, 6);
            Assert.Equal(20, both.Max.Y, 6);
        }

        [Fact]
        public void Expanded_grows_each_face_by_its_own_amount()
        {
            var box = new Bounds(new Vec3(0, 0, 0), new Vec3(10, 10, 10));

            Bounds grown = box.Expanded(1, 2, 3, 4, 5, 6);

            Assert.Equal(-1, grown.Min.X, 6);
            Assert.Equal(12, grown.Max.X, 6);
            Assert.Equal(-3, grown.Min.Y, 6);
            Assert.Equal(14, grown.Max.Y, 6);
            Assert.Equal(-5, grown.Min.Z, 6);
            Assert.Equal(16, grown.Max.Z, 6);
        }

        [Fact]
        public void Vectors_within_epsilon_are_equal()
        {
            var a = new Vec3(1, 2, 3);
            var b = new Vec3(1 + 1e-12, 2, 3);

            Assert.True(a.Equals(b));
        }

        [Fact]
        public void Vectors_further_apart_than_epsilon_are_not_equal()
        {
            var a = new Vec3(1, 2, 3);
            var b = new Vec3(1.001, 2, 3);

            Assert.False(a.Equals(b));
        }
    }
}
