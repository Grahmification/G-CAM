using System;
using System.Collections.Generic;
using System.Linq;
using GCam.Core.Geometry.Primitives;
using GCam.Core.Model;

namespace GCam.Core.Rendering
{
    /// <summary>
    /// Turns a toolpath into drawable batches, coloured by what each move is for.
    /// </summary>
    /// <remarks>
    /// The same arrangement as <see cref="BoxMesh"/>: Core says what to draw,
    /// GCam.SolidWorks says how. Nothing here knows about OpenGL, and all of it is
    /// testable without a licence.
    ///
    /// Consecutive moves of the same kind become one <see cref="PrimitiveKind.LineStrip"/>,
    /// which is both the cheapest thing to draw and the only way to get a continuous path
    /// rather than a dotted-looking run of separate segments. Where the kind changes, the
    /// next strip starts at the same vertex the last one ended on, so there is no gap.
    ///
    /// Arcs are tessellated here rather than stored as points, to a tolerance the caller
    /// chooses - the screen needs far fewer points than a machine does.
    /// </remarks>
    public static class ToolpathMesh
    {
        /// <summary>Scene layers holding toolpaths start with this.</summary>
        /// <remarks>
        /// One layer per operation, so any one of them can be shown or hidden without
        /// touching the others. The layer name is the coordination mechanism the renderer
        /// already expects - see docs/design/jobs.md.
        /// </remarks>
        public const string LayerPrefix = "toolpath:";

        /// <summary>How far a tessellated arc may deviate from the true one, mm.</summary>
        /// <remarks>
        /// A screen tolerance, not a machining one: 0.05mm is well under a pixel at any
        /// zoom someone inspects a path at, and costs a tenth of the vertices that a
        /// machining tolerance would.
        /// </remarks>
        public const double DefaultArcTolerance = 0.05;

        /// <summary>Cutting moves are drawn heavier than the moves that just get there.</summary>
        public const double CuttingLineWidth = 2.0;

        /// <summary>How much of its colour a stale path keeps.</summary>
        public const double StaleAlpha = 0.35;

        public static string LayerName(string operationId) => LayerPrefix + operationId;

        /// <summary>
        /// What each kind of move is drawn in.
        /// </summary>
        /// <remarks>
        /// Chosen to be told apart rather than to match another product exactly: yellow
        /// rapids because that is universal, red plunges because a plunge in the wrong
        /// place is what breaks cutters, and blue leads so an entry mark can be traced to
        /// the move that made it.
        /// </remarks>
        public static RenderColour ColourFor(MoveKind kind)
        {
            switch (kind)
            {
                case MoveKind.Rapid: return new RenderColour(1.0, 0.85, 0.1);
                case MoveKind.Lead: return new RenderColour(0.2, 0.5, 1.0);
                case MoveKind.Link: return new RenderColour(0.2, 0.75, 0.8);
                case MoveKind.Plunge: return new RenderColour(0.9, 0.25, 0.2);
                case MoveKind.Retract: return new RenderColour(0.65, 0.4, 0.9);

                // Cycle draws as cutting until drilling brings its own meaning.
                case MoveKind.Cutting:
                case MoveKind.Cycle:
                default:
                    return new RenderColour(0.15, 0.75, 0.25);
            }
        }

        /// <summary>
        /// The batches for one toolpath.
        /// </summary>
        /// <param name="path">What to draw. Null or a single move yields nothing.</param>
        /// <param name="toPart">
        /// The operation's frame to the part's, applied to every vertex.
        /// <see cref="RenderBatch"/> promises part coordinates, and a toolpath is computed
        /// in the operation's frame - so on any job whose coordinate system is rotated,
        /// skipping this draws the path in the wrong plane. Required rather than optional
        /// for exactly that reason: it was forgotten once, and the result looked like a
        /// broken toolpath rather than a broken transform. Pass
        /// <see cref="Matrix4.Identity"/> when the two frames are the same.
        /// </param>
        /// <param name="showRapids">
        /// False leaves the rapids out. On a drilling job they dominate the screen, and
        /// they are the first thing someone turns off to see the cutting.
        /// </param>
        /// <param name="stale">
        /// Draws faded, for a path whose inputs have changed since it was made. Faded
        /// rather than hidden: it is still the only picture of what the machine last did,
        /// and drawing it at full strength would claim it matches the current parameters.
        /// </param>
        /// <param name="arcTolerance">Chord tolerance for tessellating arcs, mm.</param>
        public static IReadOnlyList<RenderBatch> Build(
            Toolpath path,
            Matrix4 toPart,
            bool showRapids = true,
            bool stale = false,
            double arcTolerance = DefaultArcTolerance)
        {
            var batches = new List<RenderBatch>();

            if (path == null || path.IsEmpty)
            {
                return batches;
            }

            IReadOnlyList<Move> moves = path.Moves;
            var run = new List<Vec3>();
            MoveKind runKind = moves[1].Kind;

            for (int i = 1; i < moves.Count; i++)
            {
                Move move = moves[i];
                Vec3 from = moves[i - 1].End;

                if (move.Kind != runKind && run.Count > 0)
                {
                    Emit(batches, run, runKind, toPart, stale, showRapids);

                    // The next run starts where this one stopped, or the path would show a
                    // gap at every change of kind.
                    Vec3 last = run[run.Count - 1];
                    run = new List<Vec3> { last };
                    runKind = move.Kind;
                }

                if (run.Count == 0)
                {
                    run.Add(from);
                    runKind = move.Kind;
                }

                if (move.IsArc)
                {
                    AppendArc(run, from, move.End, move.Arc, arcTolerance);
                }
                else
                {
                    run.Add(move.End);
                }
            }

            Emit(batches, run, runKind, toPart, stale, showRapids);
            return batches;
        }

        private static void Emit(
            ICollection<RenderBatch> batches,
            List<Vec3> vertices,
            MoveKind kind,
            Matrix4 toPart,
            bool stale,
            bool showRapids)
        {
            if (vertices.Count < 2 || (!showRapids && kind == MoveKind.Rapid))
            {
                return;
            }

            RenderColour colour = ColourFor(kind);
            if (stale)
            {
                colour = colour.WithAlpha(StaleAlpha);
            }

            batches.Add(new RenderBatch(
                PrimitiveKind.LineStrip,
                vertices.Select(toPart.Transform).ToArray(),
                colour,
                lineWidth: kind == MoveKind.Rapid ? RenderBatch.DefaultLineWidth : CuttingLineWidth));
        }

        /// <summary>
        /// Adds the points of an arc, excluding the one it starts from and including the
        /// one it ends on.
        /// </summary>
        /// <remarks>
        /// Rotates the start radius about the plane's normal in equal steps, interpolating
        /// the out-of-plane component so a helical ramp comes out as a helix rather than a
        /// flat circle.
        ///
        /// A degenerate arc - zero radius, or endpoints that do not sit on one - falls back
        /// to a straight line. Drawing something wrong but visible beats drawing nothing at
        /// all, because nothing looks like a gap in the path and sends someone hunting for
        /// a bug in the strategy.
        /// </remarks>
        private static void AppendArc(
            List<Vec3> into, Vec3 from, Vec3 to, ArcData arc, double tolerance)
        {
            Vec3 normal = arc.Normal.Normalised();
            Vec3 startRadius = PerpendicularPart(from - arc.Centre, normal);
            Vec3 endRadius = PerpendicularPart(to - arc.Centre, normal);

            double radius = startRadius.Length;

            if (radius <= Precision.Epsilon || endRadius.Length <= Precision.Epsilon)
            {
                into.Add(to);
                return;
            }

            double sweep = SweepAngle(startRadius, endRadius, normal, arc.Clockwise);
            int steps = StepCount(radius, sweep, tolerance);

            double alongNormalStart = (from - arc.Centre).Dot(normal);
            double alongNormalEnd = (to - arc.Centre).Dot(normal);

            for (int i = 1; i <= steps; i++)
            {
                double fraction = (double)i / steps;
                Vec3 turned = Rotate(startRadius, normal, sweep * fraction);
                double alongNormal = alongNormalStart + ((alongNormalEnd - alongNormalStart) * fraction);

                into.Add(arc.Centre + turned + (normal * alongNormal));
            }

            // Land exactly on the endpoint rather than on the last rotated approximation,
            // so the next move starts where this one is supposed to have finished.
            into[into.Count - 1] = to;
        }

        /// <summary>The part of a vector lying in the plane the normal defines.</summary>
        private static Vec3 PerpendicularPart(Vec3 v, Vec3 normal) => v - (normal * v.Dot(normal));

        /// <summary>
        /// The signed angle to turn through, always in the requested direction.
        /// </summary>
        /// <remarks>
        /// A full circle is the interesting case: start and end coincide, the raw angle is
        /// zero, and the answer wanted is a whole turn rather than standing still.
        /// </remarks>
        private static double SweepAngle(Vec3 from, Vec3 to, Vec3 normal, bool clockwise)
        {
            double angle = Math.Atan2(from.Cross(to).Dot(normal), from.Dot(to));

            // Clockwise about the normal is the negative direction.
            if (clockwise)
            {
                if (angle >= -Precision.Epsilon)
                {
                    angle -= 2 * Math.PI;
                }
            }
            else if (angle <= Precision.Epsilon)
            {
                angle += 2 * Math.PI;
            }

            return angle;
        }

        private static int StepCount(double radius, double sweep, double tolerance)
        {
            double sweptAngle = Math.Abs(sweep);

            if (tolerance <= 0 || tolerance >= radius)
            {
                return Math.Max(2, (int)Math.Ceiling(sweptAngle / (Math.PI / 8)));
            }

            // The largest step whose chord stays within tolerance of the arc.
            double maxStep = 2.0 * Math.Acos(1.0 - (tolerance / radius));
            int steps = (int)Math.Ceiling(sweptAngle / Math.Max(maxStep, 1e-6));

            return Math.Max(2, steps);
        }

        /// <summary>Rodrigues' rotation of a vector about a unit axis.</summary>
        private static Vec3 Rotate(Vec3 v, Vec3 axis, double angle)
        {
            double cos = Math.Cos(angle);
            double sin = Math.Sin(angle);

            return (v * cos)
                   + (axis.Cross(v) * sin)
                   + (axis * (axis.Dot(v) * (1 - cos)));
        }
    }
}
