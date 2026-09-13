using System;
using System.Collections.Generic;
using System.Linq;
using GCam.Core;
using GCam.Core.Diagnostics;
using GCam.Core.Geometry;
using GCam.Core.Geometry.Primitives;
using GCam.Core.Model;
using GCam.Core.Strategies.Shared;
using GCam.SolidWorks.Selection;
using SolidWorks.Interop.sldworks;

namespace GCam.SolidWorks.Extraction
{
    /// <summary>
    /// Turns an operation's selected edges and faces into closed contours a strategy can
    /// cut.
    /// </summary>
    /// <remarks>
    /// The bridge between what the user picked and what the toolpath code works in:
    /// resolve the references, tessellate whatever curves they turn out to be, convert
    /// metres to millimetres, carry everything into the operation's frame, and chain the
    /// pieces into loops.
    ///
    /// **Chaining itself is in Core** (<see cref="Chaining"/>) because it is arithmetic
    /// with a right answer. Only the resolving and the tessellating are here.
    ///
    /// **Everything comes out in the job's frame.** SOLIDWORKS gives part coordinates in
    /// metres; a strategy is promised millimetres in the operation's frame, so the
    /// conversion happens once, here, at the edge.
    /// </remarks>
    internal static class ContourExtraction
    {
        /// <summary>
        /// Filters out tessellated segments shorter than this, metres. Long enough to drop
        /// the slivers a tessellator leaves at a tangency, short enough to keep any real
        /// feature.
        /// </summary>
        private const double LengthToleranceMetres = 1e-6;

        public static IReadOnlyList<Polyline> Extract(
            ModelDoc2 model,
            IEnumerable<ContourSelection> selections,
            JobFrame frame,
            double chordToleranceMillimetres,
            IGCamLog log = null)
        {
            log = log ?? NullLog.Instance;

            var pieces = new List<Polyline>();

            if (model == null || selections == null)
            {
                return pieces;
            }

            foreach (Edge edge in EdgesOf(model, selections, log))
            {
                Polyline piece = Tessellate(edge, frame, chordToleranceMillimetres);

                if (piece != null && !piece.IsEmpty)
                {
                    pieces.Add(piece);
                }
            }

            IReadOnlyList<Polyline> chains = Chaining.ChainIntoLoops(pieces);

            // An open chain is a profile that does not close - a partial selection, or a
            // gap the chaining tolerance could not bridge. The contour strategy cuts
            // closed profiles only, so say which rather than cutting something wrong.
            foreach (Polyline open in chains.Where(c => !c.IsClosed))
            {
                log.Warn(
                    "A selected contour does not close ({0} points); it will not be cut.",
                    open.Count);
            }

            return chains.Where(c => c.IsClosed).ToList();
        }

        /// <summary>
        /// Every edge the selections amount to, with tangent propagation applied.
        /// </summary>
        private static IEnumerable<Edge> EdgesOf(
            ModelDoc2 model, IEnumerable<ContourSelection> selections, IGCamLog log)
        {
            // Reference equality: the CLR hands out one runtime callable wrapper per COM
            // identity, so the same edge reached twice is the same object - the same
            // guarantee JobTreeTabs relies on to key tabs by document.
            var seen = new HashSet<Edge>();

            foreach (ContourSelection selection in selections)
            {
                if (selection == null || selection.IsEmpty)
                {
                    continue;
                }

                object entity = PersistentRefs.Resolve(model, selection.Entity?.PersistentId);

                if (entity == null)
                {
                    log.Warn(
                        "The selected {0} is no longer in the model and has been skipped.",
                        selection.Entity);
                    continue;
                }

                foreach (Edge edge in EdgesFrom(entity, selection, log))
                {
                    if (edge != null && seen.Add(edge))
                    {
                        yield return edge;
                    }
                }
            }
        }

        private static IEnumerable<Edge> EdgesFrom(
            object entity, ContourSelection selection, IGCamLog log)
        {
            var face = entity as Face2;
            if (face != null)
            {
                // A face stands for its boundary, which is what makes "select the floor of
                // a pocket" mean "cut round it".
                var edges = face.GetEdges() as object[];

                foreach (object item in edges ?? new object[0])
                {
                    yield return item as Edge;
                }

                yield break;
            }

            var edge = entity as Edge;
            if (edge == null)
            {
                log.Warn("A selected entity is neither an edge nor a face, and was skipped.");
                yield break;
            }

            yield return edge;

            if (!selection.PropagateTangent)
            {
                yield break;
            }

            // Picking one edge of a filleted profile and cutting only that edge is never
            // what anybody meant.
            var tangent = edge.GetTangentEdges() as object[];

            foreach (object item in tangent ?? new object[0])
            {
                yield return item as Edge;
            }
        }

        /// <summary>
        /// One edge as a chain of points, in millimetres, in the job's frame.
        /// </summary>
        private static Polyline Tessellate(Edge edge, JobFrame frame, double chordToleranceMillimetres)
        {
            var curve = edge.GetCurve() as Curve;

            if (curve == null)
            {
                return null;
            }

            // GetCurveParams3 needs GetCurve to have been called first - the help says so,
            // and the call above is what satisfies it.
            CurveParamData parameters = edge.GetCurveParams3();

            if (parameters == null)
            {
                return null;
            }

            double chordToleranceMetres = Math.Max(
                Units.MillimetresToMetres(chordToleranceMillimetres), 1e-8);

            var points = curve.GetTessPts(
                chordToleranceMetres,
                LengthToleranceMetres,
                parameters.StartPoint,
                parameters.EndPoint) as double[];

            if (points == null || points.Length < 6)
            {
                return null;
            }

            var chain = new List<Vec3>(points.Length / 3);

            for (int i = 0; i + 2 < points.Length; i += 3)
            {
                // Metres to millimetres, then part coordinates to the job's frame. Both
                // conversions happen exactly once, here.
                var inPart = new Vec3(
                    Units.MetresToMillimetres(points[i]),
                    Units.MetresToMillimetres(points[i + 1]),
                    Units.MetresToMillimetres(points[i + 2]));

                chain.Add(frame.ToJob.Transform(inPart));
            }

            return new Polyline(chain);
        }
    }
}
