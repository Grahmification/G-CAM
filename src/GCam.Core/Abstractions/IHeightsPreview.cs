using GCam.Core.Model;
using GCam.Core.Model.Heights;

namespace GCam.Core.Abstractions
{
    /// <summary>
    /// Shows an operation's machining heights in the 3D view, as a plane each.
    /// </summary>
    /// <remarks>
    /// Its own contract for the same reason as <see cref="ICutDirectionPreview"/>: this is
    /// what one tab of one property page is editing, not what the tree has selected.
    ///
    /// Implementations are entry points and must swallow their exceptions - this is called
    /// from PropertyManager page callbacks, including focus changes, where a dialog would
    /// land on top of the edit that caused it.
    /// </remarks>
    public interface IHeightsPreview
    {
        /// <summary>
        /// Shows a plane for every height that can be resolved, replacing whatever was
        /// shown before.
        /// </summary>
        /// <param name="filled">
        /// The one being edited, which is drawn filled as well as outlined. Null fills
        /// none of them.
        /// </param>
        void Show(Job job, Operation operation, HeightKind? filled);

        /// <summary>Takes the planes off the screen.</summary>
        void Clear();
    }
}
