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
        /// Opens the browser as a tool chooser. Returns the tool the user picked, or null.
        /// </summary>
        /// <remarks>
        /// Nothing calls this yet. It exists because assigning a tool to an operation is
        /// the obvious next use, and having the entry point now means the window's
        /// contract does not have to change then.
        /// </remarks>
        public static Tool PickTool(IntPtr owner, IGCamSettings settings, IGCamLog log = null)
        {
            Tool picked = null;
            ToolLibraryWindow window = Show(
                owner, settings, log, onActivated: tool => picked = tool);

            return picked ?? window.SelectedTool;
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
