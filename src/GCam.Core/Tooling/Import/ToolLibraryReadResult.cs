using System.Collections.Generic;

namespace GCam.Core.Tooling.Import
{
    /// <summary>
    /// What came out of reading a library file, and what did not.
    /// </summary>
    /// <remarks>
    /// Warnings exist because a partial import must never be silent. A library that
    /// arrives with nine of eleven tools is the kind of thing noticed at the machine,
    /// so anything skipped is named here and surfaced to the user.
    /// </remarks>
    public sealed class ToolLibraryReadResult
    {
        public ToolLibraryReadResult(ToolLibrary library, IReadOnlyList<string> warnings)
        {
            Library = library;
            Warnings = warnings ?? new List<string>();
        }

        public ToolLibrary Library { get; }

        /// <summary>Tools that could not be read, and why. One line each.</summary>
        public IReadOnlyList<string> Warnings { get; }
    }
}
