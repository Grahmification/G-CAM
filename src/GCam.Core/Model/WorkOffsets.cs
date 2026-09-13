using System.Collections.Generic;
using System.Globalization;

namespace GCam.Core.Model
{
    /// <summary>
    /// The machine work offsets a job can be posted against, G54 to G59.
    /// </summary>
    /// <remarks>
    /// Stored as a number rather than an enum so that extended offsets - G59.1 and up on
    /// Haas and Fanuc controls - can be added without changing what a job holds. The
    /// dropdown offers the six standard ones; the post decides how to emit anything
    /// beyond them.
    /// </remarks>
    public static class WorkOffsets
    {
        /// <summary>G54, and the default for a new job.</summary>
        public const int First = 1;

        /// <summary>G59.</summary>
        public const int Last = 6;

        // G54 is the first work offset G-code. Offset 1 means G54, 2 means G55, and so on.
        private const int FirstGCode = 54;

        /// <summary>The G-code word for an offset number, e.g. 1 becomes "G54".</summary>
        public static string Name(int offset)
        {
            return "G" + (FirstGCode + offset - First).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>The six standard offsets, in order, for a dropdown.</summary>
        public static IReadOnlyList<string> Names
        {
            get
            {
                var names = new List<string>();
                for (int offset = First; offset <= Last; offset++)
                {
                    names.Add(Name(offset));
                }

                return names;
            }
        }

        public static bool IsStandard(int offset) => offset >= First && offset <= Last;
    }
}
