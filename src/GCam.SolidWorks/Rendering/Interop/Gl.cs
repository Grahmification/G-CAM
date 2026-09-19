using System;
using System.Runtime.InteropServices;
using System.Security;

namespace GCam.SolidWorks.Rendering.Interop
{
    /// <summary>
    /// The OpenGL entry points G-CAM draws with, and the constants they take.
    /// </summary>
    /// <remarks>
    /// <b>Every function here is exported directly by opengl32.dll</b>, which is the
    /// reason the renderer is built on vertex arrays rather than buffer objects or
    /// shaders. Anything newer than OpenGL 1.1 has to be fetched through
    /// wglGetProcAddress against whichever context happens to be current, and inside
    /// SOLIDWORKS that context is not ours to reason about. A plain DllImport needs none
    /// of that and cannot fail halfway. See
    /// docs/decisions/0005-opengl-overlay-with-vertex-arrays.md.
    ///
    /// <b>There is no context management here on purpose.</b> G-CAM draws only from
    /// BufferSwapNotify, where SOLIDWORKS has already made its context current; the help
    /// says so in as many words, and adds that the matrices are already set up for part
    /// coordinates. Creating or making current a context of our own would be how you
    /// corrupt the host's rendering.
    ///
    /// Names drop the gl prefix - Gl.Enable, not Gl.glEnable - because the class already
    /// says it. The GL_ constants keep theirs, so they can be matched against the OpenGL
    /// specification by eye.
    ///
    /// <b>Verified</b> drawing on SOLIDWORKS 2025 SP3. See
    /// docs/solidworks-api/opengl-overlay.md for what is documented, what has been
    /// tested, and what is still assumed.
    /// </remarks>
    [SuppressUnmanagedCodeSecurity]
    internal static class Gl
    {
        private const string Library = "opengl32.dll";

        // ---- Attribute stack -------------------------------------------------
        //
        // How G-CAM keeps its hands off SOLIDWORKS' state. Push before touching
        // anything, pop after. The two stacks are separate: server state (enables,
        // colour, depth) and client state (which vertex arrays are on and where they
        // point) do not push together, and forgetting the second one leaves SOLIDWORKS
        // reading from our array.

        public const uint GL_CURRENT_BIT = 0x00000001;
        public const uint GL_POINT_BIT = 0x00000002;
        public const uint GL_LINE_BIT = 0x00000004;
        public const uint GL_POLYGON_BIT = 0x00000008;
        public const uint GL_LIGHTING_BIT = 0x00000040;
        public const uint GL_DEPTH_BUFFER_BIT = 0x00000100;
        public const uint GL_ENABLE_BIT = 0x00002000;
        public const uint GL_COLOR_BUFFER_BIT = 0x00004000;
        public const uint GL_TEXTURE_BIT = 0x00040000;

        public const uint GL_CLIENT_VERTEX_ARRAY_BIT = 0x00000002;

        // ---- Capabilities ----------------------------------------------------

        public const uint GL_POINT_SMOOTH = 0x0B10;
        public const uint GL_LINE_SMOOTH = 0x0B20;
        public const uint GL_CULL_FACE = 0x0B44;
        public const uint GL_LIGHTING = 0x0B50;
        public const uint GL_DEPTH_TEST = 0x0B71;
        public const uint GL_BLEND = 0x0BE2;
        public const uint GL_TEXTURE_2D = 0x0DE1;

        // ---- Blending --------------------------------------------------------

        public const uint GL_SRC_ALPHA = 0x0302;
        public const uint GL_ONE_MINUS_SRC_ALPHA = 0x0303;

        // ---- Primitives ------------------------------------------------------

        public const uint GL_POINTS = 0x0000;
        public const uint GL_LINES = 0x0001;
        public const uint GL_LINE_STRIP = 0x0003;
        public const uint GL_TRIANGLES = 0x0004;

        // ---- Vertex arrays ---------------------------------------------------

        public const uint GL_FLOAT = 0x1406;
        public const uint GL_VERTEX_ARRAY = 0x8074;

        // ---- Reading the view ------------------------------------------------

        public const uint GL_VIEWPORT = 0x0BA2;
        public const uint GL_MODELVIEW_MATRIX = 0x0BA6;
        public const uint GL_PROJECTION_MATRIX = 0x0BA7;

        // ---- Errors ----------------------------------------------------------

        public const uint GL_NO_ERROR = 0;

        [DllImport(Library, EntryPoint = "glPushAttrib")]
        public static extern void PushAttrib(uint mask);

        [DllImport(Library, EntryPoint = "glPopAttrib")]
        public static extern void PopAttrib();

        [DllImport(Library, EntryPoint = "glPushClientAttrib")]
        public static extern void PushClientAttrib(uint mask);

        [DllImport(Library, EntryPoint = "glPopClientAttrib")]
        public static extern void PopClientAttrib();

        [DllImport(Library, EntryPoint = "glEnable")]
        public static extern void Enable(uint capability);

        [DllImport(Library, EntryPoint = "glDisable")]
        public static extern void Disable(uint capability);

        [DllImport(Library, EntryPoint = "glBlendFunc")]
        public static extern void BlendFunc(uint source, uint destination);

        /// <param name="writable">
        /// GL_TRUE or GL_FALSE as a byte - OpenGL's GLboolean, not a Windows BOOL, so
        /// marshalling a .NET bool here would write four bytes where one is expected.
        /// </param>
        [DllImport(Library, EntryPoint = "glDepthMask")]
        public static extern void DepthMask(byte writable);

        [DllImport(Library, EntryPoint = "glColor4f")]
        public static extern void Color4f(float red, float green, float blue, float alpha);

        [DllImport(Library, EntryPoint = "glLineWidth")]
        public static extern void LineWidth(float width);

        [DllImport(Library, EntryPoint = "glPointSize")]
        public static extern void PointSize(float size);

        [DllImport(Library, EntryPoint = "glEnableClientState")]
        public static extern void EnableClientState(uint array);

        [DllImport(Library, EntryPoint = "glDisableClientState")]
        public static extern void DisableClientState(uint array);

        /// <param name="pointer">
        /// OpenGL keeps this pointer and reads through it when DrawArrays is called, not
        /// now. The array behind it must stay pinned across both calls - which is why it
        /// is an IntPtr from a GCHandle rather than a float[] the marshaller would pin
        /// only for the duration of this one call.
        /// </param>
        [DllImport(Library, EntryPoint = "glVertexPointer")]
        public static extern void VertexPointer(int size, uint type, int stride, IntPtr pointer);

        [DllImport(Library, EntryPoint = "glDrawArrays")]
        public static extern void DrawArrays(uint mode, int first, int count);

        [DllImport(Library, EntryPoint = "glGetError")]
        public static extern uint GetError();

        /// <summary>
        /// Reads back state SOLIDWORKS set - the two matrices, for working out what a
        /// pixel is worth in model units.
        /// </summary>
        /// <remarks>
        /// A read, not a write, so it is not part of what <see cref="GlState"/> has to put
        /// back. Both matrices come out column-major, which is what OpenGL has always
        /// meant by a matrix and the opposite of how they are written down.
        /// </remarks>
        [DllImport(Library, EntryPoint = "glGetDoublev")]
        public static extern void GetDoublev(uint name, [Out] double[] values);

        [DllImport(Library, EntryPoint = "glGetIntegerv")]
        public static extern void GetIntegerv(uint name, [Out] int[] values);

        /// <summary>
        /// Drains and returns the first queued error, or GL_NO_ERROR.
        /// </summary>
        /// <remarks>
        /// OpenGL queues errors rather than reporting them, so a single GetError can
        /// return one raised long before the call being investigated. Draining the queue
        /// keeps a stale error from being blamed on the next frame, and returning the
        /// first one keeps the earliest - and therefore most likely relevant - cause.
        ///
        /// The loop is bounded because a context that has gone away can report an error
        /// forever.
        /// </remarks>
        public static uint DrainErrors()
        {
            const int limit = 32;

            uint first = GL_NO_ERROR;

            for (int i = 0; i < limit; i++)
            {
                uint error = GetError();

                if (error == GL_NO_ERROR)
                {
                    break;
                }

                if (first == GL_NO_ERROR)
                {
                    first = error;
                }
            }

            return first;
        }
    }
}
