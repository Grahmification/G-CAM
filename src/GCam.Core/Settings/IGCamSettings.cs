using System.Collections.Generic;

namespace GCam.Core.Settings
{
    /// <summary>
    /// User preferences that outlive a session.
    /// </summary>
    /// <remarks>
    /// An interface so the UI can be exercised with an in-memory implementation, and so
    /// the storage location is a decision made once in the composition root rather than
    /// assumed by whatever needs a setting.
    /// </remarks>
    public interface IGCamSettings
    {
        /// <summary>
        /// Folders the tool library browser watches. Order is the user's, so the list is
        /// mutable rather than a set.
        /// </summary>
        IList<string> ToolLibraryFolders { get; }

        /// <summary>Writes the settings out. Never throws; failure to save is logged.</summary>
        void Save();
    }
}
