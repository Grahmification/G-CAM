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
    /// Shows what is selected in the 3D view: a job's stock as a translucent box, its
    /// coordinate system as a triad of arrows at the origin, and an operation's toolpath.
    /// </summary>
    /// <remarks>
    /// The first thing G-CAM drew, and the shape every later one follows: measure the
    /// model, ask Core what the answer looks like, hand the result to a layer. Nothing
    /// here knows about OpenGL and nothing in Core knows about SOLIDWORKS.
    ///
    /// <b>What is drawn is decided in Core</b>, by <see cref="PreviewSelection"/> - which
    /// job shows stock, which operations show a toolpath. This says how each of those
    /// looks and what it costs to measure, which is why the work is grouped by job: the
    /// expensive half is resolving the coordinate system and measuring the model along its
    /// axes, and everything drawn for that job wants exactly it.
    ///
    /// <b>Everything is built in job coordinates and drawn in part coordinates.</b> Stock
    /// offsets mean "above the top of the model" and "on all four sides" in the job's
    /// own frame, so on a rotated coordinate system the box is rotated with it and the
    /// triad turns to match. That is the whole reason the extent is measured along the
    /// job's axes rather than taken from a part-aligned bounding box.
    ///
    /// <b>A layer each, not one between them</b>, so that a stock box that cannot be
    /// computed still leaves the origin on screen - which is the half a user is more likely
    /// to be checking when the stock is wrong - and so that two selected operations can be
    /// drawn without either knowing about the other.
    ///
    /// One per document, living as long as the document's G-CAM tab.
    /// </remarks>
    public sealed class JobPreview : IJobPreview
    {
        /// <summary>
        /// The scene layers this owns, one set per job it is showing. Named rather than
        /// positional so toolpaths and the simulated tool can sit beside them without any
        /// of them having to know about the others.
        /// </summary>
        private const string StockLayerPrefix = "stock:";

        private const string OriginLayerPrefix = "job-origin:";

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

        /// <summary>
        /// The layers currently on screen, so the ones the next selection does not want
        /// can be taken down.
        /// </summary>
        /// <remarks>
        /// Tracked rather than derived from the model: an operation that has just been
        /// deleted still has a layer on screen, and nothing left in the document can name
        /// it.
        /// </remarks>
        private readonly HashSet<string> _layers = new HashSet<string>(StringComparer.Ordinal);

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
        ///
        /// A job that cannot be measured costs only its own layers. With several jobs
        /// selected, one of them naming bodies that have since gone is no reason to take
        /// the others off the screen.
        /// </remarks>
        public void Show(PreviewSelection selection)
        {
            try
            {
                var wanted = new List<string>();

                foreach (PreviewedJob job in (selection ?? PreviewSelection.Empty).Jobs)
                {
                    Build(job, wanted);
                }

                RemoveAllExcept(wanted);
            }
            catch (Exception ex)
            {
                _errors.Handle(ex, nameof(Show), quiet: true);

                // Whatever went wrong, do not leave half a preview of something we can no
                // longer describe standing on screen.
                Clear();
            }
        }

        private void Clear() => RemoveAllExcept(new List<string>());

        /// <summary>
        /// Takes down every layer this preview has put up that the selection just stated
        /// does not want.
        /// </summary>
        private void RemoveAllExcept(List<string> wanted)
        {
            var keep = new HashSet<string>(wanted, StringComparer.Ordinal);

            foreach (string layer in _layers)
            {
                if (!keep.Contains(layer))
                {
                    _viewport.Scene.Remove(layer);
                }
            }

            _layers.Clear();
            _layers.UnionWith(keep);
        }

        /// <summary>
        /// Measures one job once and sets everything selected inside it from that.
        /// </summary>
        /// <remarks>
        /// The measurement is shared deliberately: resolving the coordinate system and
        /// finding the model extent is the expensive half, and the stock box, the triad
        /// and every toolpath all need exactly it.
        /// </remarks>
        private void Build(PreviewedJob previewed, List<string> wanted)
        {
            Job job = previewed?.Job;

            if (job == null)
            {
                return;
            }

            try
            {
                JobFrame frame = CoordinateSystems.Resolve(_swApp, _model, job.CoordinateSystem);

                List<Body2> bodies = JobSelections.SolidBodies(_model, job.ModelBodies);

                if (bodies.Count == 0)
                {
                    // A part with no solid bodies, or a job naming only bodies that have
                    // since gone. Neither is worth a warning on every repaint.
                    _log.Debug("No bodies to measure for job '{0}', so nothing is shown.", job.Name);
                    return;
                }

                Bounds? extent = ModelExtent.OfBodies(bodies, frame);

                if (extent == null)
                {
                    _log.Debug("Could not measure the model extent for job '{0}'.", job.Name);
                    return;
                }

                Bounds model = extent.Value;
                Bounds stock = job.Stock.ComputeBounds(model);

                if (previewed.ShowStock)
                {
                    SetStock(job, stock, frame, wanted);
                }

                SetOrigin(job, stock, model, frame, wanted);
                SetToolpaths(previewed, frame, wanted);
            }
            catch (Exception ex)
            {
                // One job's measurement failing is not a reason to lose the others.
                _errors.Handle(ex, nameof(Build) + ":" + job.Name, quiet: true);
            }
        }

        private void SetStock(Job job, Bounds stock, JobFrame frame, List<string> wanted)
        {
            if (stock.IsEmpty)
            {
                // Fixed-size stock left at its defaults, most likely. Drawing a box with
                // no thickness puts a yellow plane through the part, which reads as a bug
                // rather than as "you have not set a size yet".
                return;
            }

            Vec3[] corners = BoxMesh.Corners(stock, frame.ToPart);

            // Edges before faces so the producer's own order is opaque-then-transparent,
            // which is the order the renderer draws in anyway. Keeping the two in step
            // means the list reads the way the picture is built.
            Set(StockLayerPrefix + Key(job), new[]
            {
                new RenderBatch(
                    PrimitiveKind.Lines, BoxMesh.Edges(corners), EdgeColour, lineWidth: EdgeWidth),
                new RenderBatch(
                    PrimitiveKind.Triangles, BoxMesh.Triangles(corners), FaceColour),
            }, wanted);
        }

        /// <summary>
        /// Puts the triad at the job's origin, sized against the stock where there is
        /// usable stock and against the model where there is not.
        /// </summary>
        /// <remarks>
        /// Falling back to the model matters more than it looks. A job whose stock has
        /// not been set up yet is exactly when someone is checking that the origin is in
        /// the right place, and sizing off an empty box would give a triad of length
        /// zero - which is to say, no triad at the moment it is most wanted. The same goes
        /// for a selected operation, whose job's stock is not being drawn at all.
        /// </remarks>
        private void SetOrigin(Job job, Bounds stock, Bounds model, JobFrame frame, List<string> wanted)
        {
            Vec3 against = stock.IsEmpty ? model.Size : stock.Size;

            double largest = Math.Max(against.X, Math.Max(against.Y, against.Z));
            double length = largest * TriadFraction;

            if (length <= Precision.Epsilon)
            {
                return;
            }

            Set(OriginLayerPrefix + Key(job), AxisTriad.Build(frame.ToPart, length), wanted);
        }

        /// <summary>
        /// Draws the toolpaths of the operations that are selected, one layer each.
        /// </summary>
        /// <remarks>
        /// A layer per operation, so one can be shown or hidden without touching the
        /// others - the naming convention lives in <see cref="ToolpathMesh.LayerName"/>.
        ///
        /// A stale path is drawn faded rather than hidden: it is still the only picture of
        /// what the machine last did, and drawing it at full strength would claim it
        /// matches the current parameters. A suppressed operation is not drawn at all, even
        /// when it is the operation the user has clicked, because it will not be cut - the
        /// greyed-out row in the tree is what says so.
        /// </remarks>
        private void SetToolpaths(PreviewedJob previewed, JobFrame frame, List<string> wanted)
        {
            foreach (Operation operation in previewed.Operations)
            {
                if (operation?.Toolpath == null || !operation.Enabled)
                {
                    continue;
                }

                // Into part coordinates, exactly as the stock box is. The toolpath is
                // computed in the job's frame; RenderBatch promises the part's.
                IReadOnlyList<RenderBatch> batches = ToolpathMesh.Build(
                    operation.Toolpath,
                    frame.ToPart,
                    stale: operation.State == OperationState.Stale);

                if (batches.Count == 0)
                {
                    continue;
                }

                Set(ToolpathMesh.LayerName(operation.Id), batches, wanted);
            }
        }

        /// <summary>
        /// Sets a layer and records that it is up, so the next selection can take it down
        /// again.
        /// </summary>
        private void Set(string layer, IEnumerable<RenderBatch> batches, List<string> wanted)
        {
            _viewport.Scene.Set(layer, batches);
            wanted.Add(layer);
        }

        /// <summary>
        /// What a job's layers are named after.
        /// </summary>
        /// <remarks>
        /// The id, so that a layer survives a rename and so that the same job keeps its
        /// place in the scene rather than flickering out and back in. A job with no id has
        /// not been through persistence yet - the Job page's working clone is the one that
        /// turns up here - and its name is unique within the document, which is enough.
        /// </remarks>
        private static string Key(Job job) =>
            string.IsNullOrEmpty(job.Id) ? job.Name ?? string.Empty : job.Id;
    }
}
