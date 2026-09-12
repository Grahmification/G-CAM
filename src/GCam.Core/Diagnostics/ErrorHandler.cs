using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace GCam.Core.Diagnostics
{
    /// <summary>
    /// The single place that decides what happens to an exception caught at an entry point.
    /// </summary>
    /// <remarks>
    /// Every method SOLIDWORKS, WPF or the task scheduler can call wraps its body in
    /// try/catch and hands the exception here. Interior code throws freely - only
    /// entry points catch - so the stack trace arrives intact.
    ///
    /// See docs/error-handling.md for the entry-point list and why this is plain
    /// try/catch rather than a lambda wrapper.
    /// </remarks>
    public sealed class ErrorHandler
    {
        private readonly IGCamLog _log;
        private readonly IErrorPresenter _presenter;

        // Contexts already reported, so a callback that fails on every repaint
        // produces one dialog and one stack trace rather than thousands.
        private readonly HashSet<string> _reported = new HashSet<string>(StringComparer.Ordinal);
        private readonly object _gate = new object();

        public ErrorHandler(IGCamLog log, IErrorPresenter presenter)
        {
            _log = log ?? NullLog.Instance;
            _presenter = presenter ?? NullErrorPresenter.Instance;
        }

        /// <summary>
        /// Logs an exception caught at an entry point and decides whether to show it.
        /// </summary>
        /// <param name="ex">The exception. Null is ignored.</param>
        /// <param name="context">
        /// The entry point it escaped from, e.g. nameof(OnCommand). Used for the log
        /// message and as the repeat-suppression key, so keep it stable.
        /// </param>
        /// <param name="quiet">
        /// True to log but never show UI. For callbacks SOLIDWORKS invokes constantly
        /// (OnCommandEnable, the render hook) and for teardown paths where a modal
        /// dialog would block an unload.
        /// </param>
        public void Handle(Exception ex, string context, bool quiet = false)
        {
            if (ex == null)
            {
                return;
            }

            if (ex is GCamUserException userError)
            {
                _log.Info("User error in {0}: {1}", context, userError.Message);
                if (!quiet)
                {
                    Present(() => _presenter.ShowUserError(userError.Message));
                }

                return;
            }

            bool firstTime;
            lock (_gate)
            {
                firstTime = _reported.Add(context + "|" + ex.GetType().FullName);
            }

            if (firstTime)
            {
                _log.Error(ex, "Unhandled exception in {0}", context);
            }
            else
            {
                _log.Debug("Repeat of {0} in {1}: {2}", ex.GetType().Name, context, ex.Message);
            }

#if DEBUG
            // Only when a debugger is actually attached. Rethrowing without one would
            // let the exception escape into SOLIDWORKS, which discards it silently -
            // strictly worse than reporting it.
            //
            // Note this rethrow surfaces at THIS line, not the original throw site.
            // For that, turn on Debug > Windows > Exception Settings > Common Language
            // Runtime Exceptions, which breaks before any catch runs.
            if (Debugger.IsAttached)
            {
                ExceptionDispatchInfo.Capture(ex).Throw();
            }
#endif

            if (!quiet && firstTime)
            {
                Present(() => _presenter.ShowError(ex, context));
            }
        }

        /// <summary>
        /// Presenters are not supposed to throw, but if one does it must not replace
        /// the failure we were already reporting.
        /// </summary>
        private void Present(Action show)
        {
            try
            {
                show();
            }
            catch (Exception presenterFailure)
            {
                _log.Error(presenterFailure, "The error presenter itself failed");
            }
        }
    }
}
