using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using GCam.SolidWorks.Persistence.Interop;

namespace GCam.SolidWorks.Persistence
{
    /// <summary>
    /// Moves bytes between a COM <see cref="IStream"/> and ordinary .NET ones.
    /// </summary>
    /// <remarks>
    /// The whole reason the format lives in Core: everything above this line works in
    /// <see cref="Stream"/> and <c>byte[]</c>, and only this file knows that the other end
    /// is a COM object inside a SOLIDWORKS document.
    /// </remarks>
    internal static class ComStreams
    {
        /// <summary>How much is read or written per call.</summary>
        private const int ChunkSize = 64 * 1024;

        /// <summary>
        /// Writes a named stream into a storage, replacing whatever was there.
        /// </summary>
        public static void WriteStream(IStorage storage, string name, byte[] bytes)
        {
            IStream stream = null;

            try
            {
                storage.CreateStream(name, Stgm.CreateForWriting, 0, 0, out stream);

                if (stream == null)
                {
                    throw new IOException($"SOLIDWORKS would not create the '{name}' stream.");
                }

                // A single Write can legitimately take less than it is offered, so this
                // loops rather than trusting one call with a large buffer.
                IntPtr written = Marshal.AllocCoTaskMem(sizeof(int));
                try
                {
                    int offset = 0;
                    while (offset < bytes.Length)
                    {
                        int count = Math.Min(ChunkSize, bytes.Length - offset);
                        byte[] chunk = offset == 0 && count == bytes.Length
                            ? bytes
                            : Slice(bytes, offset, count);

                        stream.Write(chunk, count, written);

                        int actual = Marshal.ReadInt32(written);
                        if (actual <= 0)
                        {
                            throw new IOException(
                                $"Writing the '{name}' stream stopped after {offset} bytes.");
                        }

                        offset += actual;
                    }
                }
                finally
                {
                    Marshal.FreeCoTaskMem(written);
                }

                stream.Commit(0);
            }
            finally
            {
                Release(stream);
            }
        }

        /// <summary>
        /// Reads a named stream, or null when it is not there.
        /// </summary>
        /// <remarks>
        /// Absent is a normal answer, not an error: a part saved before G-CAM touched it
        /// has none of our streams, and an operation that was never generated has no
        /// toolpath stream. <c>OpenStream</c> reports that by throwing, which is why this
        /// catches rather than checks first - structured storage offers no "does this
        /// exist" that is cheaper than trying.
        /// </remarks>
        public static byte[] ReadStream(IStorage storage, string name)
        {
            IStream stream = null;

            try
            {
                storage.OpenStream(name, IntPtr.Zero, Stgm.OpenForReading, 0, out stream);
            }
            catch (COMException)
            {
                return null;
            }

            if (stream == null)
            {
                return null;
            }

            try
            {
                stream.Stat(out System.Runtime.InteropServices.ComTypes.STATSTG stat, 1 /* STATFLAG_NONAME */);

                long size = stat.cbSize;
                if (size <= 0)
                {
                    return new byte[0];
                }

                if (size > int.MaxValue)
                {
                    throw new IOException($"The '{name}' stream is too large to read.");
                }

                var bytes = new byte[size];
                IntPtr read = Marshal.AllocCoTaskMem(sizeof(int));

                try
                {
                    int offset = 0;
                    while (offset < bytes.Length)
                    {
                        int count = Math.Min(ChunkSize, bytes.Length - offset);
                        var chunk = new byte[count];

                        stream.Read(chunk, count, read);

                        int actual = Marshal.ReadInt32(read);
                        if (actual <= 0)
                        {
                            // Short of what Stat promised. Keep what arrived - the formats
                            // above both cope with truncation, and half a toolpath is more
                            // use than an exception.
                            Array.Resize(ref bytes, offset);
                            break;
                        }

                        Buffer.BlockCopy(chunk, 0, bytes, offset, actual);
                        offset += actual;
                    }
                }
                finally
                {
                    Marshal.FreeCoTaskMem(read);
                }

                return bytes;
            }
            finally
            {
                Release(stream);
            }
        }

        private static byte[] Slice(byte[] source, int offset, int count)
        {
            var slice = new byte[count];
            Buffer.BlockCopy(source, offset, slice, 0, count);
            return slice;
        }

        /// <summary>
        /// Releases a COM object we created, per the house rule - and only ones we
        /// created. These streams are ours: nothing else in SOLIDWORKS holds them.
        /// </summary>
        private static void Release(object com)
        {
            if (com != null && Marshal.IsComObject(com))
            {
                Marshal.ReleaseComObject(com);
            }
        }
    }
}
