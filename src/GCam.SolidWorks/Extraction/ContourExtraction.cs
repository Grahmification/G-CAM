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
using SolidWorks.Interop.swconst;

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
        /// How far a tessellation may be from the length of the curve it came from before
        /// it is reported, as a fraction.
        /// </summary>
        private const double LengthAgreement = 0.1;

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
        /// <param name="flatten">
        /// Whether a propagated chain is projected onto the Z of the edge it was picked
        /// from. True for 2D work, where a chain that has run up a 3D edge still has to be
        /// cut at one depth; a 3D strategy would take the edges where they actually lie.
        /// </param>
        public static IReadOnlyList<ResolvedContour> Extract(
            ModelDoc2 model,
            IEnumerable<ContourSelection> selections,
            JobFrame frame,
            double chordToleranceMillimetres,
            IGCamLog log = null,
            bool flatten = true)
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
            var topology = new ModelEdgeTopology(frame);

            foreach (OwnedEdge owned in EdgesOf(model, selections, topology, log))
            {
                // The log is what lets Tessellate report geometry that came back wrong.
                Polyline piece = Tessellate(owned.Edge, frame, chordToleranceMillimetres, log);

                if (flatten && owned.Level.HasValue)
                {
                    piece = Flattened(piece, owned.Level.Value);
                }

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
            public OwnedEdge(Edge edge, ContourSelection owner, double? level)
            {
                Edge = edge;
                Owner = owner;
                Level = level;
            }

            public Edge Edge { get; }

            public ContourSelection Owner { get; }

            /// <summary>The Z of the picked edge, when this one is to be flattened onto it.</summary>
            public double? Level { get; }
        }

        /// <summary>
        /// Every edge the selections amount to, with propagation applied.
        /// </summary>
        private static IEnumerable<OwnedEdge> EdgesOf(
            ModelDoc2 model,
            IEnumerable<ContourSelection> selections,
            ModelEdgeTopology topology,
            IGCamLog log)
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

                double? level = LevelOf(entity as Edge, selection, topology);

                foreach (Edge edge in EdgesFrom(entity, selection, topology, log))
                {
                    // An edge reached through two picks belongs to the first: it is one
                    // piece of one chain, and it cannot be walked two ways at once.
                    if (edge != null && seen.Add(edge))
                    {
                        yield return new OwnedEdge(edge, selection, level);
                    }
                }
            }
        }

        /// <summary>
        /// The Z a pick's chain is flattened onto, or null when nothing propagates from it
        /// and so nothing can have left that level.
        /// </summary>
        private static double? LevelOf(
            Edge picked, ContourSelection selection, ModelEdgeTopology topology)
        {
            if (picked == null || !(selection.PropagateTangent || selection.PropagateAlongZ))
            {
                return null;
            }

            return topology.StartOf(topology.Index(picked)).Z;
        }

        private static IEnumerable<Edge> EdgesFrom(
            object entity, ContourSelection selection, ModelEdgeTopology topology, IGCamLog log)
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

            // The walk itself is in Core; this only says which edge it starts from and
            // which way round. Reverse turns the arrow round, so it turns the walk round
            // with it.
            IReadOnlyList<int> walked = EdgePropagation.Walk(
                topology.Index(edge),
                topology,
                selection.PropagateTangent,
                selection.PropagateAlongZ,
                forwards: !selection.Reversed);

            foreach (int index in walked)
            {
                yield return topology.At(index);
            }
        }

        /// <summary>
        /// One piece of a chain projected onto the level it was picked at, or null when
        /// that leaves nothing of it - which is what happens to a vertical edge the walk
        /// ran up.
        /// </summary>
        private static Polyline Flattened(Polyline piece, double z)
        {
            if (piece == null || piece.IsEmpty)
            {
                return piece;
            }

            var flat = new Polyline(
                piece.Points.Select(p => new Vec3(p.X, p.Y, z)).ToList(), piece.IsClosed);

            return flat.Length > Chaining.DefaultTolerance ? flat : null;
        }

        /// <summary>
        /// One edge as a chain of points, in millimetres, in the job's frame.
        /// </summary>
        /// <remarks>
        /// Internal rather than private because <see cref="EntityHeights"/> needs the same
        /// thing for a different question - whether an edge lies at one Z. Tessellating an
        /// edge into the job's frame is worth having once.
        /// </remarks>
        internal static Polyline Tessellate(
            Edge edge, JobFrame frame, double chordToleranceMillimetres, IGCamLog log = null)
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

            // **The points go in the edge's own direction, which is not always the
            // curve's.** `Sense` is false when the two run opposite ways, and the help is
            // explicit about what that means: StartPoint is then the *end* of the edge.
            // Handing those to GetTessPts unswapped asks it to travel from the end of the
            // edge to its start, and on an arc the only way to do that is the long way
            // round the circle - so a 1mm fillet came back as the 4.4mm arc that is not
            // there, folded back across the corner. A line has no long way round, which is
            // why only fillets ever showed it. Verified on 2025 SP3.
            object from = parameters.Sense ? parameters.StartPoint : parameters.EndPoint;
            object to = parameters.Sense ? parameters.EndPoint : parameters.StartPoint;

            var points = curve.GetTessPts(
                chordToleranceMetres, LengthToleranceMetres, from, to) as double[];

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

            var tessellated = new Polyline(chain);

            WarnIfNotTheLengthOfTheEdge(tessellated, curve, parameters, log);

            return tessellated;
        }

        /// <summary>
        /// Says so when the points that came back are not the length the edge actually is.
        /// </summary>
        /// <remarks>
        /// One COM call to catch the whole family of faults that the <c>Sense</c> bug
        /// belonged to: geometry that is quietly wrong reads exactly like geometry that is
        /// right, and cost an afternoon to find by eye. <c>GetLength3</c> measures the
        /// curve itself between the edge's own parameters, so it is independent of
        /// whatever the tessellation decided to do.
        ///
        /// Generous, because a tessellation is *meant* to be shorter than its curve: the
        /// chords cut every corner, by more the coarser the tolerance. Only a difference
        /// no tolerance explains - the wrong arc is several hundred percent - trips it.
        /// </remarks>
        private static void WarnIfNotTheLengthOfTheEdge(
            Polyline tessellated, Curve curve, CurveParamData parameters, IGCamLog log)
        {
            if (log == null)
            {
                return;
            }

            double expected = Units.MetresToMillimetres(
                curve.GetLength3(parameters.UMinValue, parameters.UMaxValue));

            if (expected <= Precision.Epsilon
                || Math.Abs(tessellated.Length - expected) <= expected * LengthAgreement)
            {
                return;
            }

            log.Warn(
                "A {0} edge tessellated to {1:0.###}mm where the curve itself is {2:0.###}mm long. " +
                "The toolpath will follow the points, so it will be wrong.",
                (swCurveTypes_e)parameters.CurveType,
                tessellated.Length,
                expected);
        }
    }
}
