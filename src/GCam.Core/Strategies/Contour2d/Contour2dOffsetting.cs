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
        /// Closed contours run counter-clockwise for a climb cut and clockwise otherwise;
        /// an open one is walked as picked. <see cref="ResolvedContour.Reversed"/> flips
        /// either, which is how the user chooses the side.
        /// </remarks>
        public static Polyline Walked(ResolvedContour profile, bool climb)
        {
            return profile.Path.IsClosed
                ? profile.Path.WithDirection(CounterClockwise(profile, climb))
                : profile.Reversed ? profile.Path.Reversed() : profile.Path;
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

                return offsetter.OffsetOpen(walked, distance, side, arcTolerance)
                    .OrderByDescending(p => p.Length)
                    .FirstOrDefault();
            }

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
                : profile.Reversed;

        private static bool CounterClockwise(ResolvedContour profile, bool climb) =>
            climb != profile.Reversed;
    }
}
