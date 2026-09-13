using System;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;
using GCam.Core.Geometry.Primitives;

namespace GCam.Core.Geometry.Offset
{
    /// <summary>
    /// Contour offsetting, done by Clipper2.
    /// </summary>
    /// <remarks>
    /// The one bought-in algorithm in the geometry kernel, and the one worth buying: an
    /// offset that removes its own self-intersections is a solved problem with a lot of
    /// edge cases, and getting it subtly wrong produces a toolpath that looks right and
    /// gouges the part.
    ///
    /// **Clipper2 works in fixed point.** Its double-precision entry points scale by a
    /// decimal precision and compute in 64-bit integers. At <see cref="Precision"/> = 4
    /// the grid is 0.0001mm - two orders finer than any machining tolerance, and nowhere
    /// near overflowing a long for parts measured in metres.
    ///
    /// **Z is carried, not computed.** Clipper2 is a 2D library; the contour's Z is taken
    /// from its first point and put back on every result. Contouring only ever offsets in
    /// plan, so nothing is lost, and pretending otherwise would invite someone to offset a
    /// ramp.
    /// </remarks>
    public sealed class Clipper2Offsetter : IContourOffsetter
    {
        /// <summary>Decimal places Clipper2 keeps. 4 is a tenth of a micron.</summary>
        public const int Precision = 4;

        public IReadOnlyList<Polyline> Offset(Polyline contour, double distance, double arcTolerance)
        {
            var results = new List<Polyline>();

            if (contour == null || contour.IsEmpty)
            {
                return results;
            }

            if (Math.Abs(distance) <= Precision_Epsilon)
            {
                // Nothing to do. Returning the contour itself rather than running it
                // through the library keeps "no offset" exact - a round trip through a
                // fixed-point grid would move points by up to half a grid step.
                results.Add(contour);
                return results;
            }

            double z = contour.Points[0].Z;

            var path = new PathD(contour.Points.Select(p => new PointD(p.X, p.Y)));
            var paths = new PathsD { path };

            PathsD offset = Clipper.InflatePaths(
                paths,
                distance,
                JoinType.Round,
                EndType.Polygon,
                miterLimit: 2.0,
                precision: Precision,
                arcTolerance: Math.Max(arcTolerance, 1e-4));

            foreach (PathD result in offset)
            {
                if (result.Count >= 3)
                {
                    results.Add(new Polyline(
                        result.Select(p => new Vec3(p.x, p.y, z)), closed: true));
                }
            }

            return results;
        }

        /// <summary>
        /// Below this an offset is no offset. One grid step of
        /// <see cref="Precision"/>, because a smaller distance cannot be represented
        /// anyway.
        /// </summary>
        private const double Precision_Epsilon = 1e-4;
    }
}
