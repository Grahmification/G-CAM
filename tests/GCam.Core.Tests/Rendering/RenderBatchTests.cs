using System;
using System.Collections.Generic;
using System.Linq;
using GCam.Core.Geometry.Primitives;
using GCam.Core.Rendering;
using Xunit;

namespace GCam.Core.Tests.Rendering
{
    public class RenderBatchTests
    {
        private static readonly RenderColour Opaque = new RenderColour(1, 1, 0);

        [Fact]
        public void A_batch_copies_a_lazy_sequence_rather_than_holding_it()
        {
            // A batch is meant to be immutable, and a deferred LINQ query is anything
            // but - the renderer would re-enumerate it, possibly against a collection
            // that had since changed.
            var points = new List<Vec3> { Vec3.Zero, new Vec3(1, 0, 0), new Vec3(0, 1, 0) };

            var batch = new RenderBatch(PrimitiveKind.Triangles, points.Select(p => p), Opaque);

            points.Clear();

            Assert.Equal(3, batch.Vertices.Count);
        }

        [Theory]
        [InlineData(PrimitiveKind.Triangles, 2, true)]
        [InlineData(PrimitiveKind.Triangles, 3, false)]
        [InlineData(PrimitiveKind.Lines, 1, true)]
        [InlineData(PrimitiveKind.Lines, 2, false)]
        [InlineData(PrimitiveKind.LineStrip, 1, true)]
        [InlineData(PrimitiveKind.Points, 0, true)]
        [InlineData(PrimitiveKind.Points, 1, false)]
        public void Too_few_vertices_to_form_a_primitive_counts_as_empty(
            PrimitiveKind kind, int vertexCount, bool expectedEmpty)
        {
            var batch = new RenderBatch(kind, new Vec3[vertexCount], Opaque);

            Assert.Equal(expectedEmpty, batch.IsEmpty);
        }

        [Fact]
        public void A_batch_needs_vertices()
        {
            Assert.Throws<ArgumentNullException>(
                () => new RenderBatch(PrimitiveKind.Lines, null, Opaque));
        }

        [Fact]
        public void Alpha_below_one_marks_a_colour_transparent()
        {
            // This is what decides drawing order and whether the depth buffer is written,
            // so it is worth pinning rather than leaving to a float comparison nobody
            // looks at.
            Assert.False(new RenderColour(1, 1, 0).IsTransparent);
            Assert.False(new RenderColour(1, 1, 0, 1.0).IsTransparent);
            Assert.True(new RenderColour(1, 1, 0, 0.99).IsTransparent);
            Assert.True(new RenderColour(1, 1, 0, 0).IsTransparent);
        }

        [Fact]
        public void Colour_channels_are_clamped_rather_than_wrapped()
        {
            var clamped = new RenderColour(2.0, -1.0, 0.5, 7.0);

            Assert.Equal(1.0, clamped.Red, 6);
            Assert.Equal(0.0, clamped.Green, 6);
            Assert.Equal(0.5, clamped.Blue, 6);
            Assert.Equal(1.0, clamped.Alpha, 6);
        }

        [Fact]
        public void WithAlpha_keeps_the_colour_and_changes_only_the_transparency()
        {
            var faded = new RenderColour(0.2, 0.4, 0.6).WithAlpha(0.25);

            Assert.Equal(0.2, faded.Red, 6);
            Assert.Equal(0.4, faded.Green, 6);
            Assert.Equal(0.6, faded.Blue, 6);
            Assert.Equal(0.25, faded.Alpha, 6);
        }
    }
}
