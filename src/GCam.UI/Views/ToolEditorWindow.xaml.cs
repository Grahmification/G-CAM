using System;
using System.Windows;
using GCam.Core.Tooling;
using GCam.UI.ViewModels;

namespace GCam.UI.Views
{
    /// <summary>
    /// Tabbed editor for a single tool, with a live profile preview.
    /// </summary>
    /// <remarks>
    /// Edits a clone; <see cref="Result"/> is only meaningful when ShowDialog returned
    /// true. Cancel drops the clone, so nothing needs undoing.
    /// </remarks>
    public partial class ToolEditorWindow : Window
    {
        private readonly ToolEditorViewModel _model;

        public ToolEditorWindow(ToolEditorViewModel model)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));

            InitializeComponent();
            DataContext = _model;
        }

        /// <summary>The edited tool. Valid only after OK.</summary>
        public Tool Result => _model.Result;

        private void OnOk(object sender, RoutedEventArgs e)
        {
            if (!_model.IsValid)
            {
                return;
            }

            DialogResult = true;
            Close();
        }
    }
}
