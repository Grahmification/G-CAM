using System.IO;
using System.Linq;
using GCam.Core.Geometry.Primitives;
using GCam.Core.Model;
using GCam.Core.Persistence;
using Xunit;

namespace GCam.Core.Tests.Persistence
{
    public class ToolpathBinaryTests
    {
        private static Vec3 P(double x, double y, double z = 0) => new Vec3(x, y, z);

        private static Toolpath Sample()
        {
            return new Toolpath()
                .Add(Move.Rapid(P(0, 0, 10)))
                .Add(Move.Plunge(P(0, 0, -1.5), 200))
                .Add(Move.Cut(P(10.25, 0, -1.5), 1800))
                .Add(Move.CutArc(P(20, 10, -1.5), 1800, P(10.25, 10, -1.5), clockwise: true))
                .Add(Move.Lead(P(25, 15, -1.5), 900))
                .Add(Move.Retract(P(25, 15, 10), 1000));
        }

        [Fact]
        public void A_toolpath_survives_a_round_trip_move_for_move()
        {
            Toolpath original = Sample();

            Toolpath read = ToolpathBinary.FromBytes(ToolpathBinary.ToBytes(original));

            Assert.Equal(original.Moves.Count, read.Moves.Count);

            for (int i = 0; i < original.Moves.Count; i++)
            {
                Move a = original.Moves[i];
                Move b = read.Moves[i];

                Assert.Equal(a.Kind, b.Kind);
                Assert.Equal(a.End, b.End);
                Assert.Equal(a.Feed, b.Feed, 9);
                Assert.Equal(a.IsArc, b.IsArc);
            }
        }

        [Fact]
        public void An_arc_keeps_its_centre_direction_and_plane()
        {
            var original = new Toolpath()
                .Add(Move.Rapid(P(10, 0)))
                .Add(Move.CutArc(P(0, 10), 500, P(0.5, -0.25, 3), clockwise: true, plane: ArcPlane.YZ));

            Move arc = ToolpathBinary.FromBytes(ToolpathBinary.ToBytes(original)).Moves.Last();

            Assert.Equal(P(0.5, -0.25, 3), arc.Arc.Centre);
            Assert.True(arc.Arc.Clockwise);
            Assert.Equal(ArcPlane.YZ, arc.Arc.Plane);
        }

        [Fact]
        public void Coordinates_keep_full_precision()
        {
            // A toolpath rounded on the way to disk is a different toolpath.
            var original = new Toolpath()
                .Add(Move.Rapid(P(0, 0)))
                .Add(Move.Cut(P(1.0 / 3.0, 123456.789012345, -0.000001), 1234.5678));

            Move read = ToolpathBinary.FromBytes(ToolpathBinary.ToBytes(original)).Moves.Last();

            Assert.Equal(1.0 / 3.0, read.End.X, 15);
            Assert.Equal(123456.789012345, read.End.Y, 9);
            Assert.Equal(1234.5678, read.Feed, 12);
        }

        [Fact]
        public void An_empty_toolpath_round_trips_as_empty()
        {
            Toolpath read = ToolpathBinary.FromBytes(ToolpathBinary.ToBytes(new Toolpath()));

            Assert.NotNull(read);
            Assert.Empty(read.Moves);
        }

        [Fact]
        public void A_null_toolpath_writes_an_empty_one_rather_than_failing()
        {
            Assert.Empty(ToolpathBinary.FromBytes(ToolpathBinary.ToBytes(null)).Moves);
        }

        [Fact]
        public void Bytes_that_are_not_a_toolpath_read_as_null_rather_than_as_empty()
        {
            // Null tells the caller to leave the operation needing a regenerate. An empty
            // path would claim the operation legitimately cuts nothing.
            Assert.Null(ToolpathBinary.FromBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
            Assert.Null(ToolpathBinary.FromBytes(new byte[0]));
            Assert.Null(ToolpathBinary.FromBytes(null));
        }

        [Fact]
        public void A_stream_written_by_a_newer_build_reads_as_null()
        {
            byte[] bytes = ToolpathBinary.ToBytes(Sample());
            bytes[ToolpathBinary.Magic.Length] = 99;   // version

            Assert.Null(ToolpathBinary.FromBytes(bytes));
        }

        [Fact]
        public void A_truncated_stream_keeps_the_moves_that_survived()
        {
            // Visibly wrong beats invisibly empty: a half-drawn path says something is
            // broken, where no path looks like an operation that cuts nothing.
            byte[] bytes = ToolpathBinary.ToBytes(Sample());
            byte[] truncated = bytes.Take(bytes.Length / 2).ToArray();

            Toolpath read = ToolpathBinary.FromBytes(truncated);

            Assert.NotNull(read);
            Assert.NotEmpty(read.Moves);
            Assert.True(read.Moves.Count < Sample().Moves.Count);
        }

        [Fact]
        public void A_stream_claiming_a_negative_count_is_refused()
        {
            byte[] bytes = ToolpathBinary.ToBytes(Sample());
            bytes[ToolpathBinary.Magic.Length + 4] = 0xFF;
            bytes[ToolpathBinary.Magic.Length + 5] = 0xFF;
            bytes[ToolpathBinary.Magic.Length + 6] = 0xFF;
            bytes[ToolpathBinary.Magic.Length + 7] = 0xFF;

            Assert.Null(ToolpathBinary.FromBytes(bytes));
        }

        [Fact]
        public void Writing_leaves_the_stream_open_for_whoever_owns_it()
        {
            // The storage layer writes into a stream it got from SOLIDWORKS and has to
            // release it itself.
            var stream = new MemoryStream();

            ToolpathBinary.Write(Sample(), stream);

            Assert.True(stream.CanWrite);
            stream.Position = 0;
            Assert.NotEmpty(ToolpathBinary.Read(stream).Moves);
            Assert.True(stream.CanRead);
        }

        [Fact]
        public void Binary_is_far_smaller_than_the_moves_would_be_as_text()
        {
            // The whole reason toolpaths are not in model.xml.
            var path = new Toolpath();
            path.Add(Move.Rapid(P(0, 0, 10)));
            for (int i = 0; i < 1000; i++)
            {
                path.Add(Move.Cut(P(i * 0.1, i * 0.2, -1), 1800));
            }

            int bytes = ToolpathBinary.ToBytes(path).Length;

            Assert.True(bytes < 45 * path.Moves.Count, $"{bytes} bytes for {path.Moves.Count} moves");
        }
    }
}
