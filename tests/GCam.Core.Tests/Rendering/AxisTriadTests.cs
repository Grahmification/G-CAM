using System;
using System.Collections.Generic;
using System.Linq;
using GCam.Core.Geometry.Primitives;
using GCam.Core.Rendering;
using Xunit;

namespace GCam.Core.Tests.Rendering
{
    public class AxisTriadTests
    {
        private const double Length = 10.0;

        [Fact]
        public void A_triad_is_a_shaft_and_a_head_per_axis()
        {
            IReadOnlyList<RenderBatch> batches = AxisTriad.Build(Matrix4.Identity, Length);

            Assert.Equal(6, batches.Count);
            Assert.Equal(3, batches.Count(b => b.Kind == PrimitiveKind.Lines));
            Assert.Equal(3, batches.Count(b => b.Kind == PrimitiveKind.Triangles));
        }

        [Fact]
        public void Red_is_X_green_is_Y_blue_is_Z()
        {
            // The convention every CAD package shares. A triad in G-CAM's own colours
            // would be a triad nobody could read, so this is worth pinning.
            IReadOnlyList<RenderBatch> batches = AxisTriad.Build(Matrix4.Identity, Length);

            Assert.True(AxisTriad.XAxis.Red > AxisTriad.XAxis.Green);
            Assert.True(AxisTriad.XAxis.Red > AxisTriad.XAxis.Blue);

            Assert.True(AxisTriad.YAxis.Green > AxisTriad.YAxis.Red);
            Assert.True(AxisTriad.YAxis.Green > AxisTriad.YAxis.Blue);

            Assert.True(AxisTriad.ZAxis.Blue > AxisTriad.ZAxis.Red);
            Assert.True(AxisTriad.ZAxis.Blue > AxisTriad.ZAxis.Green);

            Assert.Equal(AxisTriad.XAxis, ShaftAlong(batches, new Vec3(1, 0, 0)).Colour);
            Assert.Equal(AxisTriad.YAxis, ShaftAlong(batches, new Vec3(0, 1, 0)).Colour);
            Assert.Equal(AxisTriad.ZAxis, ShaftAlong(batches, new Vec3(0, 0, 1)).Colour);
        }

        [Fact]
        public void Every_arrow_starts_at_the_origin_and_reaches_the_full_length()
        {
            IReadOnlyList<RenderBatch> batches = AxisTriad.Build(Matrix4.Identity, Length);

            foreach (RenderBatch shaft in batches.Where(b => b.Kind == PrimitiveKind.Lines))
            {
                Assert.True(shaft.Vertices[0].Equals(Vec3.Zero));

                // The shaft stops short so the head can finish the arrow, but not by much.
                double reach = shaft.Vertices[1].Length;
                Assert.InRange(reach, Length * 0.5, Length);
            }

            // The tips - the furthest any vertex gets - are at the full length.
            double furthest = batches.SelectMany(b => b.Vertices).Max(v => v.Length);
            Assert.Equal(Length, furthest, 6);
        }

        [Fact]
        public void A_triad_follows_the_frame_it_is_given()
        {
            // The case the whole coordinate system pipeline exists for: a job on a
            // rotated, offset coordinate system gets a triad that is rotated and offset
            // with it.
            var origin = new Vec3(100, 200, 300);

            Matrix4 rotated = Matrix4.FromAxes(
                origin,
                new Vec3(0, 1, 0),    // X points along part Y
                new Vec3(-1, 0, 0),   // Y points along part -X
                new Vec3(0, 0, 1));

            IReadOnlyList<RenderBatch> batches = AxisTriad.Build(rotated, Length);

            foreach (RenderBatch shaft in batches.Where(b => b.Kind == PrimitiveKind.Lines))
            {
                Assert.True(shaft.Vertices[0].Equals(origin));
            }

            RenderBatch x = ShaftFrom(batches, origin, AxisTriad.XAxis);
            Assert.True((x.Vertices[1] - origin).Normalised().Equals(new Vec3(0, 1, 0)));
        }

        [Fact]
        public void A_scaled_frame_still_gives_arrows_of_the_length_asked_for()
        {
            // The axes come out of a SOLIDWORKS transform, which carries a scale factor.
            // Arrow length is a length, not a multiple of whatever the frame happens to
            // be scaled by.
            Matrix4 doubled = Matrix4.FromAxes(
                Vec3.Zero, new Vec3(2, 0, 0), new Vec3(0, 2, 0), new Vec3(0, 0, 2));

            IReadOnlyList<RenderBatch> batches = AxisTriad.Build(doubled, Length);

            double furthest = batches.SelectMany(b => b.Vertices).Max(v => v.Length);

            Assert.Equal(Length, furthest, 6);
        }

        [Fact]
        public void The_whole_triad_is_drawn_on_top()
        {
            // An origin buried in the stock is exactly the one worth seeing.
            IReadOnlyList<RenderBatch> batches = AxisTriad.Build(Matrix4.Identity, Length);

            Assert.All(batches, b => Assert.True(b.AlwaysOnTop));
        }

        [Fact]
        public void A_triad_needs_a_length()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => AxisTriad.Build(Matrix4.Identity, 0));
        }

        private static RenderBatch ShaftAlong(IEnumerable<RenderBatch> batches, Vec3 direction) =>
            batches.First(b =>
                b.Kind == PrimitiveKind.Lines
                && b.Vertices[1].Normalised().Equals(direction));

        private static RenderBatch ShaftFrom(
            IEnumerable<RenderBatch> batches, Vec3 origin, RenderColour colour) =>
            batches.First(b =>
                b.Kind == PrimitiveKind.Lines
                && b.Colour.Equals(colour)
                && b.Vertices[0].Equals(origin));
    }
}
