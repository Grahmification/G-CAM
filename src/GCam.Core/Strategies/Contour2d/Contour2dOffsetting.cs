using System.Collections.Generic;
using System.Linq;
using GCam.Core.Geometry.Offset;
using GCam.Core.Geometry.Primitives;

namespace GCam.Core.Strategies.Contour2d
{
    /// <summary>
    /// Which way round a contour is walked, and which side of it the cutter runs on.
    /// </summary>
    /// <remarks>
    /// Extracted so that <see cref="Contour2dStrategy"/>, which cuts the offset, and
    /// <see cref="Contour2dCutSide"/>, which draws an arrow saying where the cut will be,
    /// cannot disagree. An arrow pointing at the wrong side of an edge is worse than no
    /// arrow at all - it would be believed - and the rule is subtle enough that a second
    /// copy of it would drift: a closed contour carries its side in its orientation and an
    /// open one names the side outright, so the two are not even the same shape of answer.
    ///
    /// Nothing here decides *how far*; that is the caller's, and it is the one thing the
    /// strategy and the preview legitimately differ on.
    /// </remarks>
    internal static class Contour2dOffsetting
    {
        /// <summary>
        /// The contour in the order the cutter walks it.
        /// </summary>
        /// <remarks>
        /// <b>Climb and conventional differ only in direction of travel.</b> The cutter
        /// stays on the side <see cref="ResolvedContour.Reversed"/> puts it on - outside a
        /// closed profile or inside it, one hand or the other of an open one - and the two
        /// cut directions walk that same side opposite ways round. That is what the words
        /// mean on a machine: with the cutter on a given side, reversing the feed is
        /// exactly what turns a climb cut into a conventional one.
        ///
        /// A closed profile is cut on the outside - see <see cref="Offset"/> - and runs
        /// counter-clockwise to climb, clockwise to cut conventionally. <c>Reversed</c>
        /// turns it round as well; for a closed contour that is all it can do today, which
        /// is the "inside profiles" gap in docs/design/operations.md.
        /// </remarks>
        public static Polyline Walked(ResolvedContour profile, bool climb)
        {
            if (profile.Path.IsClosed)
            {
                return profile.Path.WithDirection(CounterClockwise(profile, climb));
            }

            // Same rule, expressed against the path as picked: the cutter keeps its hand
            // and the walk turns round. Reading it the other way - fixing the walk and
            // flipping the hand - is what made conventional cut the far side of an edge.
            return CounterClockwise(profile, climb) ? profile.Path : profile.Path.Reversed();
        }

        /// <summary>
        /// The walked contour moved sideways onto the cutter's side of it, or null when
        /// the offset leaves nothing of that side - which a distance larger than the
        /// contour's own features can legitimately do.
        /// </summary>
        public static Polyline Offset(
            ResolvedContour profile,
            bool climb,
            double distance,
            IContourOffsetter offsetter,
            double arcTolerance)
        {
            Polyline walked = Walked(profile, climb);

            if (!profile.Path.IsClosed)
            {
                // No inside, so orientation says nothing and the side is named outright.
                // Climb puts the material on the left of travel - a cutter turning
                // clockwise seen from above then has its edge moving with the feed at the
                // point of contact, which is what climb means - so the cutter centre goes
                // to the right.
                OffsetSide side = climb ? OffsetSide.Right : OffsetSide.Left;

                // A closed contour takes a negative distance directly - it shrinks - but
                // an open path has no area to shrink, and IContourOffsetter says so: its
                // distance is always positive and the side is named. So the sign is read
                // here, where it means what it means, and the cutter crosses over.
                if (distance < 0)
                {
                    side = side == OffsetSide.Right ? OffsetSide.Left : OffsetSide.Right;
                    distance = -distance;
                }

                return offsetter.OffsetOpen(walked, distance, side, arcTolerance)
                    .OrderByDescending(p => p.Length)
                    .FirstOrDefault();
            }

            // **Orientation does not decide the side here, the sign does.** Measured, not
            // assumed: Clipper grows the enclosed region for a positive distance whichever
            // way the path runs, so a closed profile is cut outside and the direction of
            // travel is free to be whatever climb asks for. That is why this branch needs
            // no counterpart to the flip the open one does.
            IReadOnlyList<Polyline> offset = offsetter.Offset(walked, distance, arcTolerance);

            if (offset.Count == 0)
            {
                return null;
            }

            // A pinched shape can offset into several. The longest is the one that is
            // recognisably the profile; the rest are slivers left by the pinch.
            return offset
                .OrderByDescending(p => p.Length)
                .First()
                .WithDirection(CounterClockwise(profile, climb));
        }

        /// <summary>
        /// True when <see cref="Walked"/> runs the opposite way round from the contour as
        /// it was resolved.
        /// </summary>
        /// <remarks>
        /// So that something can be anchored to the contour itself and still know which
        /// way the cut goes. Re-orienting first and then picking a point off the result
        /// means the point moves when the direction changes, which for a symmetrical
        /// profile puts it on the far side of the part.
        /// </remarks>
        public static bool WalksBackwards(ResolvedContour profile, bool climb) =>
            profile.Path.IsClosed
                ? profile.Path.IsCounterClockwise != CounterClockwise(profile, climb)
                : !CounterClockwise(profile, climb);

        private static bool CounterClockwise(ResolvedContour profile, bool climb) =>
            climb != profile.Reversed;
    }
}
