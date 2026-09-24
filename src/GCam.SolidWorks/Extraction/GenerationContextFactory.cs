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

            var heightContext = HeightContext.From(stock, model, SelectionHeights(operation, frame));

            if (!operation.Heights.IsContourRelative)
            {
                ResolvedHeights heights = ResolveHeights(operation, heightContext);

                var plain = new GenerationContext(
                    job, operation, tool, heights, stock, ExtractContours(operation, frame));

                // A retract below the feed height is lifted rather than refused, and said so.
                string correction = OperationHeights.DescribeCorrections(heights);

                if (correction != null)
                {
                    plain.Warnings.Add(correction);
                }

                return plain;
            }

            IReadOnlyList<ResolvedContour> contours = ExtractContours(operation, frame);

            // Cutting heights that follow the contour are resolved once per contour. The
            // operation's own answer is then the first contour's: clearance, retract and
            // feed are the same for all of them, and those are what it is read for.
            var warnings = new List<string>();

            contours = ContourHeights.Resolve(
                operation.Heights, heightContext, contours, warnings, out string failure);

            if (contours.Count == 0)
            {
                throw new GCamUserException($"'{operation.Name}' cannot be generated: " + failure);
            }

            var context = new GenerationContext(
                job, operation, tool, contours[0].Heights, stock, contours);

            foreach (string warning in warnings)
            {
                context.Warnings.Add(warning);
            }

            return context;
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

        private static ResolvedHeights ResolveHeights(Operation operation, HeightContext context)
        {
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
        private Dictionary<string, double> SelectionHeights(Operation operation, JobFrame frame) =>
            EntityHeights.ForOperation(_model, operation.Heights, frame);

        private IReadOnlyList<ResolvedContour> ExtractContours(Operation operation, JobFrame frame)
        {
            if (!(operation.Settings is IContourSelectionOwner owner))
            {
                return new ResolvedContour[0];
            }

            IReadOnlyList<ResolvedContour> contours = ContourExtraction.Extract(
                _model, owner.Contours, frame, operation.Tolerance, _log);

            // Open profiles cut now, so the only way to select something and get nothing
            // is for the selections themselves to have gone - which is worth saying
            // plainly, because the operation still names geometry it can no longer find.
            if (contours.Count == 0 && owner.Contours.Count > 0)
            {
                throw new GCamUserException(
                    $"None of '{operation.Name}''s selected contours could be found in the model.");
            }

            return contours;
        }
    }
}
