using GCam.Core.Rendering;

namespace GCam.Core.Abstractions
{
    /// <summary>
    /// Draws what the user is looking at in the 3D view - a job's stock and origin, an
    /// operation's toolpath.
    /// </summary>
    /// <remarks>
    /// Declared in Core for the same reason as <see cref="IJobEditor"/>: the job tree is
    /// WPF in GCam.UI and the property page is COM in GCam.SolidWorks, and neither can
    /// see the other. Both state the same intent - "this is what the user is looking
    /// at" - and GCam.SolidWorks answers it by building a scene.
    ///
    /// Implementations are called from WPF event handlers and PropertyManager page
    /// callbacks, so they are entry points in the sense of docs/error-handling.md and
    /// must not let exceptions escape. A preview that cannot be computed is a log line,
    /// never a dialog: it is decoration, and interrupting someone mid-edit over
    /// decoration is worse than showing nothing.
    /// </remarks>
    public interface IJobPreview
    {
        /// <summary>
        /// Shows exactly this selection and nothing else.
        /// </summary>
        /// <remarks>
        /// Each call states the whole picture rather than adding to it, so
        /// <see cref="PreviewSelection.Empty"/> is what a deselection, a cancelled page and
        /// a deleted job all amount to. Null means the same.
        /// </remarks>
        void Show(PreviewSelection selection);
    }
}
