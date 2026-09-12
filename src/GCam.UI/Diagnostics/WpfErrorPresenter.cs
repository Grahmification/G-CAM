using System;
using System.Windows.Interop;
using System.Windows.Threading;
using GCam.Core.Diagnostics;

namespace GCam.UI.Diagnostics
{
    /// <summary>
    /// Shows G-CAM errors in a WPF dialog owned by the SOLIDWORKS main window.
    /// </summary>
    /// <remarks>
    /// Construct this on the SOLIDWORKS main STA thread - it captures that thread's
    /// dispatcher and marshals to it, so background toolpath work can report failures
    /// without callers having to think about threads.
    /// </remarks>
    public sealed class WpfErrorPresenter : IErrorPresenter
    {
        private readonly Dispatcher _dispatcher;
        private readonly Func<IntPtr> _ownerHandle;
        private readonly string _logDirectory;

        /// <param name="ownerHandle">
        /// Supplies the SOLIDWORKS main window handle. A callback rather than a value
        /// because the frame is not available at every point during startup.
        /// </param>
        public WpfErrorPresenter(Func<IntPtr> ownerHandle, string logDirectory)
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            _ownerHandle = ownerHandle ?? (() => IntPtr.Zero);
            _logDirectory = logDirectory;
        }

        public void ShowError(Exception ex, string context)
        {
            Show(() => ErrorDialog.ForException(ex, context, _logDirectory));
        }

        public void ShowUserError(string message)
        {
            Show(() => ErrorDialog.ForUserMessage(message, _logDirectory));
        }

        private void Show(Func<ErrorDialog> create)
        {
            if (!_dispatcher.CheckAccess())
            {
                // Called from a worker thread. Marshal and return immediately rather
                // than blocking it behind a modal dialog.
                _dispatcher.BeginInvoke(new Action(() => Show(create)));
                return;
            }

            ErrorDialog dialog = create();

            IntPtr owner = SafeOwnerHandle();
            if (owner != IntPtr.Zero)
            {
                new WindowInteropHelper(dialog).Owner = owner;
            }

            dialog.ShowDialog();
        }

        private IntPtr SafeOwnerHandle()
        {
            try
            {
                return _ownerHandle();
            }
            catch (Exception)
            {
                // An unowned dialog is worse than an owned one, but far better than
                // losing the error because we could not find a window handle.
                return IntPtr.Zero;
            }
        }
    }
}
