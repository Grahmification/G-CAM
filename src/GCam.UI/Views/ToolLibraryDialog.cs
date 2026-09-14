using System;
using System.Windows.Interop;
using GCam.Core.Diagnostics;
using GCam.Core.Settings;
using GCam.Core.Tooling;
using GCam.UI.ViewModels;

namespace GCam.UI.Views
{
    /// <summary>
    /// Opens the tool library browser.
    /// </summary>
    /// <remarks>
    /// Exists so callers can open a WPF window without referencing WPF themselves - the
    /// only things crossing the boundary are a window handle and Core types. That keeps
    /// GCam.AddIn free of PresentationFramework.
    /// </remarks>
    public static class ToolLibraryDialog
    {
        /// <summary>
        /// Opens the browser for looking at libraries.
        /// </summary>
        /// <remarks>
        /// Modal for now. A modeless browser would let you keep working in SOLIDWORKS
        /// with it open, and the window is written so that is a one-line change - but
        /// modal has a nested message pump of its own, which removes any doubt about
        /// keyboard input reaching the search box from inside a Win32 host. Worth
        /// revisiting once it has been used against a real SOLIDWORKS session.
        /// </remarks>
        public static void ShowBrowser(IntPtr owner, IGCamSettings settings, IGCamLog log = null)
        {
            Show(owner, settings, log, onActivated: null);
        }

        /// <summary>
        /// Opens the browser as a tool chooser. Returns a copy of the tool the user
        /// picked, stamped with the library it came from, or null if they picked nothing.
        /// </summary>
        /// <remarks>
        /// Two gestures choose a tool, because both are what people try: double-clicking
        /// a row picks it and closes, and OK picks whichever row is selected. Cancel,
        /// Escape and the close box pick nothing - backing out of a browse is not a
        /// choice, and returning the last-highlighted row would assign a tool nobody
        /// agreed to.
        ///
        /// The result is checked out of its library rather than handed over directly, so
        /// the caller can put it in a part without the two sharing one object. See
        /// <see cref="ToolLibraryWindow.CheckOut"/>.
        /// </remarks>
        public static Tool PickTool(IntPtr owner, IGCamSettings settings, IGCamLog log = null)
        {
            Tool activated = null;
            ToolLibraryWindow window = Show(
                owner, settings, log, onActivated: tool => activated = tool);

            return window.CheckOut(activated ?? (window.Committed ? window.SelectedTool : null));
        }

        private static ToolLibraryWindow Show(
            IntPtr owner, IGCamSettings settings, IGCamLog log, Action<Tool> onActivated)
        {
            var model = new ToolLibraryBrowserViewModel(settings, log: log);
            var window = new ToolLibraryWindow(model);

            if (onActivated != null)
            {
                // Double-clicking a tool picks it and closes, which is what people expect
                // from a chooser.
                window.ToolActivated += (s, tool) =>
                {
                    onActivated(tool);
                    window.Close();
                };
            }

            if (owner != IntPtr.Zero)
            {
                new WindowInteropHelper(window).Owner = owner;
            }

            window.ShowDialog();
            return window;
        }
    }
}
