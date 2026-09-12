using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using GCam.Core.Tooling;

namespace GCam.UI.Views
{
    /// <summary>
    /// Turns a <see cref="ToolType"/> into a small silhouette for the tool list.
    /// </summary>
    /// <remarks>
    /// Vector geometry rather than bitmaps: crisp at any DPI and any size, recolourable,
    /// and nothing to add to the build or the deployed folder.
    ///
    /// The shapes are stylised by TYPE, not drawn from a particular tool's dimensions.
    /// A 16 pixel icon has no room to show a corner radius, and the point of the column
    /// is to tell a drill from an end mill at a glance - CutterProfile already draws the
    /// real thing in the preview pane.
    ///
    /// Drawn in a nominal 10 wide by 16 tall box with the tip at the bottom; the Path
    /// stretches uniformly to whatever size the template asks for.
    /// </remarks>
    public sealed class ToolTypeIconConverter : IValueConverter
    {
        private static readonly Dictionary<ToolType, string> Paths = new Dictionary<ToolType, string>
        {
            // Plain cylinder, square end.
            { ToolType.FlatEndMill, "M 2,1 L 8,1 L 8,15 L 2,15 Z" },

            // Cylinder with a full hemispherical end.
            { ToolType.BallEndMill, "M 2,1 L 8,1 L 8,12 A 3,3 0 0 1 2,12 Z" },

            // Square end with the corners merely broken - deliberately a much smaller
            // radius than the ball nose, or the two are indistinguishable at 16 pixels.
            { ToolType.BullNoseEndMill, "M 2,1 L 8,1 L 8,13.5 A 1.5,1.5 0 0 1 6.5,15 L 3.5,15 A 1.5,1.5 0 0 1 2,13.5 Z" },

            // Narrow body, long shallow point.
            { ToolType.Drill, "M 3,1 L 7,1 L 7,11 L 5,15 L 3,11 Z" },

            // Stubbier body, steeper point - how a spot drill differs from a drill.
            { ToolType.SpotDrill, "M 2,1 L 8,1 L 8,9 L 5,15 L 2,9 Z" },

            // Narrow shank above a wide cone.
            { ToolType.ChamferMill, "M 3.5,1 L 6.5,1 L 6.5,7 L 9,7 L 5,15 L 1,7 L 3.5,7 Z" },

            // Jagged flanks stand in for the thread form.
            {
                ToolType.Tap,
                "M 3,1 L 7,1 L 7,4 L 8,5 L 7,6 L 8,7 L 7,8 L 8,9 L 7,10 L 5,15 " +
                "L 3,10 L 2,9 L 3,8 L 2,7 L 3,6 L 2,5 L 3,4 Z"
            },
        };

        private static readonly Dictionary<ToolType, Geometry> Cache = BuildCache();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ToolType type && Cache.TryGetValue(type, out Geometry geometry))
            {
                return geometry;
            }

            // An unmapped type draws nothing rather than throwing - a missing icon is a
            // far smaller problem than a list that will not render.
            return Geometry.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Parses and freezes every icon once. Frozen geometry is shared safely across
        /// every row and costs nothing to re-render.
        /// </summary>
        private static Dictionary<ToolType, Geometry> BuildCache()
        {
            var cache = new Dictionary<ToolType, Geometry>();

            foreach (KeyValuePair<ToolType, string> entry in Paths)
            {
                Geometry geometry = Geometry.Parse(entry.Value);
                geometry.Freeze();
                cache[entry.Key] = geometry;
            }

            return cache;
        }
    }
}
