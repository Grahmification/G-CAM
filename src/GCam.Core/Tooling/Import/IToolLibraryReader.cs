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
        /// Reads a library. Throws <see cref="Diagnostics.GCamUserException"/> for a
        /// malformed or unsupported file - that is a user-facing problem, not a defect.
        /// </summary>
        ToolLibrary Read(Stream stream, string sourcePath);
    }
}
