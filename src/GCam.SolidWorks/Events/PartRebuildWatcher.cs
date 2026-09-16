using System;
using GCam.Core.Diagnostics;
using GCam.Core.Generation;
using GCam.Core.Model;
using SolidWorks.Interop.sldworks;
using Timer = System.Windows.Forms.Timer;

namespace GCam.SolidWorks.Events
{
    /// <summary>
    /// Marks a part's operations stale when SOLIDWORKS rebuilds it.
    /// </summary>
    /// <remarks>
    /// Without this a toolpath outlives the geometry it was cut from: change a dimension
    /// and the old path stays on screen, at full strength, with every operation still
    /// calling itself Generated. That is the one failure staleness exists to prevent,
    /// because it is the one that reaches a machine.
    ///
    /// <b>One per open part, like <c>JobStorageHook</c>.</b> The notification is raised by
    /// <see cref="PartDoc"/> rather than by the application and carries no document, so a
    /// shared handler would have to guess which part rebuilt. Holding the part in the
    /// subscriber removes the question.
    ///
    /// <b><c>RegenPostNotify2</c>, not <c>RegenPostNotify</c></b> - the latter is marked
    /// obsolete in the 2025 help. It post-notifies a rebuild *or* a rollback, which is
    /// right for both: rolling the tree back past the feature an operation cuts changes
    /// what that operation would produce just as surely as editing it would.
    /// </remarks>
    public sealed class PartRebuildWatcher : IDisposable
    {
        private readonly PartDoc _part;
        private readonly Func<JobDocument> _jobs;
        private readonly Action _changed;
        private readonly ErrorHandler _errors;
        private readonly IGCamLog _log;

        private Timer _timer;
        private bool _subscribed;

        /// <param name="changed">
        /// Run once, after the notification has returned, when a rebuild actually marked
        /// something. Redrawing the tree and the 3D scene is the caller's business.
        /// </param>
        public PartRebuildWatcher(
            PartDoc part,
            Func<JobDocument> jobs,
            Action changed,
            ErrorHandler errors,
            IGCamLog log)
        {
            _part = part ?? throw new ArgumentNullException(nameof(part));
            _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
            _changed = changed ?? throw new ArgumentNullException(nameof(changed));
            _errors = errors ?? throw new ArgumentNullException(nameof(errors));
            _log = log ?? NullLog.Instance;

            _part.RegenPostNotify2 += OnRegenPost;
            _subscribed = true;
        }

        /// <summary>
        /// Entry point 8. The part has finished rebuilding or rolling back.
        /// </summary>
        /// <param name="stopFeature">
        /// The feature below the rollback bar for a rollback, null for a rebuild. G-CAM
        /// treats the two the same: both change what the model is.
        /// </param>
        /// <remarks>
        /// Marking stale is pure Core and touches no COM, so it is safe to do here. The
        /// redraw is not: it measures bodies and rebuilds a scene, and this is running
        /// inside SOLIDWORKS' own rebuild. It is deferred to the message pump for the same
        /// reason <c>GCamPropertyPage.RebuildAfterHandlerReturns</c> is.
        ///
        /// Quiet on failure. This fires on every rebuild the user asks for, so a dialog
        /// here would be one per <kbd>Ctrl</kbd>+<kbd>B</kbd>.
        /// </remarks>
        private int OnRegenPost(object stopFeature)
        {
            try
            {
                int marked = Staleness.ModelRebuilt(_jobs());

                if (marked > 0)
                {
                    _log.Debug(
                        "The part was rebuilt; {0} operation(s) are now out of date.", marked);

                    RedrawAfterHandlerReturns();
                }
            }
            catch (Exception ex)
            {
                _errors.Handle(ex, nameof(OnRegenPost), quiet: true);
            }

            return 0;
        }

        /// <summary>
        /// Runs the redraw on a later turn of the message pump.
        /// </summary>
        /// <remarks>
        /// One timer at a time. A rebuild that arrives while one is already pending needs
        /// no second redraw - the first has not run yet and will see the same marks.
        /// </remarks>
        private void RedrawAfterHandlerReturns()
        {
            if (_timer != null)
            {
                return;
            }

            _timer = new Timer { Interval = 1 };
            _timer.Tick += OnRedrawTick;
            _timer.Start();
        }

        /// <summary>Entry point 8. On the STA thread, after the notification returned.</summary>
        private void OnRedrawTick(object sender, EventArgs e)
        {
            try
            {
                StopTimer();
                _changed();
            }
            catch (Exception ex)
            {
                _errors.Handle(ex, nameof(OnRedrawTick), quiet: true);
            }
        }

        private void StopTimer()
        {
            if (_timer == null)
            {
                return;
            }

            _timer.Stop();
            _timer.Tick -= OnRedrawTick;
            _timer.Dispose();
            _timer = null;
        }

        public void Dispose()
        {
            StopTimer();

            if (!_subscribed)
            {
                return;
            }

            try
            {
                _part.RegenPostNotify2 -= OnRegenPost;
            }
            catch (Exception ex)
            {
                // Expected when the document has already gone.
                _log.Error(ex, "Unsubscribing from the rebuild notification failed.");
            }
            finally
            {
                _subscribed = false;
            }
        }
    }
}
