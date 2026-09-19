using System;
using System.Collections.Generic;
using System.Linq;
using GCam.Core;
using GCam.Core.Geometry.Primitives;
using GCam.Core.Model.Heights;
using GCam.SolidWorks.Selection;
using SolidWorks.Interop.sldworks;

namespace GCam.SolidWorks.Extraction
{
    /// <summary>
    /// The Z of a picked entity, for a height measured from geometry rather than from the
    /// stock or the model.
    /// </summary>
    /// <remarks>
    /// <b>Only geometry that lies at a single Z has a height.</b> A flat face, a flat edge
    /// - a circle counts - or a vertex. A sloped face, a cylinder or a helix is refused,
    /// because "the height of that" has no one answer and any of the plausible ones (its
    /// top, its bottom, its centre) would be a decision the user did not make and cannot
    /// see. Refusing shows up as a height that will not resolve, which the page and the
    /// preview both already handle.
    ///
    /// Shared by generation and by the Heights tab's planes, so a plane cannot sit
    /// somewhere the cut will not.
    /// </remarks>
    internal static class EntityHeights
    {
        /// <summary>
        /// How much Z spread across a tessellated edge still counts as flat, millimetres.
        /// </summary>
        /// <remarks>
        /// A tenth of a micron. A genuinely flat edge tessellates to an exactly constant
        /// Z, so this absorbs arithmetic noise rather than sloppiness - anything a user
        /// would call sloped is orders of magnitude above it.
        /// </remarks>
        private const double FlatToleranceMillimetres = 1e-4;

        /// <summary>
        /// How far a face's normal may be off the job's Z axis, as a dot product.
        /// </summary>
        /// <remarks>
        /// About five thousandths of a degree. A face modelled flat is exact; one drafted
        /// even slightly is refused, which is the intent.
        /// </remarks>
        private const double NormalTolerance = 1e-6;

        /// <summary>Tessellation fineness for an edge, millimetres. Only Z is read.</summary>
        private const double ChordToleranceMillimetres = 0.05;

        /// <summary>
        /// The Z of every entity this operation's heights are measured from, keyed by the
        /// persistent id that names it.
        /// </summary>
        /// <remarks>
        /// Resolved up front and handed to Core as a dictionary, so
        /// <see cref="HeightSetting.TryResolve"/> stays pure arithmetic and never calls
        /// back across the boundary.
        /// </remarks>
        public static Dictionary<string, double> ForOperation(
            ModelDoc2 model, OperationHeights heights, JobFrame frame)
        {
            var found = new Dictionary<string, double>(StringComparer.Ordinal);

            if (model == null || heights == null)
            {
                return found;
            }

            foreach (HeightSetting height in All(heights))
            {
                if (height.Mode != HeightMode.FromSelection || height.Reference == null)
                {
                    continue;
                }

                string id = height.Reference.PersistentId;

                if (string.IsNullOrEmpty(id) || found.ContainsKey(id))
                {
                    continue;
                }

                double? z = Of(PersistentRefs.Resolve(model, id), frame);

                if (z.HasValue)
                {
                    found[id] = z.Value;
                }
            }

            return found;
        }

        private static IEnumerable<HeightSetting> All(OperationHeights heights)
        {
            yield return heights.Clearance;
            yield return heights.Retract;
            yield return heights.Feed;
            yield return heights.Top;
            yield return heights.Bottom;
        }

        /// <summary>
        /// The height of one entity in the job's frame, or null when it has gone or does
        /// not lie at a single Z.
        /// </summary>
        public static double? Of(object entity, JobFrame frame)
        {
            var vertex = entity as Vertex;

            if (vertex != null)
            {
                return ZOf(vertex.GetPoint() as double[], frame);
            }

            var face = entity as Face2;

            if (face != null)
            {
                return ZOfFace(face, frame);
            }

            var edge = entity as Edge;

            if (edge != null)
            {
                return ZOfEdge(edge, frame);
            }

            return null;
        }

        /// <summary>
        /// A planar face square to the job's Z, at the height of its own plane.
        /// </summary>
        /// <remarks>
        /// Read from the surface rather than measured off the face, which is both exact
        /// and the only way to tell a flat face from one that merely looks flat. The
        /// normal is checked in the <i>job's</i> frame, not the part's - on a job whose
        /// coordinate system is rotated, "flat" means square to that system.
        /// </remarks>
        private static double? ZOfFace(Face2 face, JobFrame frame)
        {
            var surface = face.GetSurface() as Surface;

            if (surface == null || !surface.IsPlane())
            {
                return null;
            }

            // Six doubles: the normal, then a point on the plane in metres.
            var plane = surface.PlaneParams as double[];

            if (plane == null || plane.Length < 6)
            {
                return null;
            }

            Vec3 normal = frame.ToJob.TransformDirection(
                new Vec3(plane[0], plane[1], plane[2]));

            if (normal.Length <= Precision.Epsilon)
            {
                return null;
            }

            if (Math.Abs(Math.Abs(normal.Normalised().Z) - 1) > NormalTolerance)
            {
                return null;
            }

            return ZOf(new[] { plane[3], plane[4], plane[5] }, frame);
        }

        /// <summary>An edge whose every point is at the same height.</summary>
        private static double? ZOfEdge(Edge edge, JobFrame frame)
        {
            Polyline path = ContourExtraction.Tessellate(edge, frame, ChordToleranceMillimetres);

            if (path == null || path.IsEmpty)
            {
                return null;
            }

            double lowest = path.Points.Min(p => p.Z);
            double highest = path.Points.Max(p => p.Z);

            return highest - lowest > FlatToleranceMillimetres ? (double?)null : (lowest + highest) / 2;
        }

        /// <summary>A point in metres in part coordinates, as a height in the job's frame.</summary>
        private static double? ZOf(double[] point, JobFrame frame)
        {
            if (point == null || point.Length < 3)
            {
                return null;
            }

            var inPart = new Vec3(
                Units.MetresToMillimetres(point[0]),
                Units.MetresToMillimetres(point[1]),
                Units.MetresToMillimetres(point[2]));

            return frame.ToJob.Transform(inPart).Z;
        }
    }
}
