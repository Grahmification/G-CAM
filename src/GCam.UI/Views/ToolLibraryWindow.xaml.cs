using System.Windows;

namespace GCam.UI.Views
{
    /// <summary>
    /// Popup viewer for the external tool library XML. No behaviour yet.
    /// </summary>
    public partial class ToolLibraryWindow : Window
    {
        public ToolLibraryWindow()
        {
            InitializeComponent();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
