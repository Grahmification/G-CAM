using System;
using System.Collections.Generic;
using GCam.Core.Geometry.Primitives;

namespace GCam.Core.Geometry
{
    /// <summary>
    /// How far a selection runs from the edge that was actually picked.
    /// </summary>
    /// <remarks>
    /// Two modifiers, HSMWorks' pair, evaluated together at every junction the walk
    /// reaches:
    ///
    /// - **Tangential propagation** follows edges that run smoothly on from this one, and
    ///   only *forwards* - the way the cut-direction arrow points. It may climb or
    ///   descend, which is why the caller flattens what comes back.
    /// - **Propagate along Z** follows any joining edge lying flat at the picked edge's
    ///   own Z, and does so from both ends.
    ///
    /// With both on there is one walk and not two: a junction is crossed when *either*
    /// rule accepts the edge on the far side, so tangency carries on past a level corner
    /// and vice versa. The second flag is also what opens the backward end, so tangential
    /// propagation runs both ways when it is on.
    ///
    /// **A branch stops the walk.** Where two edges at a junction both qualify there is no
    /// answer to which the user meant, and guessing produces a contour nobody picked.
    /// </remarks>
    public static class EdgePropagation
    {
        /// <summary>Ends closer than this are the same point, mm.</summary>
        private const double JunctionTolerance = Chaining.DefaultTolerance;

        /// <summary>
        /// The picked edge and everything the modifiers reach from it, in walk order.
        /// </summary>
        /// <param name="forwards">
        /// Which end the tangential walk leaves from - the end of the edge as the arrow
        /// points, which <c>ContourSelection.Reversed</c> flips.
        /// </param>
        public static IReadOnlyList<int> Walk(
            int seed,
            IEdgeTopology topology,
            bool tangent,
            bool alongZ,
            bool forwards = true)
        {
            if (topology == null)
            {
                throw new ArgumentNullException(nameof(topology));
            }

            var walked = new List<int> { seed };

            if (!tangent && !alongZ)
            {
                return walked;
            }

            var visited = new HashSet<int> { seed };

            // The level everything is measured against is the pick's own, taken from its
            // start point so that an edge which is not flat still has one.
            double level = topology.StartOf(seed).Z;

            Follow(seed, forwards, topology, tangent, alongZ, level, walked, visited);

            if (alongZ)
            {
                Follow(seed, !forwards, topology, tangent, alongZ, level, walked, visited);
            }

            return walked;
        }

        private static void Follow(
            int seed,
            bool atEnd,
            IEdgeTopology topology,
            bool tangent,
            bool alongZ,
            double level,
            List<int> walked,
            HashSet<int> visited)
        {
            int current = seed;
            bool end = atEnd;

            while (true)
            {
                Vec3 junction = end ? topology.EndOf(current) : topology.StartOf(current);

                int next = Continuation(current, end, topology, tangent, alongZ, level);

                if (next < 0 || !visited.Add(next))
                {
                    // Nothing fits, the junction branches, or the chain has closed on
                    // itself. All three are the end of this walk.
                    return;
                }

                walked.Add(next);

                // Carry on out of the far end of what was just stepped onto.
                end = Near(topology.StartOf(next), junction);
                current = next;
            }
        }

        /// <summary>
        /// The one edge the walk may step onto at this junction, or -1 when there is no
        /// such edge or more than one.
        /// </summary>
        private static int Continuation(
            int edge,
            bool atEnd,
            IEdgeTopology topology,
            bool tangent,
            bool alongZ,
            double level)
        {
            int found = -1;

            foreach (int candidate in topology.Joining(edge, atEnd))
            {
                if (candidate == edge)
                {
                    continue;
                }

                bool accepted = (tangent && topology.AreTangent(edge, candidate))
                                || (alongZ && topology.LiesAt(candidate, level));

                if (!accepted)
                {
                    continue;
                }

                if (found >= 0)
                {
                    return -1;
                }

                found = candidate;
            }

            return found;
        }

        private static bool Near(Vec3 a, Vec3 b) => (a - b).Length <= JunctionTolerance;
    }
}
