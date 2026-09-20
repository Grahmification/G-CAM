using System.Collections.Generic;
using GCam.Core.Geometry.Offset;
using GCam.Core.Geometry.Primitives;
using GCam.Core.Strategies.Shared;

namespace GCam.Core.Strategies.Contour2d
{
    /// <summary>
    /// Where one contour will be cut: a point beside it, the way the cutter travels, and
    /// which side of it the cutter is on.
    /// </summary>
    /// <remarks>
    /// Directions are unit vectors in the job's frame, in the contour's own plane.
    /// Nothing here has a size: how big the arrow drawn from this ends up is a question
    /// about the screen, not about the cut. See
    /// <see cref="GCam.Core.Rendering.ScreenArrow"/>.
    /// </remarks>
    public sealed class CutSideMarker
    {
        public CutSideMarker(Vec3 anchor, Vec3 travel, Vec3 side)
        {
            Anchor = anchor;
            Travel = travel;
            Side = side;
        }

        /// <summary>A point on the contour itself, millimetres in the job's frame.</summary>
        public Vec3 Anchor { get; }

        /// <summary>Unit vector along the cut at <see cref="Anchor"/>.</summary>
        public Vec3 Travel { get; }

        /// <summary>Unit vector from <see cref="Anchor"/> towards the cutter.</summary>
        public Vec3 Side { get; }

        public override string ToString() => $"at {Anchor} along {Travel}, cutter {Side}";
    }

    /// <summary>
    /// Works out where a 2D contour will be cut, for showing it before it is generated.
    /// </summary>
    /// <remarks>
    /// <b>The side is measured from a real offset, never re-derived.</b> It would be
    /// shorter to reason "climb means the cutter is on the right of travel" and compute a
    /// perpendicular - and it would be wrong for half the cases, because a closed contour
    /// takes its side from its orientation and an open one from an explicit hand. So this
    /// offsets the contour by a probe distance through exactly the call the strategy makes
    /// (<see cref="Contour2dOffsetting"/>) and reads the side off the result. If the two
    /// ever disagree the arrow is not the thing that is wrong.
    ///
    /// The probe is small and fixed, because it is only being used to find a direction.
    /// The cutter's real offset is not known here anyway - an operation with no tool yet
    /// still has a side.
    /// </remarks>
    public static class Contour2dCutSide
    {
        /// <summary>
        /// How far the contour is offset to find out which way the cutter lies, mm.
        /// </summary>
        /// <remarks>
        /// Two orders above Clipper2's fixed-point grid, so the direction is not read off
        /// rounding noise, and small enough that it does not collapse any feature big
        /// enough to cut. A contour that does vanish at this distance simply gets no
        /// arrow - see <see cref="Marker"/>.
        /// </remarks>
        private const double ProbeDistance = 0.05;

        /// <summary>Matches <c>Contour2dStrategy.ArcTolerance</c>.</summary>
        private const double ArcTolerance = 0.01;

        /// <summary>
        /// One marker per contour that can be read, in the order they were given.
        /// </summary>
        /// <remarks>
        /// A contour that cannot be read is left out rather than guessed at. Silence is
        /// the honest answer for a profile so small that a 0.05mm offset swallows it, and
        /// the alternative - an arrow on a side nobody checked - is exactly what this is
        /// meant to prevent.
        /// </remarks>
        public static IReadOnlyList<CutSideMarker> Markers(
            IEnumerable<ResolvedContour> contours,
            Contour2dSettings settings,
            IContourOffsetter offsetter)
        {
            var markers = new List<CutSideMarker>();

            if (contours == null || settings == null || offsetter == null)
            {
                return markers;
            }

            bool climb = settings.Direction == CutDirection.Climb;

            foreach (ResolvedContour contour in contours)
            {
                CutSideMarker marker = Marker(contour, climb, offsetter);

                if (marker != null)
                {
                    markers.Add(marker);
                }
            }

            return markers;
        }

        private static CutSideMarker Marker(
            ResolvedContour profile, bool climb, IContourOffsetter offsetter)
        {
            if (profile?.Path == null || profile.Path.IsEmpty)
            {
                return null;
            }

            Vec3 anchor;
            Vec3 travel;

            // Anchored to the contour as picked, not to the re-oriented walk, so the
            // arrow stays on the edge it is about. Only Reverse turns it round.
            if (!LongestSegment(profile.Path, out anchor, out travel))
            {
                return null;
            }

            if (Contour2dOffsetting.WalksBackwards(profile))
            {
                travel = travel * -1;
            }

            Polyline offset = Contour2dOffsetting.Offset(
                profile, climb, ProbeDistance, offsetter, ArcTolerance);

            if (offset == null || offset.IsEmpty)
            {
                return null;
            }

            // The offset runs parallel to the contour a probe away, so its nearest point
            // to a mid-segment anchor is that anchor pushed straight onto the cutter's
            // side. Which is the whole measurement.
            Vec3 towards = offset.NearestPointXy(anchor) - anchor;
            var inPlane = new Vec3(towards.X, towards.Y, 0);

            if (inPlane.Length <= Precision.Epsilon)
            {
                return null;
            }

            return new CutSideMarker(anchor, travel, inPlane.Normalised());
        }

        /// <summary>
        /// The middle of the contour's longest straight run, and the direction of travel
        /// along it.
        /// </summary>
        /// <remarks>
        /// <b>Mid-segment, never on a corner.</b> The obvious choice - half way along the
        /// contour - lands exactly on a corner for a rectangle, which is the commonest
        /// thing anyone selects, and a corner is the one place the offset is not parallel
        /// to the contour. The side read there comes back diagonal. The longest segment's
        /// midpoint cannot do that, and on a straight-edged profile it is also the
        /// roomiest place to hang an arrow.
        ///
        /// Tessellation only subdivides curves, so on a polygonal profile this is the
        /// middle of its longest edge. On a circle every segment is the same length and
        /// the first wins - arbitrary, but stable, which is what matters for something
        /// redrawn on every pick. Stability is also why the caller passes the contour as
        /// picked rather than as walked: on a rectangle the two long edges tie, so the
        /// winner would otherwise swap ends every time the direction changed.
        /// </remarks>
        private static bool LongestSegment(Polyline path, out Vec3 point, out Vec3 travel)
        {
            point = Vec3.Zero;
            travel = Vec3.Zero;

            double longest = 0;

            for (int i = 0; i < path.SegmentCount; i++)
            {
                Vec3 from = path[i];
                Vec3 step = path.EndOfSegment(i) - from;
                double length = step.Length;

                if (length <= longest || length <= Precision.Epsilon)
                {
                    continue;
                }

                longest = length;
                point = from + (step * 0.5);
                travel = step.Normalised();
            }

            return longest > Precision.Epsilon;
        }
    }
}
