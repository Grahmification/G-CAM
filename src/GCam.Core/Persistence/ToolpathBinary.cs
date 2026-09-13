using System;
using System.IO;
using System.Text;
using GCam.Core.Geometry.Primitives;
using GCam.Core.Model;

namespace GCam.Core.Persistence
{
    /// <summary>
    /// Reads and writes a toolpath as bytes, for its own stream inside the part.
    /// </summary>
    /// <remarks>
    /// **Binary because toolpaths are big.** A few thousand moves as XML adds megabytes to
    /// every part file and to every save; the same moves here are about 40 bytes each. The
    /// model stays XML, where being readable when something goes wrong is worth far more
    /// than the space - see docs/decisions/0009-persist-toolpaths-in-the-document.md.
    ///
    /// **A toolpath is always disposable.** Anything unreadable here costs a regeneration,
    /// never the operation's parameters, which is why the two live in separate streams. So
    /// reading is permissive about the end of a stream - a truncated file yields the moves
    /// that survived - but strict about the header, since a wrong magic number means these
    /// are not toolpath bytes at all.
    /// </remarks>
    public static class ToolpathBinary
    {
        /// <summary>"GTP1" - enough to know these bytes are not something else.</summary>
        public static readonly byte[] Magic = Encoding.ASCII.GetBytes("GTP1");

        public const int CurrentVersion = 1;

        public static void Write(Toolpath path, Stream stream)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            path = path ?? new Toolpath();

            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(Magic);
                writer.Write(CurrentVersion);
                writer.Write(path.Moves.Count);

                foreach (Move move in path.Moves)
                {
                    writer.Write((byte)move.Kind);
                    writer.Write(move.Arc != null);

                    WriteVec(writer, move.End);
                    writer.Write(move.Feed);

                    if (move.Arc != null)
                    {
                        WriteVec(writer, move.Arc.Centre);
                        writer.Write(move.Arc.Clockwise);
                        writer.Write((int)move.Arc.Plane);
                    }
                }
            }
        }

        public static byte[] ToBytes(Toolpath path)
        {
            using (var buffer = new MemoryStream())
            {
                Write(path, buffer);
                return buffer.ToArray();
            }
        }

        /// <summary>
        /// Reads a toolpath back.
        /// </summary>
        /// <returns>
        /// The moves that could be read. Null when the stream is not a toolpath at all, so
        /// the caller can leave the operation needing a regenerate rather than showing an
        /// empty path as though it were the real one.
        /// </returns>
        public static Toolpath Read(Stream stream)
        {
            if (stream == null)
            {
                return null;
            }

            var path = new Toolpath();

            try
            {
                using (var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true))
                {
                    byte[] magic = reader.ReadBytes(Magic.Length);
                    if (!SameBytes(magic, Magic))
                    {
                        return null;
                    }

                    int version = reader.ReadInt32();
                    if (version > CurrentVersion)
                    {
                        return null;
                    }

                    int count = reader.ReadInt32();
                    if (count < 0)
                    {
                        return null;
                    }

                    for (int i = 0; i < count; i++)
                    {
                        var kind = (MoveKind)reader.ReadByte();
                        bool isArc = reader.ReadBoolean();

                        Vec3 end = ReadVec(reader);
                        double feed = reader.ReadDouble();

                        ArcData arc = null;
                        if (isArc)
                        {
                            Vec3 centre = ReadVec(reader);
                            bool clockwise = reader.ReadBoolean();
                            var plane = (ArcPlane)reader.ReadInt32();
                            arc = new ArcData(centre, clockwise, plane);
                        }

                        path.Add(new Move(kind, end, feed, arc));
                    }
                }
            }
            catch (EndOfStreamException)
            {
                // Truncated. Keep what was read: a partial path is visibly wrong, where an
                // empty one looks like an operation that legitimately cuts nothing.
                return path;
            }

            return path;
        }

        public static Toolpath FromBytes(byte[] bytes)
        {
            if (bytes == null)
            {
                return null;
            }

            using (var buffer = new MemoryStream(bytes, writable: false))
            {
                return Read(buffer);
            }
        }

        private static void WriteVec(BinaryWriter writer, Vec3 v)
        {
            writer.Write(v.X);
            writer.Write(v.Y);
            writer.Write(v.Z);
        }

        private static Vec3 ReadVec(BinaryReader reader) =>
            new Vec3(reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble());

        private static bool SameBytes(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
