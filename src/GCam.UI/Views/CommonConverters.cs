using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GCam.UI.Views
{
    /// <summary>
    /// True to Visible, false to Collapsed. Set <see cref="Invert"/> for the opposite.
    /// </summary>
    /// <remarks>
    /// WPF ships BooleanToVisibilityConverter but provides no resource key for it, so
    /// every window declares its own instance in its own Window.Resources. There is no
    /// App.xaml in this add-in to hold a shared one - SOLIDWORKS owns the application
    /// object, not us.
    ///
    /// Invert exists so that "show this when the flag is false" does not need a second
    /// converter class. Two keyed instances of this one are clearer than two types that
    /// differ by a negation.
    /// </remarks>
    public sealed class BoolToVisibilityConverter : IValueConverter
    {
        /// <summary>When true, false becomes Visible and true becomes Collapsed.</summary>
        public bool Invert { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool flag = value is bool b && b;

            if (Invert)
            {
                flag = !flag;
            }

            return flag ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
