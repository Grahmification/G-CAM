using System.IO;

namespace GCam.Core.Tooling.Import
{
    /// <summary>
    /// Reads a tool library from a file in some format.
    /// </summary>
    /// <remarks>
    /// The native format is <see cref="GcamXmlLibraryReader"/>. This interface exists so
    /// importers for Fusion/HSM or ISO 13399 libraries can be added without the domain
    /// model knowing anything about them - shops already own tool data, and re-typing
    /// it is the fastest way to make a CAM tool unusable.
    /// </remarks>
    public interface IToolLibraryReader
    {
        /// <summary>Short name of the format, for file dialogs and error messages.</summary>
        string FormatName { get; }

        /// <summary>File extension including the dot, e.g. ".gcamtools".</summary>
        string FileExtension { get; }

        /// <summary>
        /// Reads a library, along with warnings for anything that could not be read.
        /// </summary>
        /// <remarks>
        /// Throws <see cref="Diagnostics.GCamUserException"/> when the whole file is
        /// unusable - that is a user-facing problem, not a defect. A single unreadable
        /// tool belongs in the warnings instead, so one bad entry cannot lose a library.
        /// </remarks>
        ToolLibraryReadResult Read(Stream stream, string sourcePath);
    }
}
