using System;
using System.Collections.Generic;
using System.Linq;
using GCam.Core.Abstractions;
using GCam.Core.Diagnostics;
using GCam.Core.Geometry.Offset;
using GCam.Core.Geometry.Primitives;
using GCam.Core.Model;
using GCam.Core.Rendering;
using GCam.Core.Strategies;
using GCam.Core.Strategies.Contour2d;
using GCam.SolidWorks.Extraction;
using SolidWorks.Interop.sldworks;

namespace GCam.SolidWorks.Rendering
{
    /// <summary>
    /// Draws what an operation is about to cut: each contour highlighted along its own
    /// length, with an arrow beside it on the side the cutter will run and pointing the
    /// way it will travel.
    /// </summary>
    /// <remarks>
    /// What the Geometry tab of the Operation page shows while it is open. The question it
    /// answers - "which side of this edge is it going to cut?" - is the one that is
    /// otherwise unanswerable until the toolpath has been generated and looked at, by
    /// which time the page has been accepted.
    ///
    /// <b>The side comes from the same offset the strategy cuts</b>, through
    /// <see cref="Contour2dCutSide"/>. Nothing here re-derives it, and nothing here knows
    /// the rule.
    ///
    /// <b>The highlight follows the chained contour, not the selection.</b> SOLIDWORKS
    /// already lights up what was picked; what it cannot show is what those picks chained
    /// into, which is the thing that gets cut. They differ whenever an edge is reached by
    /// tangent propagation rather than by being clicked.
    ///
    /// One layer, replaced on every change, in the same shape as <see cref="JobPreview"/>
    /// - a producer states everything it wants drawn and the layer name keeps it out of
    /// everyone else's way.
    ///
    /// Contour2d only. Another strategy's geometry means something else, and an arrow
    /// borrowed from this one would be a guess; a page editing one gets no arrows until
    /// somebody decides what they should say.
    /// </remarks>
    public sealed class CutDirectionPreview : ICutDirectionPreview
    {
        /// <summary>The one scene layer this owns.</summary>
        public const string Layer = "cut-direction";

        /// <summary>
        /// How finely edges are tessellated for this, millimetres.
        /// </summary>
        /// <remarks>
        /// A screen tolerance, not a machining one - the same reasoning as
        /// <see cref="ToolpathMesh.DefaultArcTolerance"/>. All that is taken from the
        /// tessellation is a point half way along and the direction there, so the
        /// operation's own tolerance would buy nothing but vertices.
        /// </remarks>
        private const double ChordTolerance = 0.05;

        /// <summary>How wide a highlighted edge is drawn, pixels.</summary>
        /// <remarks>
        /// Wide enough to read as a highlight over the edge SOLIDWORKS has already
        /// thickened for being selected, and narrow enough to stay inside the line widths
        /// a smoothed GL line is reliably given. Pixels, so it does not change with zoom -
        /// which is most of why a flat line beats the swept tube it is imitating.
        /// </remarks>
        private const double HighlightWidth = 4.0;

        /// <summary>
        /// Dark blue: this reads poorly against a shaded grey part in anything lighter,
        /// which is what the first orange attempt was.
        /// </summary>
        /// <remarks>
        /// One colour for the arrow and the highlight, so they read as one annotation
        /// rather than two things that happen to be on at once.
        ///
        /// Darker than the blue <see cref="ToolpathMesh"/> cuts in, because the two do
        /// share a screen: editing an operation that has already generated leaves its
        /// toolpath up, and this says what is going to happen rather than what has been
        /// computed.
        /// </remarks>
        private static readonly RenderColour HighlightColour = new RenderColour(0.05, 0.12, 0.5);

        private readonly SldWorks _swApp;
        private readonly ModelDoc2 _model;
        private readonly IViewportRenderer _viewport;
        private readonly ErrorHandler _errors;
        private readonly IGCamLog _log;
        private readonly IContourOffsetter _offsetter = new Clipper2Offsetter();

        public CutDirectionPreview(
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
        /// Entry point 10, by way of the Operation page's change callbacks.
        /// </summary>
        /// <remarks>
        /// Quiet on failure, and it has to be: this runs on every pick in the selection
        /// box, and a dialog would land on top of the page that caused it. An arrow is
        /// decoration - the operation is still perfectly editable without one.
        /// </remarks>
        public void Show(Job job, Operation operation)
        {
            try
            {
                var settings = operation?.Settings as Contour2dSettings;

                if (job == null || settings == null || settings.Contours.Count == 0)
                {
                    Clear();
                    return;
                }

                JobFrame frame = CoordinateSystems.Resolve(_swApp, _model, job.CoordinateSystem);

                IReadOnlyList<ResolvedContour> contours = ContourExtraction.Extract(
                    _model, settings.Contours, frame, ChordTolerance, _log);

                IReadOnlyList<CutSideMarker> markers =
                    Contour2dCutSide.Markers(contours, settings, _offsetter);

                // Everything comes out in the job's frame; a scene is drawn in the part's.
                // Points move, directions only turn.
                List<RenderBatch> highlights = contours
                    .Select(c => Highlight(c.Path, frame.ToPart))
                    .Where(b => b != null)
                    .ToList();

                List<ScreenArrow> arrows = markers.Select(m => new ScreenArrow(
                    frame.ToPart.Transform(m.Anchor),
                    frame.ToPart.TransformDirection(m.Travel),
                    frame.ToPart.TransformDirection(m.Side),
                    HighlightColour)).ToList();

                if (highlights.Count == 0 && arrows.Count == 0)
                {
                    Clear();
                    return;
                }

                // Both, in one call: a layer states everything it wants drawn. A contour
                // whose side could not be measured still gets its edge highlighted, which
                // is why these are counted separately rather than together.
                _viewport.Scene.Set(Layer, highlights, arrows);
            }
            catch (Exception ex)
            {
                _errors.Handle(ex, nameof(Show), quiet: true);

                // Better nothing than arrows describing a state we can no longer work out.
                Clear();
            }
        }

        /// <summary>
        /// One contour drawn along its own length, in part coordinates.
        /// </summary>
        /// <remarks>
        /// <b>Always on top, like the arrow.</b> Not for emphasis - the highlight sits
        /// exactly on a model edge, so depth testing it against the part it lies on is
        /// z-fighting by construction, and the line would break up into stipple as the
        /// view moved. Turning depth off removes the question rather than tuning an
        /// offset, and it also means the far side of a profile stays visible, which is
        /// what someone checking a selection wants.
        /// </remarks>
        private static RenderBatch Highlight(Polyline path, Matrix4 toPart)
        {
            if (path == null || path.IsEmpty)
            {
                return null;
            }

            List<Vec3> points = path.Points.Select(toPart.Transform).ToList();

            // A closed contour does not repeat its first point and a line strip does not
            // close itself, so the last segment has to be asked for.
            if (path.IsClosed)
            {
                points.Add(points[0]);
            }

            return new RenderBatch(
                PrimitiveKind.LineStrip,
                points,
                HighlightColour,
                lineWidth: HighlightWidth,
                alwaysOnTop: true);
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
    }
}
