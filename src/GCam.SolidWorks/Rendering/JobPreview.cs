using System;
using System.Collections.Generic;
using GCam.Core;
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
    /// Shows the selected job in the 3D view: its stock as a translucent box, and its
    /// coordinate system as a triad of arrows at the origin.
    /// </summary>
    /// <remarks>
    /// The first thing G-CAM draws, and the shape every later one follows: measure the
    /// model, ask Core what the answer looks like, hand the result to a layer. Nothing
    /// here knows about OpenGL and nothing in Core knows about SOLIDWORKS.
    ///
    /// <b>Both are built in job coordinates and drawn in part coordinates.</b> Stock
    /// offsets mean "above the top of the model" and "on all four sides" in the job's
    /// own frame, so on a rotated coordinate system the box is rotated with it and the
    /// triad turns to match. That is the whole reason the extent is measured along the
    /// job's axes rather than taken from a part-aligned bounding box.
    ///
    /// <b>Two layers, not one</b>, so that a stock box that cannot be computed still
    /// leaves the origin on screen - which is the half a user is more likely to be
    /// checking when the stock is wrong.
    ///
    /// One per document, living as long as the document's G-CAM tab.
    /// </remarks>
    public sealed class JobPreview : IJobPreview
    {
        /// <summary>
        /// The scene layers this owns. Named rather than positional so toolpaths and the
        /// simulated tool can sit beside them without any of them having to know about
        /// the others.
        /// </summary>
        private const string StockLayer = "stock";

        private const string OriginLayer = "job-origin";

        // Yellow, because it reads as raw material against SOLIDWORKS' grey model and
        // against every part colour a user is likely to have chosen. A quarter alpha is
        // enough to see the box without losing the model inside it; the edges are nearly
        // opaque so the extent stays legible where the walls are nearly edge-on.
        private static readonly RenderColour FaceColour = new RenderColour(1.0, 0.83, 0.10, 0.22);
        private static readonly RenderColour EdgeColour = new RenderColour(0.85, 0.65, 0.00, 0.95);

        private const double EdgeWidth = 1.5;

        /// <summary>
        /// Triad length, as a fraction of the largest dimension of what it is sizing
        /// against.
        /// </summary>
        /// <remarks>
        /// Proportional rather than a fixed number of millimetres, so the triad is
        /// legible on a 20mm part and on a two-metre one without anybody configuring it.
        /// The cost is that it is not screen-constant the way HSMWorks' is: zoom far
        /// enough out and it shrinks with everything else. Making it screen-constant
        /// would mean rebuilding the geometry on every view change and a scene per window
        /// rather than per document - see the note in docs/design/jobs.md.
        /// </remarks>
        private const double TriadFraction = 0.30;

        private readonly SldWorks _swApp;
        private readonly ModelDoc2 _model;
        private readonly IViewportRenderer _viewport;
        private readonly ErrorHandler _errors;
        private readonly IGCamLog _log;

        public JobPreview(
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
                if (job == null)
                {
                    Clear();
                    return;
                }

                Build(job);
            }
            catch (Exception ex)
            {
                _errors.Handle(ex, nameof(ShowJob), quiet: true);

                // Whatever went wrong, do not leave half a preview of a job we can no
                // longer describe standing on screen.
                Clear();
            }
        }

        private void Clear()
        {
            _viewport.Scene.Remove(StockLayer);
            _viewport.Scene.Remove(OriginLayer);
        }

        /// <summary>
        /// Measures the job once and sets both layers from it.
        /// </summary>
        /// <remarks>
        /// The measurement is shared deliberately: resolving the coordinate system and
        /// finding the model extent is the expensive half, and the stock box and the
        /// triad both need exactly it.
        /// </remarks>
        private void Build(Job job)
        {
            JobFrame frame = CoordinateSystems.Resolve(_swApp, _model, job.CoordinateSystem);

            List<Body2> bodies = JobSelections.SolidBodies(_model, job.ModelBodies);

            if (bodies.Count == 0)
            {
                // A part with no solid bodies, or a job naming only bodies that have
                // since gone. Neither is worth a warning on every repaint.
                _log.Debug("No bodies to measure for job '{0}', so nothing is shown.", job.Name);
                Clear();
                return;
            }

            Bounds? extent = ModelExtent.OfBodies(bodies, frame);

            if (extent == null)
            {
                _log.Debug("Could not measure the model extent for job '{0}'.", job.Name);
                Clear();
                return;
            }

            Bounds model = extent.Value;
            Bounds stock = job.Stock.ComputeBounds(model);

            SetStock(stock, frame);
            SetOrigin(stock, model, frame);
        }

        private void SetStock(Bounds stock, JobFrame frame)
        {
            if (stock.IsEmpty)
            {
                // Fixed-size stock left at its defaults, most likely. Drawing a box with
                // no thickness puts a yellow plane through the part, which reads as a bug
                // rather than as "you have not set a size yet".
                _viewport.Scene.Remove(StockLayer);
                return;
            }

            Vec3[] corners = BoxMesh.Corners(stock, frame.ToPart);

            // Edges before faces so the producer's own order is opaque-then-transparent,
            // which is the order the renderer draws in anyway. Keeping the two in step
            // means the list reads the way the picture is built.
            _viewport.Scene.Set(StockLayer, new[]
            {
                new RenderBatch(
                    PrimitiveKind.Lines, BoxMesh.Edges(corners), EdgeColour, lineWidth: EdgeWidth),
                new RenderBatch(
                    PrimitiveKind.Triangles, BoxMesh.Triangles(corners), FaceColour),
            });
        }

        /// <summary>
        /// Puts the triad at the job's origin, sized against the stock where there is
        /// usable stock and against the model where there is not.
        /// </summary>
        /// <remarks>
        /// Falling back to the model matters more than it looks. A job whose stock has
        /// not been set up yet is exactly when someone is checking that the origin is in
        /// the right place, and sizing off an empty box would give a triad of length
        /// zero - which is to say, no triad at the moment it is most wanted.
        /// </remarks>
        private void SetOrigin(Bounds stock, Bounds model, JobFrame frame)
        {
            Vec3 against = stock.IsEmpty ? model.Size : stock.Size;

            double largest = Math.Max(against.X, Math.Max(against.Y, against.Z));
            double length = largest * TriadFraction;

            if (length <= Precision.Epsilon)
            {
                _viewport.Scene.Remove(OriginLayer);
                return;
            }

            _viewport.Scene.Set(OriginLayer, AxisTriad.Build(frame.ToPart, length));
        }
    }
}
