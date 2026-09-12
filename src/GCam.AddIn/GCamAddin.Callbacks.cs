using System;
using GCam.AddIn.Commands;
using GCam.UI.Views;
using SolidWorks.Interop.sldworks;

namespace GCam.AddIn
{
    /// <summary>
    /// Toolbar callbacks. SOLIDWORKS resolves these by name at click time - they must
    /// stay public, and their names must match the strings passed to AddCommandItem2.
    /// </summary>
    public partial class GCamAddin
    {
        /// <summary>Invoked when a G-CAM toolbar or menu item is clicked.</summary>
        public void OnCommand(int commandId)
        {
            switch ((GCamCommand)commandId)
            {
                case GCamCommand.ToolLibrary:
                    ToolLibraryDialog.Show(MainWindowHandle());
                    break;

                // Deliberately empty: the UI exists so the wiring can be verified,
                // the behaviour arrives with the first vertical slice.
                case GCamCommand.NewJob:
                case GCamCommand.PostProcess:
                case GCamCommand.Simulate:
                default:
                    break;
            }
        }

        /// <summary>
        /// Controls whether each item is enabled. SOLIDWORKS calls this before
        /// displaying the item: 1 enables, 0 disables (greyed).
        /// </summary>
        public int OnCommandEnable(int commandId)
        {
            return 1;
        }

        /// <summary>
        /// Handle of the SOLIDWORKS main frame, for parenting modal dialogs.
        /// </summary>
        private IntPtr MainWindowHandle()
        {
            // GetHWndx64 rather than GetHWnd: SOLIDWORKS is 64-bit, and the 32-bit
            // variant truncates the handle.
            var frame = _swApp.Frame() as IFrame;
            return frame == null ? IntPtr.Zero : new IntPtr(frame.GetHWndx64());
        }
    }
}
