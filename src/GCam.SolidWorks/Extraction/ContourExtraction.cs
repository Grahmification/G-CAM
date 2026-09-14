using System;
using System.Collections.Generic;
using System.Linq;
using GCam.Core;
using GCam.Core.Diagnostics;
using GCam.Core.Geometry;
using GCam.Core.Geometry.Primitives;
using GCam.Core.Model;
using GCam.Core.Strategies;
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

        /// <summary>
        /// The selections, tessellated and chained into contours to cut.
        /// </summary>
        /// <remarks>
        /// **Open chains come back too.** They were dropped here with a warning until
        /// 2026-09-13, because the offsetter could only work on closed contours. It can
        /// offset one side of an open path now, so a profile that does not close is a
        /// thing to cut rather than a thing to complain about - which is what HSMWorks
        /// does, and what makes a partial selection useful.
        ///
        /// Each contour carries whether it should be walked backwards, because that is how
        /// the user picks which side of it the cutter runs on. A chain has one direction,
        /// so it is reversed if **any** of the picks that built it was marked reversed -
        /// see <see cref="Chaining.ChainWithSources"/>.
        /// </remarks>
        public static IReadOnlyList<ResolvedContour> Extract(
            ModelDoc2 model,
            IEnumerable<ContourSelection> selections,
            JobFrame frame,
            double chordToleranceMillimetres,
            IGCamLog log = null)
        {
            log = log ?? NullLog.Instance;

            var contours = new List<ResolvedContour>();

            if (model == null || selections == null)
            {
                return contours;
            }

            // Kept in step by index: the nth piece came from the nth entry here, which is
            // what lets a chain be traced back to the picks that made it.
            var pieces = new List<Polyline>();
            var pieceOwners = new List<ContourSelection>();

            foreach (OwnedEdge owned in EdgesOf(model, selections, log))
            {
                Polyline piece = Tessellate(owned.Edge, frame, chordToleranceMillimetres);

                if (piece != null && !piece.IsEmpty)
                {
                    pieces.Add(piece);
                    pieceOwners.Add(owned.Owner);
                }
            }

            foreach (Chaining.Chain chain in Chaining.ChainWithSources(pieces))
            {
                bool reversed = chain.Sources.Any(
                    i => i < pieceOwners.Count && pieceOwners[i] != null && pieceOwners[i].Reversed);

                contours.Add(new ResolvedContour(chain.Path, reversed));
            }

            return contours;
        }

        /// <summary>An edge and the selection it was reached through.</summary>
        private struct OwnedEdge
        {
            public OwnedEdge(Edge edge, ContourSelection owner)
            {
                Edge = edge;
                Owner = owner;
            }

            public Edge Edge { get; }

            public ContourSelection Owner { get; }
        }

        /// <summary>
        /// Every edge the selections amount to, with tangent propagation applied.
        /// </summary>
        private static IEnumerable<OwnedEdge> EdgesOf(
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
                    // An edge reached through two picks belongs to the first: it is one
                    // piece of one chain, and it cannot be walked two ways at once.
                    if (edge != null && seen.Add(edge))
                    {
                        yield return new OwnedEdge(edge, selection);
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
