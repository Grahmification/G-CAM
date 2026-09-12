using System;
using System.Windows.Interop;

namespace GCam.UI.Views
{
    /// <summary>
    /// Launches the tool library window.
    /// </summary>
    /// <remarks>
    /// Exists so callers can open a WPF dialog without referencing WPF themselves -
    /// the only thing crossing the boundary is an owner window handle. That keeps
    /// GCam.AddIn free of PresentationFramework.
    /// </remarks>
    public static class ToolLibraryDialog
    {
        /// <param name="owner">
        /// Handle of the window to parent to - the SOLIDWORKS main frame. Passing
        /// IntPtr.Zero shows the dialog unowned, which lets it fall behind SOLIDWORKS.
        /// </param>
        public static void Show(IntPtr owner)
        {
            var window = new ToolLibraryWindow();

            if (owner != IntPtr.Zero)
            {
                new WindowInteropHelper(window).Owner = owner;
            }

            window.ShowDialog();
        }
    }
}
