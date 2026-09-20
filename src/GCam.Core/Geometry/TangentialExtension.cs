using System;
using System.Collections.Generic;
using System.Linq;
using GCam.Core.Geometry.Primitives;

namespace GCam.Core.Geometry
{
    /// <summary>
    /// Lengthens an open contour along its own tangent at both ends, or shortens it.
    /// </summary>
    /// <remarks>
    /// HSMWorks' `tangentialExtensionDistance`, and what it is for: running the cutter on
    /// past the end of an edge so it leaves the material cleanly rather than stopping in
    /// it, or off the start so it arrives up to speed.
    ///
    /// **The profile is extended, not the toolpath.** The cutter offset is taken from the
    /// result, so the extension is offset too and the wall it cuts continues straight.
    /// That is what separates this from HSMWorks' `tangentialFragmentExtensionDistance`,
    /// which stretches the computed motion instead and is not built.
    ///
    /// **Closed contours are returned untouched.** A loop has no ends to extend, and
    /// HSMWorks ignores the parameter for one.
    ///
    /// The tangent is the direction of the end segment. On a tessellated arc that is a
    /// chord rather than the true tangent, so it is off by whatever the chord tolerance
    /// allows - far below anything a cut can tell.
    /// </remarks>
    public static class TangentialExtension
    {
        /// <summary>
        /// The contour with both ends moved out along their tangents by
        /// <paramref name="distance"/>, or null when a negative distance consumes it.
        /// </summary>
        /// <remarks>
        /// A negative distance shortens instead, walking back through as many segments as
        /// it takes. Shortening past what is there leaves nothing to cut, which the caller
        /// reports rather than turning into a zero-length pass.
        /// </remarks>
        public static Polyline Apply(Polyline path, double distance)
        {
            if (path == null || path.IsEmpty || path.IsClosed || Math.Abs(distance) <= Precision.Epsilon)
            {
                return path;
            }

            return distance > 0 ? Lengthened(path, distance) : Shortened(path, -distance);
        }

        private static Polyline Lengthened(Polyline path, double distance)
        {
            List<Vec3> points = path.Points.ToList();

            Vec3 atStart = Direction(points, 0, 1);
            Vec3 atEnd = Direction(points, points.Count - 1, points.Count - 2);

            if (atStart.Length > Precision.Epsilon)
            {
                points.Insert(0, points[0] + (atStart * distance));
            }

            if (atEnd.Length > Precision.Epsilon)
            {
                points.Add(points[points.Count - 1] + (atEnd * distance));
            }

            return new Polyline(points);
        }

        private static Polyline Shortened(Polyline path, double distance)
        {
            // Both ends, so the whole of a contour shorter than two of them is gone.
            if (path.Length <= (distance * 2) + Precision.Epsilon)
            {
                return null;
            }

            List<Vec3> points = path.Points.ToList();

            TrimEnd(points, distance);
            points.Reverse();
            TrimEnd(points, distance);
            points.Reverse();

            return points.Count < 2 ? null : new Polyline(points);
        }

        /// <summary>
        /// Walks back from the last point by <paramref name="distance"/>, dropping whole
        /// segments and landing part way along the one that runs out.
        /// </summary>
        private static void TrimEnd(List<Vec3> points, double distance)
        {
            double left = distance;

            while (points.Count >= 2)
            {
                Vec3 last = points[points.Count - 1];
                Vec3 previous = points[points.Count - 2];
                double segment = (last - previous).Length;

                if (segment > left)
                {
                    Vec3 back = (previous - last).Normalised() * left;
                    points[points.Count - 1] = last + back;
                    return;
                }

                left -= segment;
                points.RemoveAt(points.Count - 1);
            }
        }

        /// <summary>
        /// The unit vector pointing out of the contour at <paramref name="end"/>, taken
        /// from the first segment that has a direction at all.
        /// </summary>
        private static Vec3 Direction(List<Vec3> points, int end, int inwards)
        {
            int step = inwards > end ? 1 : -1;

            for (int i = inwards; i >= 0 && i < points.Count; i += step)
            {
                Vec3 away = points[end] - points[i];

                if (away.Length > Precision.Epsilon)
                {
                    return away.Normalised();
                }
            }

            return Vec3.Zero;
        }
    }
}
