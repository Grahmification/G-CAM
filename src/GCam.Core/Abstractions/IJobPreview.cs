using GCam.Core.Model;

namespace GCam.Core.Abstractions
{
    /// <summary>
    /// Shows a job in the 3D view - today its stock box, later its toolpaths.
    /// </summary>
    /// <remarks>
    /// Declared in Core for the same reason as <see cref="IJobEditor"/>: the job tree is
    /// WPF in GCam.UI and the property page is COM in GCam.SolidWorks, and neither can
    /// see the other. Both state the same intent - "this is the job the user is looking
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
        /// Shows this job. Null clears the preview, which is what a deselection, a
        /// cancelled page or a deleted job all amount to.
        /// </summary>
        void ShowJob(Job job);
    }
}
