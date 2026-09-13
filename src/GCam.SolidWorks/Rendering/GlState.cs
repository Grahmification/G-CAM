using System;
using GCam.SolidWorks.Rendering.Interop;

namespace GCam.SolidWorks.Rendering
{
    /// <summary>
    /// Saves the OpenGL state G-CAM is about to change and puts it back afterwards.
    /// </summary>
    /// <remarks>
    /// <b>SOLIDWORKS owns the context; we are a guest in it.</b> Anything left enabled,
    /// any colour left set, any vertex array left pointing at a managed buffer that has
    /// since moved, becomes a SOLIDWORKS rendering bug that looks nothing like an add-in
    /// problem - and it will be reported as a SOLIDWORKS problem, because that is what it
    /// looks like. docs/architecture.md names this as a rule; this class is the rule made
    /// mechanical, so no drawing code has to remember it.
    ///
    /// Both stacks are pushed. Server state (enables, colour, depth mask, line width) and
    /// client state (which vertex arrays are enabled and where they point) are separate
    /// in OpenGL, and pushing only the first is the easy mistake: the array pointer
    /// survives the pop and SOLIDWORKS then draws through a pointer into our buffer.
    ///
    /// Use it and nothing else:
    /// <code>
    /// using (new GlState())
    /// {
    ///     // change whatever you like
    /// }
    /// </code>
    /// </remarks>
    internal sealed class GlState : IDisposable
    {
        /// <summary>
        /// What gets saved. Named individually rather than using GL_ALL_ATTRIB_BITS -
        /// pushing everything copies state we never touch, on every frame, and hides
        /// which pieces this code actually cares about.
        /// </summary>
        private const uint SavedAttributes =
            Gl.GL_ENABLE_BIT          // blending, depth test, lighting, culling, texturing
            | Gl.GL_COLOR_BUFFER_BIT  // blend function
            | Gl.GL_DEPTH_BUFFER_BIT  // the depth mask
            | Gl.GL_CURRENT_BIT       // the current colour
            | Gl.GL_LINE_BIT          // line width and smoothing
            | Gl.GL_POINT_BIT         // point size and smoothing
            | Gl.GL_POLYGON_BIT       // face culling and fill mode
            | Gl.GL_LIGHTING_BIT      // material state, disturbed by colour under lighting
            | Gl.GL_TEXTURE_BIT;      // texture binding and environment

        private bool _restored;

        public GlState()
        {
            Gl.PushAttrib(SavedAttributes);
            Gl.PushClientAttrib(Gl.GL_CLIENT_VERTEX_ARRAY_BIT);
        }

        /// <summary>
        /// Pops both stacks, in the reverse of the order they were pushed.
        /// </summary>
        /// <remarks>
        /// Guarded against running twice. A double pop does not throw - it unwinds a
        /// level of SOLIDWORKS' own state instead, which is the most confusing possible
        /// outcome and would not be traced back here.
        /// </remarks>
        public void Dispose()
        {
            if (_restored)
            {
                return;
            }

            _restored = true;

            Gl.PopClientAttrib();
            Gl.PopAttrib();
        }
    }
}
