using System;
using GCam.Core.Geometry.Primitives;
using GCam.Core.Rendering;
using Xunit;

namespace GCam.Core.Tests.Rendering
{
    public class ConeMeshTests
    {
        private static readonly Vec3 BaseCentre = new Vec3(0, 0, 0);
        private static readonly Vec3 Apex = new Vec3(0, 0, 10);

        private const double Radius = 2.0;

        [Fact]
        public void A_cone_is_a_side_and_a_cap()
        {
            const int segments = 8;

            Vec3[] triangles = ConeMesh.Triangles(BaseCentre, Apex, Radius, segments);

            // One side triangle and one cap triangle per segment, three vertices each.
            Assert.Equal(segments * 2 * 3, triangles.Length);
        }

        [Fact]
        public void The_cone_is_capped()
        {
            // An uncapped arrowhead is hollow and shows its inside the moment it is
            // viewed from behind, which is the sort of thing that only turns up in a
            // screenshot.
            Vec3[] triangles = ConeMesh.Triangles(BaseCentre, Apex, Radius, 8);

            int atBaseCentre = 0;

            foreach (Vec3 vertex in triangles)
            {
                if (vertex.Equals(BaseCentre))
                {
                    atBaseCentre++;
                }
            }

            Assert.Equal(8, atBaseCentre);
        }

        [Fact]
        public void Every_base_vertex_sits_on_the_circle()
        {
            Vec3[] triangles = ConeMesh.Triangles(BaseCentre, Apex, Radius, 12);

            foreach (Vec3 vertex in triangles)
            {
                if (vertex.Equals(Apex) || vertex.Equals(BaseCentre))
                {
                    continue;
                }

                Assert.Equal(0, vertex.Z, 6);
                Assert.Equal(Radius, new Vec3(vertex.X, vertex.Y, 0).Length, 6);
            }
        }

        [Fact]
        public void Triangles_are_wound_outwards()
        {
            // Same reasoning as the box: nothing culls today, so a reversed winding is
            // invisible now and wrong later.
            Vec3[] triangles = ConeMesh.Triangles(BaseCentre, Apex, Radius, 12);

            // The centroid of a cone is a quarter of the way up from the base.
            var inside = new Vec3(0, 0, 2.5);

            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vec3 edge1 = triangles[i + 1] - triangles[i];
                Vec3 edge2 = triangles[i + 2] - triangles[i];

                Vec3 normal = edge1.Cross(edge2);
                Vec3 outward = triangles[i] - inside;

                Assert.True(
                    normal.Dot(outward) > 0,
                    $"The triangle at vertex {i} faces inwards.");
            }
        }

        [Fact]
        public void The_cone_points_whichever_way_it_is_asked_to()
        {
            // The arrows of a triad point along three different axes, so the basis this
            // builds internally has to work for any direction rather than only for Z.
            var sideways = new Vec3(7, 0, 0);

            Vec3[] triangles = ConeMesh.Triangles(BaseCentre, sideways, Radius, 8);

            foreach (Vec3 vertex in triangles)
            {
                if (vertex.Equals(sideways) || vertex.Equals(BaseCentre))
                {
                    continue;
                }

                // Base ring: on the YZ plane through the origin, at the given radius.
                Assert.Equal(0, vertex.X, 6);
                Assert.Equal(Radius, new Vec3(0, vertex.Y, vertex.Z).Length, 6);
            }
        }

        [Fact]
        public void A_cone_needs_three_sides_a_radius_and_somewhere_to_point()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => ConeMesh.Triangles(BaseCentre, Apex, Radius, 2));

            Assert.Throws<ArgumentOutOfRangeException>(
                () => ConeMesh.Triangles(BaseCentre, Apex, 0));

            Assert.Throws<ArgumentException>(
                () => ConeMesh.Triangles(BaseCentre, BaseCentre, Radius));
        }
    }
}
