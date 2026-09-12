using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GCam.UI.Views
{
    /// <summary>
    /// True to Visible, false to Collapsed.
    /// </summary>
    /// <remarks>
    /// WPF ships BooleanToVisibilityConverter but provides no resource key for it, so
    /// every window would otherwise declare its own. Declared once in App resources.
    /// </remarks>
    public sealed class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool flag && flag ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
