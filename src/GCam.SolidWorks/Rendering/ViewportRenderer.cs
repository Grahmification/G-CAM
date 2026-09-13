using System;
using System.Collections.Generic;
using System.Linq;
using GCam.Core.Abstractions;
using GCam.Core.Diagnostics;
using GCam.Core.Rendering;
using SolidWorks.Interop.sldworks;

namespace GCam.SolidWorks.Rendering
{
    /// <summary>
    /// G-CAM's graphics in one document's 3D windows: owns the scene, hooks the views,
    /// and draws when SOLIDWORKS says so.
    /// </summary>
    /// <remarks>
    /// <b>BufferSwapNotify is the only place drawing may happen.</b> The help is explicit
    /// that it fires with the OpenGL context current and the matrices already set up so
    /// that what we draw lands correctly relative to the part - which is why a batch's
    /// vertices are plain part coordinates and no projection work happens anywhere in
    /// G-CAM. Drawing from anywhere else means no context, or somebody else's.
    ///
    /// <b>One renderer per document, hooked to every window that document has.</b> A
    /// model view is a window, and Window ▸ New Window gives a part two of them; both
    /// have to draw or the overlay appears in one and not the other. The set is re-read
    /// whenever something changes rather than tracked by event, because
    /// <see cref="Invalidate"/> is not a hot path and re-reading is immune to a window
    /// event being missed.
    ///
    /// Verified drawing on SOLIDWORKS 2025 SP3, with "Enhanced graphics performance" both
    /// on and off. See docs/solidworks-api/opengl-overlay.md.
    /// </remarks>
    public sealed class ViewportRenderer : IViewportRenderer, IDisposable
    {
        /// <summary>
        /// Consecutive failed frames before the overlay switches itself off. Three is
        /// enough to ride out a transient - a document closing underneath a repaint -
        /// and few enough that a genuinely broken draw stops before it has filled the
        /// log.
        /// </summary>
        private const int FailuresBeforeGivingUp = 3;

        private readonly ModelDoc2 _model;
        private readonly ErrorHandler _errors;
        private readonly IGCamLog _log;
        private readonly SceneRenderer _renderer = new SceneRenderer();

        // The views whose BufferSwapNotify we are subscribed to. Held so they can be
        // unsubscribed; keyed by the runtime callable wrapper, which is one per COM
        // identity and so compares as the same view however it was reached.
        private readonly HashSet<ModelView> _hooked = new HashSet<ModelView>();

        private bool _drawn;
        private bool _disposed;
        private bool _reportedGlError;
        private int _consecutiveFailures;

        public ViewportRenderer(ModelDoc2 model, ErrorHandler errors, IGCamLog log)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _errors = errors ?? throw new ArgumentNullException(nameof(errors));
            _log = log ?? NullLog.Instance;

            Scene = new RenderScene();
            Scene.Changed += OnSceneChanged;

            HookViews();
        }

        public RenderScene Scene { get; }

        /// <summary>True once SOLIDWORKS has actually asked this renderer to draw.</summary>
        /// <remarks>
        /// The one fact that separates "the drawing code is wrong" from "the
        /// notification never arrives", which are otherwise indistinguishable - both are
        /// a blank screen. Kept even though no configuration is currently known to
        /// produce the second: it costs a bool, and it is the first question worth asking
        /// of an empty view.
        /// </remarks>
        public bool HasDrawn => _drawn;

        /// <summary>
        /// Entry point 9. Fires immediately before SOLIDWORKS swaps buffers, with its
        /// OpenGL context current.
        /// </summary>
        /// <remarks>
        /// Quiet on failure, and it has to be: this runs on every repaint of every
        /// window, so a dialog here would follow the user around the graphics area and
        /// re-open behind itself as the repaint it interrupted causes it again.
        ///
        /// <b>Three failures in a row and the overlay gives up.</b> A drawing fault does
        /// not get better by itself - whatever is wrong will be just as wrong on the next
        /// frame - so without this the log fills at the frame rate and G-CAM keeps
        /// reaching into a context it is evidently mishandling. Consecutive, not total:
        /// one failed frame during a document close is not the same as a broken renderer.
        /// See docs/error-handling.md, entry point 9.
        /// </remarks>
        private int OnBufferSwap()
        {
            try
            {
                _drawn = true;

                if (Scene.IsEmpty)
                {
                    return 0;
                }

                _renderer.Draw(Scene);

                ReportGlErrors();

                _consecutiveFailures = 0;
            }
            catch (Exception ex)
            {
                OnDrawFailed(ex);
            }

            return 0;
        }

        private void OnDrawFailed(Exception ex)
        {
            _consecutiveFailures++;

            if (_consecutiveFailures < FailuresBeforeGivingUp)
            {
                _errors.Handle(ex, nameof(OnBufferSwap), quiet: true);
                return;
            }

            // Reported once, and not quietly: the overlay is about to stop working for
            // the rest of the session, which the user would otherwise have no way to
            // learn. Unhooking from inside the notification is safe - the subscription
            // list is not being enumerated by us - but the scene is cleared first so
            // nothing is left half-drawn.
            _errors.Handle(ex, nameof(OnBufferSwap));

            _log.Warn(
                "The G-CAM overlay failed {0} times in a row in {1} and has been switched off " +
                "for this document. Reopen the part to try again.",
                _consecutiveFailures,
                SafeTitle());

            Dispose();
        }

        /// <summary>
        /// Asks every window showing this document to repaint.
        /// </summary>
        /// <remarks>
        /// The view set is refreshed first, so a window opened since the last change
        /// starts drawing without anything having to notice it appear.
        /// </remarks>
        public void Invalidate()
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                HookViews();

                foreach (ModelView view in _hooked.ToList())
                {
                    // Null asks for the whole window. GraphicsRedraw repaints
                    // synchronously, so OnBufferSwap has run by the time this returns.
                    view.GraphicsRedraw(null);
                }
            }
            catch (Exception ex)
            {
                _errors.Handle(ex, nameof(Invalidate), quiet: true);
            }
        }

        private void OnSceneChanged(object sender, EventArgs e) => Invalidate();

        /// <summary>
        /// Brings the set of subscribed views into line with the windows the document
        /// actually has.
        /// </summary>
        /// <remarks>
        /// The same idempotent compare-and-adjust shape as JobTreeTabs.Sync, and for the
        /// same reason: it cannot drift, and a missed or duplicated notification costs
        /// nothing.
        /// </remarks>
        private void HookViews()
        {
            var live = new HashSet<ModelView>(CurrentViews());

            foreach (ModelView gone in _hooked.Where(v => !live.Contains(v)).ToList())
            {
                Unhook(gone);
            }

            foreach (ModelView added in live.Where(v => !_hooked.Contains(v)))
            {
                added.BufferSwapNotify += OnBufferSwap;
                _hooked.Add(added);
            }
        }

        /// <summary>
        /// Every window showing this document.
        /// </summary>
        /// <remarks>
        /// ModelView, not IModelView: the coclass interface is the one that carries the
        /// event, and IModelView alone has no BufferSwapNotify to subscribe to.
        /// </remarks>
        private IEnumerable<ModelView> CurrentViews()
        {
            var view = _model.GetFirstModelView() as ModelView;

            while (view != null)
            {
                yield return view;

                view = view.GetNext() as ModelView;
            }
        }

        private void Unhook(ModelView view)
        {
            _hooked.Remove(view);

            try
            {
                view.BufferSwapNotify -= OnBufferSwap;
            }
            catch (Exception ex)
            {
                // Expected when the window has already gone.
                _errors.Handle(ex, nameof(Unhook), quiet: true);
            }

            // Deliberately not released. We did not create these - they are the
            // document's own windows, and the wrapper is the one SOLIDWORKS and every
            // other add-in are using. The same reasoning as the ModelDoc2 note in
            // JobTreeTabs.Forget.
        }

        /// <summary>
        /// Reports an OpenGL error once, then stops.
        /// </summary>
        /// <remarks>
        /// Once, because an error in the draw will recur on every frame and would fill
        /// the log faster than anything else in it. The first occurrence is the one worth
        /// having.
        /// </remarks>
        private void ReportGlErrors()
        {
            if (_reportedGlError)
            {
                return;
            }

            uint error = Interop.Gl.DrainErrors();

            if (error == Interop.Gl.GL_NO_ERROR)
            {
                return;
            }

            _reportedGlError = true;

            _log.Warn(
                "OpenGL reported error 0x{0:X4} while drawing the G-CAM overlay for {1}. " +
                "Further errors from this view are not logged.",
                error,
                SafeTitle());
        }

        private string SafeTitle()
        {
            try
            {
                return _model.GetTitle();
            }
            catch (Exception)
            {
                return "a closed document";
            }
        }

        // There was a WarnIfOverlayDisabled here that checked
        // swUserPreferenceToggle_e.swEnablePerformancePipeline - "Enhanced graphics
        // performance" - and warned that the overlay would not draw. It has been removed:
        // the overlay was then tested on SOLIDWORKS 2025 SP3 with that option both on and
        // off and drew correctly either way. The warning was inherited wisdom from older
        // versions, and a warning that says graphics will not appear while they are
        // appearing is worse than no warning at all. See
        // docs/solidworks-api/opengl-overlay.md.

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            Scene.Changed -= OnSceneChanged;
            Scene.Clear();

            foreach (ModelView view in _hooked.ToList())
            {
                Unhook(view);
            }

            _renderer.Reset();
        }
    }
}
