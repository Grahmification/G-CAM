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
    /// Draws an arrow beside each of an operation's contours, on the side the cutter will
    /// run and pointing the way it will travel.
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

        /// <summary>
        /// Dark blue: the arrows read poorly against a shaded grey part in anything
        /// lighter, which is what the first orange attempt was.
        /// </summary>
        /// <remarks>
        /// Darker than the blue <see cref="ToolpathMesh"/> cuts in, because the two do
        /// share a screen: editing an operation that has already generated leaves its
        /// toolpath up, and these arrows say what is going to happen rather than what has
        /// been computed.
        /// </remarks>
        private static readonly RenderColour ArrowColour = new RenderColour(0.05, 0.12, 0.5);

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

                if (markers.Count == 0)
                {
                    Clear();
                    return;
                }

                // Markers come out in the job's frame; a scene is drawn in the part's. The
                // anchor moves, the directions only turn.
                _viewport.Scene.SetArrows(Layer, markers.Select(m => new ScreenArrow(
                    frame.ToPart.Transform(m.Anchor),
                    frame.ToPart.TransformDirection(m.Travel),
                    frame.ToPart.TransformDirection(m.Side),
                    ArrowColour)));
            }
            catch (Exception ex)
            {
                _errors.Handle(ex, nameof(Show), quiet: true);

                // Better nothing than arrows describing a state we can no longer work out.
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
    }
}
