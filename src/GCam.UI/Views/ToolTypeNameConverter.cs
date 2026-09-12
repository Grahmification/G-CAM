using System;
using System.Globalization;
using System.Windows.Data;
using GCam.Core.Tooling;

namespace GCam.UI.Views
{
    /// <summary>
    /// Renders <see cref="ToolType"/> as readable text - "Bull nose end mill" rather
    /// than "BullNoseEndMill".
    /// </summary>
    /// <remarks>
    /// A converter rather than a display property on Tool: how a type is spelled for a
    /// human is presentation, and Core has no business knowing about it. The wording
    /// itself lives in Core so that search matches what is on screen.
    /// </remarks>
    public sealed class ToolTypeNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is ToolType type ? ToolSearch.DisplayName(type) : string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
