using System;
using System.Linq;
using GCam.Core.Geometry.Primitives;
using GCam.Core.Rendering;
using Xunit;

namespace GCam.Core.Tests.Rendering
{
    public class BoxMeshTests
    {
        private static readonly Bounds UnitBox =
            new Bounds(new Vec3(0, 0, 0), new Vec3(1, 2, 4));

        [Fact]
        public void Corners_are_the_eight_combinations_of_the_extremes()
        {
            Vec3[] corners = BoxMesh.Corners(UnitBox, Matrix4.Identity);

            Assert.Equal(BoxMesh.CornerCount, corners.Length);
            Assert.Equal(8, corners.Distinct().Count());

            Assert.All(corners, c =>
            {
                Assert.True(c.X == UnitBox.Min.X || c.X == UnitBox.Max.X);
                Assert.True(c.Y == UnitBox.Min.Y || c.Y == UnitBox.Max.Y);
                Assert.True(c.Z == UnitBox.Min.Z || c.Z == UnitBox.Max.Z);
            });
        }

        [Fact]
        public void Corner_order_is_the_documented_bit_pattern()
        {
            // Callers index into this - the edge and triangle lists here do - so the
            // order is part of the contract rather than an implementation detail.
            Vec3[] corners = BoxMesh.Corners(UnitBox, Matrix4.Identity);

            Assert.True(corners[0].Equals(new Vec3(0, 0, 0)));
            Assert.True(corners[1].Equals(new Vec3(1, 0, 0)));
            Assert.True(corners[2].Equals(new Vec3(0, 2, 0)));
            Assert.True(corners[4].Equals(new Vec3(0, 0, 4)));
            Assert.True(corners[7].Equals(new Vec3(1, 2, 4)));
        }

        [Fact]
        public void A_transform_moves_the_corners_with_the_box()
        {
            // What a rotated job coordinate system does: the box is no longer axis
            // aligned, and the eight corners still describe it exactly.
            Matrix4 quarterTurn = Matrix4.FromAxes(
                Vec3.Zero, new Vec3(0, 1, 0), new Vec3(-1, 0, 0), new Vec3(0, 0, 1));

            Vec3[] corners = BoxMesh.Corners(UnitBox, quarterTurn);

            Assert.True(corners[0].Equals(new Vec3(0, 0, 0)));
            Assert.True(corners[1].Equals(new Vec3(0, 1, 0)));
            Assert.True(corners[2].Equals(new Vec3(-2, 0, 0)));
        }

        [Fact]
        public void Triangles_fill_every_face()
        {
            Vec3[] corners = BoxMesh.Corners(UnitBox, Matrix4.Identity);
            Vec3[] triangles = BoxMesh.Triangles(corners);

            // Six faces, two triangles each, three vertices each.
            Assert.Equal(36, triangles.Length);
        }

        [Fact]
        public void Triangles_are_wound_outwards()
        {
            // Nothing turns culling or lighting on today, so a reversed winding would be
            // invisible now and wrong later - exactly the kind of thing worth pinning
            // while it is cheap.
            Vec3[] corners = BoxMesh.Corners(UnitBox, Matrix4.Identity);
            Vec3[] triangles = BoxMesh.Triangles(corners);

            Vec3 centre = UnitBox.Centre;

            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vec3 normal = Cross(triangles[i + 1] - triangles[i], triangles[i + 2] - triangles[i]);
                Vec3 outward = triangles[i] - centre;

                Assert.True(
                    Dot(normal, outward) > 0,
                    $"The triangle at vertex {i} faces inwards.");
            }
        }

        [Fact]
        public void Edges_are_the_twelve_edges_of_the_box()
        {
            Vec3[] corners = BoxMesh.Corners(UnitBox, Matrix4.Identity);
            Vec3[] edges = BoxMesh.Edges(corners);

            Assert.Equal(24, edges.Length);

            // Every segment runs along exactly one axis - a diagonal would mean two
            // corners that are not neighbours were joined.
            for (int i = 0; i < edges.Length; i += 2)
            {
                Vec3 along = edges[i + 1] - edges[i];

                int axes = (Math.Abs(along.X) > 0 ? 1 : 0)
                           + (Math.Abs(along.Y) > 0 ? 1 : 0)
                           + (Math.Abs(along.Z) > 0 ? 1 : 0);

                Assert.Equal(1, axes);
            }
        }

        [Fact]
        public void The_wrong_number_of_corners_is_rejected()
        {
            Assert.Throws<ArgumentException>(() => BoxMesh.Triangles(new Vec3[3]));
            Assert.Throws<ArgumentNullException>(() => BoxMesh.Edges(null));
        }

        private static Vec3 Cross(Vec3 a, Vec3 b) => new Vec3(
            (a.Y * b.Z) - (a.Z * b.Y),
            (a.Z * b.X) - (a.X * b.Z),
            (a.X * b.Y) - (a.Y * b.X));

        private static double Dot(Vec3 a, Vec3 b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);
    }
}
