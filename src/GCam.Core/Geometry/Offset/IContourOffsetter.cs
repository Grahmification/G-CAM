using System.Collections.Generic;
using GCam.Core.Geometry.Primitives;

namespace GCam.Core.Geometry.Offset
{
    /// <summary>
    /// Moves a contour sideways by a distance - what puts a cutter beside a wall rather
    /// than on it.
    /// </summary>
    /// <remarks>
    /// Behind an interface because offsetting is the one piece of 2D geometry worth
    /// delegating to a library, and because the choice of library should not reach the
    /// strategies. See <see cref="Clipper2Offsetter"/>.
    ///
    /// **Offsetting is not "move every segment sideways".** Where the distance exceeds the
    /// local curvature the naive result crosses itself, and cutting the self-intersecting
    /// part gouges the job. Anything implementing this has to remove those, which is why a
    /// single contour can come back as several, or as none at all.
    ///
    /// Works in XY at a single Z. Three-axis contouring only ever offsets in plan.
    ///
    /// **Clipping a path against a region is here too**, although it is not an offset: it
    /// is the other half of what lets several contours' cutter paths merge where they meet
    /// (see <c>Contour2dMerging</c>), and it is the same library doing it.
    /// </remarks>
    public interface IContourOffsetter
    {
        /// <summary>
        /// The contour moved sideways by <paramref name="distance"/>.
        /// </summary>
        /// <param name="contour">A closed contour. Millimetres.</param>
        /// <param name="distance">
        /// How far, in millimetres. Positive grows the enclosed region and negative
        /// shrinks it, <b>whichever way round the contour runs</b> - so a positive
        /// distance always puts the cutter outside, and orienting the contour first
        /// changes only the direction of travel.
        ///
        /// That was documented the other way about until 2026-09-19, and it is worth
        /// being exact: believing the side followed the orientation is what put
        /// climb/conventional in charge of which side of an open edge was cut.
        /// </param>
        /// <param name="arcTolerance">
        /// How far the rounded outside corners may deviate from a true arc, millimetres.
        /// </param>
        /// <returns>
        /// What is left after self-intersections are removed: usually one contour,
        /// sometimes several where a shape pinches in two, and none where the offset
        /// swallows the shape entirely. An empty result is an answer, not a failure.
        ///
        /// Outlines run counter-clockwise and holes clockwise, whichever way round the
        /// contour ran - so the result is a region <see cref="Outside"/> can take.
        /// </returns>
        IReadOnlyList<Polyline> Offset(Polyline contour, double distance, double arcTolerance);

        /// <summary>
        /// One side of an <b>open</b> path, moved sideways by <paramref name="distance"/>.
        /// </summary>
        /// <remarks>
        /// A separate method rather than a flag on <see cref="Offset"/>, because the two
        /// are not the same operation. Offsetting a closed contour is driven by the
        /// sign of the distance and can legitimately return several contours or none. Offsetting an
        /// open path is driven by an explicit side and returns a single parallel curve -
        /// and no general-purpose offsetting library provides it directly, because the
        /// usual meaning of "offset an open path" is the closed ribbon around it.
        /// </remarks>
        /// <param name="path">An open path. Millimetres.</param>
        /// <param name="distance">
        /// How far, in millimetres. Always positive: which side is asked for explicitly,
        /// so there is no sign to get backwards.
        /// </param>
        /// <param name="side">Which side of the path, looking along it.</param>
        /// <param name="arcTolerance">
        /// How far the rounded outside corners may deviate from a true arc, millimetres.
        /// </param>
        /// <returns>
        /// The parallel curve, or empty when the offset leaves nothing of that side -
        /// which a distance larger than the path's own features can legitimately do.
        /// </returns>
        IReadOnlyList<Polyline> OffsetOpen(
            Polyline path, double distance, OffsetSide side, double arcTolerance);

        /// <summary>
        /// The band <paramref name="halfWidth"/> either side of a path: everywhere a
        /// cutter centre would put the cutter across it.
        /// </summary>
        /// <remarks>
        /// **Round across the ends of an open path.** A selected edge does not end in air:
        /// it ends at a corner of the part, where the wall carries on round, and a cutter
        /// centred just past the end cuts that corner. The band was square across the
        /// ends until 2026-09-26, and the facing edges of two bosses closer together than
        /// the cutter were cut wherever one overhung the other. A closed path gives a
        /// ring.
        /// </remarks>
        /// <returns>
        /// A region, as closed outlines: holes run the other way round from the outlines
        /// they are in, which is what <see cref="Outside"/> expects.
        /// </returns>
        IReadOnlyList<Polyline> Band(Polyline path, double halfWidth, double arcTolerance);

        /// <summary>
        /// What is left of a path after removing whatever lies inside a region.
        /// </summary>
        /// <param name="path">Open or closed.</param>
        /// <param name="region">
        /// Closed outlines, filled non-zero - so overlapping outlines are one region, and a
        /// hole is an outline running the other way round inside another, as
        /// <see cref="Offset"/> and <see cref="Band"/> return them.
        /// </param>
        /// <returns>
        /// The pieces left, each running the same way as <paramref name="path"/>. The path
        /// itself, unchanged, when the region takes nothing from it; empty when it takes
        /// everything.
        /// </returns>
        IReadOnlyList<Polyline> Outside(Polyline path, IReadOnlyList<Polyline> region);
    }
}
