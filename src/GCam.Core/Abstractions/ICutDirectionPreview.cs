using GCam.Core.Model;

namespace GCam.Core.Abstractions
{
    /// <summary>
    /// Shows, in the 3D view, which side of its contours an operation will cut and which
    /// way round.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="IJobPreview"/> rather than another flag on a
    /// <see cref="Rendering.PreviewSelection"/>, because it answers to something else. A
    /// preview selection is what the tree has selected; this is what one property page is
    /// editing, and it is up only while that page is. Sharing the contract would mean the
    /// tree's selection and the page's edits taking turns to state the whole picture, and
    /// whichever spoke last would win.
    ///
    /// Implementations are entry points and must swallow their exceptions: this is called
    /// from PropertyManager page callbacks, where a dialog would interrupt the edit that
    /// provoked it, and an arrow that cannot be worked out is worth a log line at most.
    /// </remarks>
    public interface ICutDirectionPreview
    {
        /// <summary>
        /// Shows the arrows for an operation's current contours, replacing whatever was
        /// shown before. Anything that cannot be worked out is left out.
        /// </summary>
        void Show(Job job, Operation operation);

        /// <summary>Takes the arrows off the screen.</summary>
        void Clear();
    }
}
