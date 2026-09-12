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
        /// <summary>
        /// Entry point 4. Invoked when a G-CAM toolbar or menu item is clicked - the
        /// main user-facing path, so failures are reported in full.
        /// </summary>
        public void OnCommand(int commandId)
        {
            try
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
            catch (Exception ex)
            {
                _errors.Handle(ex, nameof(OnCommand) + "(" + commandId + ")");
            }
        }

        /// <summary>
        /// Entry point 5. Controls whether each item is enabled: 1 enables, 0 disables.
        /// </summary>
        /// <remarks>
        /// SOLIDWORKS calls this before displaying the item, so it runs constantly.
        /// Never show UI from here - a dialog would reappear on every repaint. On
        /// failure the item is greyed out and the problem is logged once; a greyed
        /// button with a log line beats an unusable SOLIDWORKS.
        /// </remarks>
        public int OnCommandEnable(int commandId)
        {
            try
            {
                return 1;
            }
            catch (Exception ex)
            {
                _errors.Handle(ex, nameof(OnCommandEnable) + "(" + commandId + ")", quiet: true);
                return 0;
            }
        }

        /// <summary>
        /// Handle of the SOLIDWORKS main frame, for parenting modal dialogs.
        /// </summary>
        private IntPtr MainWindowHandle()
        {
            // GetHWndx64 rather than GetHWnd: SOLIDWORKS is 64-bit, and the 32-bit
            // variant truncates the handle.
            var frame = _swApp?.Frame() as IFrame;
            return frame == null ? IntPtr.Zero : new IntPtr(frame.GetHWndx64());
        }
    }
}
