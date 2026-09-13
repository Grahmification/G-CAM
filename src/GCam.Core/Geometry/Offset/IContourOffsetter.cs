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
    /// </remarks>
    public interface IContourOffsetter
    {
        /// <summary>
        /// The contour moved sideways by <paramref name="distance"/>.
        /// </summary>
        /// <param name="contour">A closed contour. Millimetres.</param>
        /// <param name="distance">
        /// How far, in millimetres. Positive grows the contour, negative shrinks it - so
        /// which sign puts the cutter outside depends on which way the contour runs, and
        /// the caller decides that by orienting the contour first.
        /// </param>
        /// <param name="arcTolerance">
        /// How far the rounded outside corners may deviate from a true arc, millimetres.
        /// </param>
        /// <returns>
        /// What is left after self-intersections are removed: usually one contour,
        /// sometimes several where a shape pinches in two, and none where the offset
        /// swallows the shape entirely. An empty result is an answer, not a failure.
        /// </returns>
        IReadOnlyList<Polyline> Offset(Polyline contour, double distance, double arcTolerance);
    }
}
