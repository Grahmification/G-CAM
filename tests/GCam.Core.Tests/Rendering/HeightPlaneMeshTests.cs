using System.Collections.Generic;
using System.Linq;
using GCam.Core.Geometry.Primitives;
using GCam.Core.Rendering;
using Xunit;

namespace GCam.Core.Tests.Rendering
{
    public class HeightPlaneMeshTests
    {
        private static readonly Bounds Part =
            new Bounds(new Vec3(0, 0, 0), new Vec3(100, 60, 20));

        private static readonly RenderColour Yellow = new RenderColour(1, 1, 0);

        private static IReadOnlyList<RenderBatch> Build(bool filled) =>
            HeightPlaneMesh.Build(Part, 30, Matrix4.Identity, Yellow, filled).ToList();

        [Fact]
        public void The_outline_is_there_either_way_and_the_fill_only_when_asked()
        {
            Assert.Single(Build(filled: false));
            Assert.Equal(2, Build(filled: true).Count);
        }

        [Fact]
        public void The_outline_closes_itself_and_sits_at_the_height()
        {
            RenderBatch outline = Build(filled: false).Single();

            Assert.Equal(PrimitiveKind.LineStrip, outline.Kind);

            // Four corners and the first one again - a line strip does not close itself.
            Assert.Equal(5, outline.Vertices.Count);
            Assert.Equal(outline.Vertices[0], outline.Vertices[4]);
            Assert.All(outline.Vertices, v => Assert.Equal(30, v.Z, 6));
        }

        [Fact]
        public void The_plane_reaches_past_the_part_on_every_side()
        {
            RenderBatch outline = Build(filled: false).Single();

            Assert.True(outline.Vertices.Min(v => v.X) < Part.Min.X, "not past -X");
            Assert.True(outline.Vertices.Max(v => v.X) > Part.Max.X, "not past +X");
            Assert.True(outline.Vertices.Min(v => v.Y) < Part.Min.Y, "not past -Y");
            Assert.True(outline.Vertices.Max(v => v.Y) > Part.Max.Y, "not past +Y");
        }

        [Fact]
        public void The_fill_is_transparent_and_the_outline_is_not()
        {
            IReadOnlyList<RenderBatch> batches = Build(filled: true);

            RenderBatch fill = batches.Single(b => b.Kind == PrimitiveKind.Triangles);
            RenderBatch outline = batches.Single(b => b.Kind == PrimitiveKind.LineStrip);

            Assert.True(fill.Colour.IsTransparent);
            Assert.False(outline.Colour.IsTransparent);

            // The outline has to be readable behind the part; the fill is worth more for
            // being occluded by it. See the remarks on HeightPlaneMesh.
            Assert.False(fill.AlwaysOnTop);
            Assert.True(outline.AlwaysOnTop);
        }
    }
}
