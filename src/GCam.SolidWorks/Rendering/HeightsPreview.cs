using System;
using System.Collections.Generic;
using System.Linq;
using GCam.Core.Abstractions;
using GCam.Core.Diagnostics;
using GCam.Core.Geometry.Primitives;
using GCam.Core.Model;
using GCam.Core.Model.Heights;
using GCam.Core.Rendering;
using GCam.SolidWorks.Extraction;
using GCam.SolidWorks.Selection;
using SolidWorks.Interop.sldworks;

namespace GCam.SolidWorks.Rendering
{
    /// <summary>
    /// Draws an operation's five machining heights as planes, while the Heights tab is
    /// open.
    /// </summary>
    /// <remarks>
    /// The heights are the part of an operation with the least to show for themselves: a
    /// number and a datum it is measured from, where what anybody wants to know is whether
    /// the tool clears the clamps and where the cut stops. A plane each answers that
    /// directly, and the one being edited fills in so there is no counting stacked
    /// outlines to work out which is which.
    ///
    /// <b>Resolved through Core's own rule</b> - <see cref="HeightSetting.TryResolve"/>,
    /// the same call generation makes - so a plane cannot sit somewhere the cut will not.
    /// Each height is resolved on its own, so one that cannot be worked out costs only its
    /// own plane.
    ///
    /// Only while that tab is open, which is the page's business to decide: the Operation
    /// page calls <see cref="Clear"/> for every other tab.
    /// </remarks>
    public sealed class HeightsPreview : IHeightsPreview
    {
        /// <summary>The one scene layer this owns.</summary>
        public const string Layer = "heights";

        /// <summary>
        /// Yellow for the heights above the cut, blue for where it stops, green for where
        /// rapid becomes feed.
        /// </summary>
        /// <remarks>
        /// Clearance, Retract and Top share a colour because they are the same kind of
        /// thing - somewhere the tool is on its way through air - and telling them apart
        /// is what the fill is for. Bottom and Feed are the two that are about the cut
        /// itself, so each gets its own.
        /// </remarks>
        private static readonly RenderColour Above = new RenderColour(0.95, 0.8, 0.15);

        private static readonly RenderColour FeedColour = new RenderColour(0.2, 0.8, 0.3);

        private static readonly RenderColour BottomColour = new RenderColour(0.2, 0.6, 1.0);

        private readonly SldWorks _swApp;
        private readonly ModelDoc2 _model;
        private readonly IViewportRenderer _viewport;
        private readonly ErrorHandler _errors;
        private readonly IGCamLog _log;

        public HeightsPreview(
            SldWorks swApp,
            ModelDoc2 model,
            IViewportRenderer viewport,
            ErrorHandler errors,
            IGCamLog log)
        {
            _swApp = swApp ?? throw new ArgumentNullException(nameof(swApp));
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
            _errors = errors ?? throw new ArgumentNullException(nameof(errors));
            _log = log ?? NullLog.Instance;
        }

        /// <summary>
        /// Entry point 10, by way of the Operation page's tab, focus and change callbacks.
        /// </summary>
        /// <remarks>
        /// Quiet on failure. This runs on every keystroke in a height box and on every
        /// move of the focus; a dialog here would be unescapable.
        /// </remarks>
        public void Show(Job job, Operation operation, HeightKind? filled)
        {
            try
            {
                if (job == null || operation?.Heights == null)
                {
                    Clear();
                    return;
                }

                JobFrame frame = CoordinateSystems.Resolve(_swApp, _model, job.CoordinateSystem);
                List<Body2> bodies = JobSelections.SolidBodies(_model, job.ModelBodies);
                Bounds? measured = bodies.Count == 0 ? null : ModelExtent.OfBodies(bodies, frame);

                if (measured == null)
                {
                    // No bodies, or none that can be measured. There is nothing to size a
                    // plane against and nothing for most of the heights to be measured
                    // from either. Not worth a warning on every keystroke.
                    _log.Debug("No model extent for job '{0}', so no height planes.", job.Name);
                    Clear();
                    return;
                }

                Bounds model = measured.Value;
                Bounds stock = job.Stock.ComputeBounds(model);

                // Sized to whichever is bigger. Most heights are measured from the stock,
                // and with a side offset the stock is the larger - a plane that stopped at
                // the model would not reach the thing it is measured from.
                Bounds extent = stock.IsEmpty ? model : model.Union(stock);

                // Heights measured from a picked face, edge or vertex are resolved through
                // the same call generation makes, so a plane appears the moment the pick
                // does rather than only once the operation has been generated.
                var context = HeightContext.From(
                    stock, model, EntityHeights.ForOperation(_model, operation.Heights, frame));

                var batches = new List<RenderBatch>();

                foreach (KeyValuePair<HeightKind, HeightSetting> height in Heights(operation))
                {
                    batches.AddRange(
                        Plane(height.Value, height.Key, context, extent, frame, filled));
                }

                if (batches.Count == 0)
                {
                    Clear();
                    return;
                }

                _viewport.Scene.Set(Layer, batches);
            }
            catch (Exception ex)
            {
                _errors.Handle(ex, nameof(Show), quiet: true);
                Clear();
            }
        }

        public void Clear()
        {
            try
            {
                _viewport.Scene.Remove(Layer);
            }
            catch (Exception ex)
            {
                _errors.Handle(ex, nameof(Clear), quiet: true);
            }
        }

        private static IEnumerable<KeyValuePair<HeightKind, HeightSetting>> Heights(
            Operation operation)
        {
            OperationHeights heights = operation.Heights;

            yield return Pair(HeightKind.Clearance, heights.Clearance);
            yield return Pair(HeightKind.Retract, heights.Retract);
            yield return Pair(HeightKind.Feed, heights.Feed);
            yield return Pair(HeightKind.Top, heights.Top);
            yield return Pair(HeightKind.Bottom, heights.Bottom);
        }

        private static KeyValuePair<HeightKind, HeightSetting> Pair(
            HeightKind kind, HeightSetting setting) =>
            new KeyValuePair<HeightKind, HeightSetting>(kind, setting);

        private static IEnumerable<RenderBatch> Plane(
            HeightSetting height,
            HeightKind kind,
            HeightContext context,
            Bounds extent,
            JobFrame frame,
            HeightKind? filled)
        {
            double z;

            if (height == null || !height.TryResolve(context, out z))
            {
                // A height measured from a selection that is not wired up yet, or one
                // measured from the contour, which has a Z per contour and so none for
                // the operation - HSMWorks draws nothing for it either. No plane rather
                // than one at a guessed Z.
                return new RenderBatch[0];
            }

            return HeightPlaneMesh.Build(extent, z, frame.ToPart, ColourFor(kind), filled == kind);
        }

        private static RenderColour ColourFor(HeightKind kind)
        {
            switch (kind)
            {
                case HeightKind.Bottom: return BottomColour;
                case HeightKind.Feed: return FeedColour;
                default: return Above;
            }
        }

    }
}
