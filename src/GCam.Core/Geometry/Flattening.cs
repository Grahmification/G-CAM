using System;
using System.Collections.Generic;
using System.Linq;
using GCam.Core.Geometry.Primitives;

namespace GCam.Core.Geometry
{
    /// <summary>
    /// Projects a chain onto a single Z, which is what 2D work does to anything that is
    /// not already flat, and says which Z that is.
    /// </summary>
    /// <remarks>
    /// A 2D strategy cuts every chain at one depth, and a height measured from the
    /// contour needs that depth to be a single number - so a chain that runs up or down
    /// a 3D edge is squashed flat rather than cut along its XY with its Z ignored, which
    /// is what the two amount to anyway.
    ///
    /// **Whatever collapses is dropped.** A riser projects to a single point, which
    /// would leave a zero-length segment in the middle of the chain for the offsetter to
    /// trip over; points that land on their predecessor are merged instead.
    /// </remarks>
    public static class Flattening
    {
        /// <summary>
        /// The Z each chain is cut at in 2D: the highest point of the chain, or of any
        /// group one of its pieces belongs to.
        /// </summary>
        /// <remarks>
        /// Highest, because that is what HSMWorks does, and it is judged on the whole
        /// chain - so edges reached by propagation count as much as the one clicked.
        ///
        /// **A group rises together.** Pieces sharing a group - every edge of one picked
        /// face - take the highest Z of all of them, so a hole in a sloped face is cut at
        /// the same depth as the face's outer boundary rather than at its own top.
        ///
        /// The highest *tessellated* point, so on a curve whose top lies mid-span it is
        /// within the chord tolerance of the curve's own.
        /// </remarks>
        /// <param name="chains">Chains from <see cref="Chaining.ChainWithSources"/>.</param>
        /// <param name="pieces">The segments those chains were built from.</param>
        /// <param name="groups">
        /// Lined up with <paramref name="pieces"/>; null for a piece that stands alone.
        /// </param>
        public static IReadOnlyList<double> Levels(
            IReadOnlyList<Chaining.Chain> chains,
            IReadOnlyList<Polyline> pieces,
            IReadOnlyList<int?> groups)
        {
            var groupTops = new Dictionary<int, double>();

            for (int i = 0; i < pieces.Count && i < groups.Count; i++)
            {
                if (groups[i] is int group && pieces[i] != null && !pieces[i].IsEmpty)
                {
                    double top = Top(pieces[i]);
                    groupTops[group] = groupTops.TryGetValue(group, out double other) ? Math.Max(top, other) : top;
                }
            }

            return chains.Select(chain =>
            {
                double level = Top(chain.Path);

                foreach (int i in chain.Sources)
                {
                    if (i < groups.Count && groups[i] is int group
                        && groupTops.TryGetValue(group, out double top))
                    {
                        level = Math.Max(level, top);
                    }
                }

                return level;
            }).ToList();
        }

        private static double Top(Polyline path) => path.Points.Max(p => p.Z);

        /// <summary>
        /// True when every point of the chain is within <paramref name="tolerance"/> of
        /// <paramref name="z"/>.
        /// </summary>
        public static bool LiesAt(Polyline path, double z, double tolerance)
        {
            return path != null && path.Points.All(p => Math.Abs(p.Z - z) <= tolerance);
        }

        /// <summary>
        /// The chain projected onto <paramref name="z"/>, or null when that leaves
        /// nothing of it - which is what happens to a vertical edge.
        /// </summary>
        /// <remarks>
        /// Points closer than the chaining tolerance to the one before are merged, keeping
        /// the chain's own ends where they were: the ends are what the next piece chains
        /// on to, and moving one by even that much would be a gap.
        /// </remarks>
        public static Polyline Onto(Polyline path, double z, double tolerance = Chaining.DefaultTolerance)
        {
            if (path == null || path.IsEmpty)
            {
                return path;
            }

            var points = new List<Vec3>(path.Count);

            foreach (Vec3 p in path.Points)
            {
                var flat = new Vec3(p.X, p.Y, z);

                if (points.Count == 0 || (flat - points[points.Count - 1]).Length > tolerance)
                {
                    points.Add(flat);
                }
            }

            // The last point is an end, so it wins over whichever interior point it was
            // merged into.
            Vec3 last = path.Points[path.Count - 1];
            points[points.Count - 1] = new Vec3(last.X, last.Y, z);

            if (path.IsClosed && points.Count > 1 && (points[0] - points[points.Count - 1]).Length <= tolerance)
            {
                // A closed chain does not repeat its first point.
                points.RemoveAt(points.Count - 1);
            }

            var flattened = new Polyline(points, path.IsClosed);

            return flattened.IsEmpty || flattened.Length <= tolerance ? null : flattened;
        }
    }
}
