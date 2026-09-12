using System;
using System.Globalization;

namespace GCam.Core.Tooling
{
    /// <summary>
    /// Matches tools against what the user typed into a search box.
    /// </summary>
    /// <remarks>
    /// In Core rather than the viewmodel so it can be tested headlessly - search rules
    /// are the sort of thing that quietly stops matching what people expect.
    /// </remarks>
    public static class ToolSearch
    {
        /// <summary>
        /// True if <paramref name="tool"/> should be shown for <paramref name="query"/>.
        /// An empty query matches everything.
        /// </summary>
        /// <remarks>
        /// Every whitespace-separated term must match somewhere, so "6 drill" narrows to
        /// 6mm drills rather than returning everything that is either. Matching is
        /// case-insensitive and covers the description, the tool type, the tool number
        /// and the diameter - the four things someone actually searches by.
        /// </remarks>
        public static bool Matches(Tool tool, string query)
        {
            if (tool == null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                return true;
            }

            string haystack = Haystack(tool);

            foreach (string term in query.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (haystack.IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static string Haystack(Tool tool)
        {
            ToolGeometry geometry = tool.Geometry ?? new ToolGeometry();

            // Diameter is included both as typed ("6.35") and rounded ("6.4"), because
            // people search for the number on the label, not the stored precision.
            return string.Join(
                " ",
                tool.Name,
                tool.DisplayName,
                tool.Type.ToString(),
                DisplayName(tool.Type),
                tool.Number.ToString(CultureInfo.InvariantCulture),
                geometry.Diameter.ToString("0.###", CultureInfo.InvariantCulture),
                geometry.Diameter.ToString("0.#", CultureInfo.InvariantCulture),
                tool.Manufacturer,
                tool.ProductId,
                tool.Material);
        }

        /// <summary>
        /// Spaced-out name for a tool type - "Bull nose end mill" rather than
        /// "BullNoseEndMill". Used for display and to make search match what is on screen.
        /// </summary>
        public static string DisplayName(ToolType type)
        {
            switch (type)
            {
                case ToolType.FlatEndMill: return "Flat end mill";
                case ToolType.BallEndMill: return "Ball end mill";
                case ToolType.BullNoseEndMill: return "Bull nose end mill";
                case ToolType.Drill: return "Drill";
                case ToolType.ChamferMill: return "Chamfer mill";
                case ToolType.SpotDrill: return "Spot drill";
                case ToolType.Tap: return "Tap";
                default: return type.ToString();
            }
        }
    }
}
