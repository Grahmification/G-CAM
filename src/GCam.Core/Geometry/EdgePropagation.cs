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
    /// **A branch stops the walk**, a branch being two edges a junction cannot choose
    /// between under the rule that matched - see <see cref="Continuation"/>, which also
    /// says why tangency beats the level rule rather than competing with it.
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
        /// The one edge the walk may step onto at this junction, or -1 when the junction
        /// cannot answer.
        /// </summary>
        /// <remarks>
        /// <b>Tangency wins where both rules match.</b> A junction commonly has one edge
        /// running smoothly on and another that merely happens to lie at the same height -
        /// the end edge of a fillet crossing the face is the usual one - and treating that
        /// as a branch stopped the walk dead at the very corners it exists to get round.
        /// The tangent continuation is the more specific answer, so it is taken whenever
        /// there is exactly one of it.
        ///
        /// A branch is therefore an ambiguity *within* the rule that won: two tangent
        /// continuations, or - with nothing tangent - two edges at the level. Neither has
        /// an answer the user could have meant, and guessing produces a contour nobody
        /// picked.
        /// </remarks>
        private static int Continuation(
            int edge,
            bool atEnd,
            IEdgeTopology topology,
            bool tangent,
            bool alongZ,
            double level)
        {
            int tangentMatch = -1;
            int tangentCount = 0;
            int levelMatch = -1;
            int levelCount = 0;

            foreach (int candidate in topology.Joining(edge, atEnd))
            {
                if (candidate == edge)
                {
                    continue;
                }

                bool isTangent = tangent && topology.AreTangent(edge, candidate);
                bool atLevel = alongZ && topology.LiesAt(candidate, level);

                if (isTangent)
                {
                    tangentCount++;
                    tangentMatch = candidate;
                }
                else if (atLevel)
                {
                    levelCount++;
                    levelMatch = candidate;
                }
            }

            if (tangentCount > 0)
            {
                return tangentCount == 1 ? tangentMatch : -1;
            }

            return levelCount == 1 ? levelMatch : -1;
        }

        private static bool Near(Vec3 a, Vec3 b) => (a - b).Length <= JunctionTolerance;
    }
}
