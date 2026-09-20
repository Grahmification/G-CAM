using System;
using System.Collections.Generic;
using GCam.Core;
using GCam.Core.Geometry;
using GCam.Core.Geometry.Primitives;
using SolidWorks.Interop.sldworks;

namespace GCam.SolidWorks.Extraction
{
    /// <summary>
    /// Answers <see cref="IEdgeTopology"/> from the model, so the propagation walk itself
    /// stays in Core.
    /// </summary>
    /// <remarks>
    /// Edges are numbered as they are first seen, and every answer is cached: a walk asks
    /// the same questions of the same edges repeatedly, and each one is a COM call.
    ///
    /// **Ends come from the curve, not from the vertices.** They have to match the
    /// direction <see cref="ContourExtraction.Tessellate"/> produces, because that is what
    /// makes "forwards" mean the same thing to the walk and to the toolpath.
    /// </remarks>
    internal sealed class ModelEdgeTopology : IEdgeTopology
    {
        /// <summary>How far apart two Z values may be and still be one level, mm.</summary>
        /// <remarks>Matches <see cref="EntityHeights"/>'s own flatness tolerance.</remarks>
        private const double LevelTolerance = 1e-4;

        private readonly JobFrame _frame;

        private readonly List<Edge> _edges = new List<Edge>();
        private readonly Dictionary<Edge, int> _indices = new Dictionary<Edge, int>();

        private readonly Dictionary<int, Vec3[]> _ends = new Dictionary<int, Vec3[]>();
        private readonly Dictionary<int, HashSet<int>> _tangent = new Dictionary<int, HashSet<int>>();
        private readonly Dictionary<int, double?> _levels = new Dictionary<int, double?>();

        public ModelEdgeTopology(JobFrame frame)
        {
            _frame = frame;
        }

        /// <summary>The number this edge goes by, assigning one if it is new.</summary>
        /// <remarks>
        /// Keyed on the runtime callable wrapper, which COM identity makes unique per
        /// edge - the same guarantee <see cref="ContourExtraction"/> already relies on.
        /// </remarks>
        public int Index(Edge edge)
        {
            int index;

            if (!_indices.TryGetValue(edge, out index))
            {
                index = _edges.Count;
                _edges.Add(edge);
                _indices[edge] = index;
            }

            return index;
        }

        public Edge At(int index) => _edges[index];

        public Vec3 StartOf(int edge) => Ends(edge)[0];

        public Vec3 EndOf(int edge) => Ends(edge)[1];

        public IReadOnlyList<int> Joining(int edge, bool atEnd)
        {
            var found = new List<int>();
            Vertex vertex = VertexNear(_edges[edge], atEnd ? EndOf(edge) : StartOf(edge));

            if (vertex == null)
            {
                // A full circle has no vertex, so nothing joins it anywhere.
                return found;
            }

            var joined = vertex.GetEdges() as object[];

            foreach (object item in joined ?? new object[0])
            {
                var other = item as Edge;

                if (other != null)
                {
                    found.Add(Index(other));
                }
            }

            return found;
        }

        public bool AreTangent(int edge, int other) =>
            TangentTo(edge).Contains(other) || TangentTo(other).Contains(edge);

        public bool LiesAt(int edge, double z)
        {
            double? level = Level(edge);

            return level.HasValue && Math.Abs(level.Value - z) <= LevelTolerance;
        }

        /// <summary>The single Z this edge lies at, or null when it does not lie at one.</summary>
        public double? Level(int edge)
        {
            double? level;

            if (!_levels.TryGetValue(edge, out level))
            {
                level = EntityHeights.Of(_edges[edge], _frame);
                _levels[edge] = level;
            }

            return level;
        }

        private Vec3[] Ends(int edge)
        {
            Vec3[] ends;

            if (_ends.TryGetValue(edge, out ends))
            {
                return ends;
            }

            Edge model = _edges[edge];

            // GetCurveParams3 needs GetCurve to have been called first, as Tessellate's
            // own comment records.
            var curve = model.GetCurve() as Curve;
            CurveParamData parameters = curve == null ? null : model.GetCurveParams3();

            // The edge's ends, not the curve's: they are swapped when the two run opposite
            // ways, which is the same rule ContourExtraction.Tessellate follows to keep
            // the walk's idea of "forwards" and the tessellated direction the same.
            ends = parameters == null
                ? new[] { Vec3.Zero, Vec3.Zero }
                : new[]
                {
                    InJob((parameters.Sense ? parameters.StartPoint : parameters.EndPoint) as double[]),
                    InJob((parameters.Sense ? parameters.EndPoint : parameters.StartPoint) as double[]),
                };

            _ends[edge] = ends;
            return ends;
        }

        private HashSet<int> TangentTo(int edge)
        {
            HashSet<int> tangent;

            if (_tangent.TryGetValue(edge, out tangent))
            {
                return tangent;
            }

            tangent = new HashSet<int>();
            var edges = _edges[edge].GetTangentEdges() as object[];

            foreach (object item in edges ?? new object[0])
            {
                var other = item as Edge;

                if (other != null)
                {
                    tangent.Add(Index(other));
                }
            }

            _tangent[edge] = tangent;
            return tangent;
        }

        /// <summary>
        /// Whichever of an edge's two vertices sits at the given point.
        /// </summary>
        /// <remarks>
        /// Matched by position rather than taken as the start or the end, because an
        /// edge's vertices follow its own sense and <see cref="Ends"/> follows its curve's,
        /// and the two need not agree.
        /// </remarks>
        private Vertex VertexNear(Edge edge, Vec3 point)
        {
            var start = edge.GetStartVertex() as Vertex;
            var end = edge.GetEndVertex() as Vertex;

            double toStart = DistanceTo(start, point);
            double toEnd = DistanceTo(end, point);

            if (toStart <= toEnd)
            {
                return double.IsPositiveInfinity(toStart) ? null : start;
            }

            return double.IsPositiveInfinity(toEnd) ? null : end;
        }

        private double DistanceTo(Vertex vertex, Vec3 point)
        {
            if (vertex == null)
            {
                return double.PositiveInfinity;
            }

            return (InJob(vertex.GetPoint() as double[]) - point).Length;
        }

        /// <summary>A point in metres in part coordinates, in millimetres in the job's.</summary>
        private Vec3 InJob(double[] point)
        {
            if (point == null || point.Length < 3)
            {
                return Vec3.Zero;
            }

            return _frame.ToJob.Transform(new Vec3(
                Units.MetresToMillimetres(point[0]),
                Units.MetresToMillimetres(point[1]),
                Units.MetresToMillimetres(point[2])));
        }
    }
}
