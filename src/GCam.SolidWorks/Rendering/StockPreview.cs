using System;
using System.Collections.Generic;
using GCam.Core.Abstractions;
using GCam.Core.Diagnostics;
using GCam.Core.Geometry.Primitives;
using GCam.Core.Model;
using GCam.Core.Rendering;
using GCam.SolidWorks.Extraction;
using GCam.SolidWorks.Selection;
using SolidWorks.Interop.sldworks;

namespace GCam.SolidWorks.Rendering
{
    /// <summary>
    /// Shows the selected job's stock as a translucent box in the 3D view.
    /// </summary>
    /// <remarks>
    /// The first thing G-CAM draws, and the shape every later one follows: measure the
    /// model, ask Core what the answer looks like, hand the result to a layer. Nothing
    /// here knows about OpenGL and nothing in Core knows about SOLIDWORKS.
    ///
    /// <b>The box is built in job coordinates and drawn in part coordinates.</b> Stock
    /// offsets mean "above the top of the model" and "on all four sides" in the job's
    /// own frame, so on a rotated coordinate system the box is rotated with it. That is
    /// the whole reason the extent is measured along the job's axes rather than taken
    /// from a part-aligned bounding box.
    ///
    /// One per document, living as long as the document's G-CAM tab.
    /// </remarks>
    public sealed class StockPreview : IJobPreview
    {
        /// <summary>
        /// The scene layer this owns. Named rather than positional so toolpaths and the
        /// simulated tool can sit beside it without either having to know about the
        /// other.
        /// </summary>
        private const string LayerName = "stock";

        // Yellow, because it reads as raw material against SOLIDWORKS' grey model and
        // against every part colour a user is likely to have chosen. A quarter alpha is
        // enough to see the box without losing the model inside it; the edges are nearly
        // opaque so the extent stays legible where the walls are nearly edge-on.
        private static readonly RenderColour FaceColour = new RenderColour(1.0, 0.83, 0.10, 0.22);
        private static readonly RenderColour EdgeColour = new RenderColour(0.85, 0.65, 0.00, 0.95);

        private const double EdgeWidth = 1.5;

        private readonly SldWorks _swApp;
        private readonly ModelDoc2 _model;
        private readonly IViewportRenderer _viewport;
        private readonly ErrorHandler _errors;
        private readonly IGCamLog _log;

        public StockPreview(
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
        /// Entry points 7 and 10 reach this: the job tree's WPF selection, and the Job
        /// page's PropertyManager change callbacks.
        /// </summary>
        /// <remarks>
        /// Quiet on failure. This runs on every keystroke in a stock field; a dialog here
        /// would interrupt the edit that provoked it, and the preview is decoration -
        /// the job is still perfectly editable without it.
        /// </remarks>
        public void ShowJob(Job job)
        {
            try
            {
                IReadOnlyList<RenderBatch> batches = job == null ? null : Build(job);

                if (batches == null || batches.Count == 0)
                {
                    _viewport.Scene.Remove(LayerName);
                    return;
                }

                _viewport.Scene.Set(LayerName, batches);
            }
            catch (Exception ex)
            {
                _errors.Handle(ex, nameof(ShowJob), quiet: true);
            }
        }

        /// <summary>
        /// The batches for one job's stock, or null when there is nothing to draw.
        /// </summary>
        private IReadOnlyList<RenderBatch> Build(Job job)
        {
            JobFrame frame = CoordinateSystems.Resolve(_swApp, _model, job.CoordinateSystemName);

            List<Body2> bodies = JobSelections.SolidBodies(_model, job.ModelBodyNames);

            if (bodies.Count == 0)
            {
                // A part with no solid bodies, or a job naming only bodies that have
                // since gone. Neither is worth a warning on every repaint.
                _log.Debug("No bodies to measure for job '{0}', so no stock is shown.", job.Name);
                return null;
            }

            Bounds? model = ModelExtent.OfBodies(bodies, frame);

            if (model == null)
            {
                _log.Debug("Could not measure the model extent for job '{0}'.", job.Name);
                return null;
            }

            Bounds stock = job.Stock.ComputeBounds(model.Value);

            if (stock.IsEmpty)
            {
                // Fixed-size stock left at its defaults, most likely. Drawing a box with
                // no thickness puts a yellow plane through the part, which reads as a
                // bug rather than as "you have not set a size yet".
                return null;
            }

            Vec3[] corners = BoxMesh.Corners(stock, frame.ToPart);

            // Edges before faces so the producer's own order is opaque-then-transparent,
            // which is the order the renderer draws in anyway. Keeping the two in step
            // means the list reads the way the picture is built.
            return new[]
            {
                new RenderBatch(
                    PrimitiveKind.Lines, BoxMesh.Edges(corners), EdgeColour, lineWidth: EdgeWidth),
                new RenderBatch(
                    PrimitiveKind.Triangles, BoxMesh.Triangles(corners), FaceColour),
            };
        }
    }
}
