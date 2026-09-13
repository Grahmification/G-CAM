using GCam.Core.Rendering;

namespace GCam.Core.Abstractions
{
    /// <summary>
    /// Somewhere G-CAM's own graphics appear - one document's 3D view.
    /// </summary>
    /// <remarks>
    /// Core describes what to draw in a <see cref="RenderScene"/>; an implementation in
    /// GCam.SolidWorks puts it on screen. The same arrangement as
    /// <see cref="IJobEditor"/> and for the same reason: the thing being drawn is decided
    /// by code that must not reference SOLIDWORKS, and the drawing is pure COM and
    /// OpenGL.
    ///
    /// The usual way to use one is to change <see cref="Scene"/> and stop. The scene
    /// raises its own change notification and an implementation is expected to repaint
    /// itself from that. <see cref="Invalidate"/> is for the rarer case where nothing in
    /// the scene changed but the picture did - a display mode switch, a new window on the
    /// same document.
    /// </remarks>
    public interface IViewportRenderer
    {
        /// <summary>What this view draws. Change it to change the picture.</summary>
        RenderScene Scene { get; }

        /// <summary>Asks the view to repaint now.</summary>
        void Invalidate();
    }
}
