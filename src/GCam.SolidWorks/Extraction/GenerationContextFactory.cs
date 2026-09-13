using System;
using System.Collections.Generic;
using GCam.Core.Diagnostics;
using GCam.Core.Generation;
using GCam.Core.Geometry.Primitives;
using GCam.Core.Model;
using GCam.Core.Model.Heights;
using GCam.Core.Strategies;
using GCam.Core.Strategies.Shared;
using GCam.Core.Tooling;
using GCam.SolidWorks.Selection;
using SolidWorks.Interop.sldworks;

namespace GCam.SolidWorks.Extraction
{
    /// <summary>
    /// Resolves everything an operation needs before a strategy can run.
    /// </summary>
    /// <remarks>
    /// The only part of generation that touches SOLIDWORKS. Everything it produces is
    /// plain numbers in millimetres in the operation's frame, which is what lets the
    /// strategies be pure and testable.
    ///
    /// **Every failure here is a <see cref="GCamUserException"/>**, because every one of
    /// them is something the user can fix: choose a tool, reselect a face that has gone,
    /// give the stock a size. The queue turns them into a failed operation carrying the
    /// message and moves on to the next.
    /// </remarks>
    public sealed class GenerationContextFactory : IGenerationContextFactory
    {
        private readonly SldWorks _swApp;
        private readonly ModelDoc2 _model;
        private readonly JobDocument _jobs;
        private readonly IGCamLog _log;

        public GenerationContextFactory(
            SldWorks swApp, ModelDoc2 model, JobDocument jobs, IGCamLog log)
        {
            _swApp = swApp ?? throw new ArgumentNullException(nameof(swApp));
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
            _log = log ?? NullLog.Instance;
        }

        public GenerationContext Create(Job job, Operation operation)
        {
            if (job == null || operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            Tool tool = ResolveTool(operation);
            JobFrame frame = ResolveFrame(job, operation);

            Bounds model = MeasureModel(job, frame);
            Bounds stock = job.Stock.ComputeBounds(model);

            ResolvedHeights heights = ResolveHeights(operation, model, stock, frame);

            IReadOnlyList<Polyline> contours = ExtractContours(operation, frame);

            return new GenerationContext(job, operation, tool, heights, stock, contours);
        }

        private Tool ResolveTool(Operation operation)
        {
            if (string.IsNullOrEmpty(operation.ToolId))
            {
                throw new GCamUserException(
                    $"'{operation.Name}' has no tool. Choose one before generating.");
            }

            Tool tool = _jobs.FindTool(operation.ToolId);

            if (tool == null)
            {
                throw new GCamUserException(
                    $"The tool '{operation.Name}' uses is no longer in this part.");
            }

            return tool;
        }

        /// <summary>
        /// The frame the toolpath is computed in: the operation's own if it overrides,
        /// otherwise the job's.
        /// </summary>
        /// <remarks>
        /// The override is modelled and stored but cannot be posted on three axes, so an
        /// operation whose frame is not the job's is refused here rather than generating a
        /// path nobody can cut. Checking at generation keeps the refusal where the user is
        /// asking for the result.
        /// </remarks>
        private JobFrame ResolveFrame(Job job, Operation operation)
        {
            if (operation.Frame == null || operation.Frame.InheritFromJob)
            {
                return CoordinateSystems.Resolve(_swApp, _model, job.CoordinateSystem);
            }

            throw new GCamUserException(
                $"'{operation.Name}' uses its own coordinate system, which G-CAM cannot " +
                "post on a 3-axis machine yet. Use the job's, or put the operation in a " +
                "second job.");
        }

        private Bounds MeasureModel(Job job, JobFrame frame)
        {
            List<Body2> bodies = JobSelections.SolidBodies(_model, job.ModelBodies);

            if (bodies.Count == 0)
            {
                throw new GCamUserException(
                    $"Job '{job.Name}' has no bodies to machine. Check its model selection.");
            }

            Bounds? extent = ModelExtent.OfBodies(bodies, frame);

            if (extent == null)
            {
                throw new GCamUserException(
                    $"The extent of job '{job.Name}' could not be measured.");
            }

            return extent.Value;
        }

        private ResolvedHeights ResolveHeights(
            Operation operation, Bounds model, Bounds stock, JobFrame frame)
        {
            var context = HeightContext.From(stock, model, SelectionHeights(operation, frame));

            IReadOnlyList<string> problems = operation.Heights.Validate(context);

            if (problems.Count > 0)
            {
                throw new GCamUserException(
                    $"'{operation.Name}' cannot be generated: " + problems[0]);
            }

            operation.Heights.TryResolve(context, out ResolvedHeights heights);
            return heights;
        }

        /// <summary>
        /// The Z of every entity a height is measured from, resolved up front.
        /// </summary>
        /// <remarks>
        /// Core does pure arithmetic over a dictionary rather than calling back across the
        /// boundary, so everything a height might need is resolved before it is asked for.
        /// </remarks>
        private Dictionary<string, double> SelectionHeights(Operation operation, JobFrame frame)
        {
            var heights = new Dictionary<string, double>(StringComparer.Ordinal);

            foreach (HeightSetting height in new[]
            {
                operation.Heights.Clearance,
                operation.Heights.Retract,
                operation.Heights.Feed,
                operation.Heights.Top,
                operation.Heights.Bottom,
            })
            {
                if (height.Mode != HeightMode.FromSelection || height.Reference == null)
                {
                    continue;
                }

                string id = height.Reference.PersistentId;

                if (string.IsNullOrEmpty(id) || heights.ContainsKey(id))
                {
                    continue;
                }

                double? z = HeightOf(PersistentRefs.Resolve(_model, id), frame);

                if (z.HasValue)
                {
                    heights[id] = z.Value;
                }
            }

            return heights;
        }

        /// <summary>
        /// The height of a picked entity in the job's frame, or null if it has gone.
        /// </summary>
        /// <remarks>
        /// The top of whatever was picked, which is what "measure from this face" means
        /// for a face that is not exactly flat.
        /// </remarks>
        private double? HeightOf(object entity, JobFrame frame)
        {
            var face = entity as Face2;

            if (face != null)
            {
                var body = face.GetBody() as Body2;

                if (body != null)
                {
                    // Measuring the whole body is wrong for a face partway up it, and
                    // measuring a face needs the extreme-point probe pointed at the face
                    // rather than the body. Until that exists, refuse rather than guess.
                    return null;
                }
            }

            return null;
        }

        private IReadOnlyList<Polyline> ExtractContours(Operation operation, JobFrame frame)
        {
            if (!(operation.Settings is IContourSelectionOwner owner))
            {
                return new Polyline[0];
            }

            IReadOnlyList<Polyline> contours = ContourExtraction.Extract(
                _model, owner.Contours, frame, operation.Tolerance, _log);

            if (contours.Count == 0 && owner.Contours.Count > 0)
            {
                throw new GCamUserException(
                    $"None of '{operation.Name}''s contours form a closed profile. " +
                    "2D contouring cuts closed profiles only.");
            }

            return contours;
        }
    }
}
