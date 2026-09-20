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
        /// The contour in the order the cutter walks it, which
        /// <see cref="ResolvedContour.Reversed"/> alone decides.
        /// </summary>
        /// <remarks>
        /// <b>Travel is Reverse's; the side is climb's.</b> HSMWorks' split, and the one
        /// that keeps each control doing one thing: Reverse turns the arrow round, and
        /// climb/conventional moves the cutter across the line without touching the
        /// arrow. Both still flip the side, because with travel fixed the side is what
        /// climb means.
        /// </remarks>
        public static Polyline Walked(ResolvedContour profile)
        {
            if (profile.Path.IsClosed)
            {
                return profile.Path.WithDirection(!profile.Reversed);
            }

            return profile.Reversed ? profile.Path.Reversed() : profile.Path;
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
            Polyline walked = Walked(profile);

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
            // way the path runs. So the sign carries outside-or-inside, and the walk is
            // left free to carry the direction of travel - the two would otherwise fight
            // over one property.
            //
            // Outside for a climb cut run counter-clockwise, and for a conventional one
            // run clockwise: climb on the outside of a boss goes counter-clockwise, and
            // climb on the inside of a pocket goes clockwise.
            bool outside = climb != profile.Reversed;
            double outwards = outside ? distance : -distance;

            IReadOnlyList<Polyline> offset = offsetter.Offset(walked, outwards, arcTolerance);

            if (offset.Count == 0)
            {
                return null;
            }

            // A pinched shape can offset into several. The longest is the one that is
            // recognisably the profile; the rest are slivers left by the pinch.
            return offset
                .OrderByDescending(p => p.Length)
                .First()
                .WithDirection(!profile.Reversed);
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
        public static bool WalksBackwards(ResolvedContour profile) =>
            profile.Path.IsClosed
                ? profile.Path.IsCounterClockwise == profile.Reversed
                : profile.Reversed;
    }
}
