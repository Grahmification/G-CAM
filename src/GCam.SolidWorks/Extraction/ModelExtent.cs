using System;
using System.Collections.Generic;
using GCam.Core;
using GCam.Core.Geometry.Primitives;
using SolidWorks.Interop.sldworks;

namespace GCam.SolidWorks.Extraction
{
    /// <summary>
    /// How big the model is, measured along a job's axes rather than the part's.
    /// </summary>
    /// <remarks>
    /// <b>Not IBody2::GetBodyBox.</b> Two things rule it out. Its box is axis-aligned to
    /// the *part*, so for a job on a rotated coordinate system it describes the wrong
    /// box entirely; and the help says outright that its values "are approximate and
    /// should not be used for comparison or calculation purposes", which is what sizing
    /// stock is.
    ///
    /// IBody2::GetExtremePoint answers the question directly instead: give it a
    /// direction and it returns the furthest point of the body that way, exactly. Six
    /// directions - the job's own axes, positive and negative - give six points, and the
    /// box round those six is the tight box in job space. The -X extreme point holds the
    /// smallest X any point of the body has, so no other point can fall outside; the same
    /// on each axis. It is exact for curved faces too, which a tessellated box is not.
    ///
    /// <b>Millimetres out.</b> GetExtremePoint works in SOLIDWORKS' metres; the
    /// conversion happens here, at the extraction edge, as the units rule requires.
    /// </remarks>
    internal static class ModelExtent
    {
        /// <summary>
        /// The six job-space directions to measure along. Order is irrelevant - every
        /// point found goes into the same box.
        /// </summary>
        private static readonly Vec3[] Directions =
        {
            new Vec3(1, 0, 0), new Vec3(-1, 0, 0),
            new Vec3(0, 1, 0), new Vec3(0, -1, 0),
            new Vec3(0, 0, 1), new Vec3(0, 0, -1),
        };

        /// <summary>
        /// The tight box around these bodies, in job coordinates and millimetres, or null
        /// when nothing could be measured.
        /// </summary>
        /// <remarks>
        /// Null rather than an empty box, because "this part has no solid bodies yet" and
        /// "this part is zero-sized" are different situations and only the second is a
        /// problem. A caller showing a preview simply shows nothing.
        /// </remarks>
        public static Bounds? OfBodies(IEnumerable<Body2> bodies, JobFrame frame)
        {
            if (bodies == null)
            {
                return null;
            }

            var points = new List<Vec3>();

            foreach (Body2 body in bodies)
            {
                if (body != null)
                {
                    Collect(body, frame, points);
                }
            }

            return points.Count == 0 ? (Bounds?)null : Bounds.FromPoints(points);
        }

        private static void Collect(Body2 body, JobFrame frame, ICollection<Vec3> points)
        {
            foreach (Vec3 direction in Directions)
            {
                // The direction is stated in job space and has to be asked for in part
                // space. Rotated, not moved - a direction that picked up the coordinate
                // system's origin offset would point somewhere else entirely.
                //
                // Renormalised because the frame may carry a scale, and an extreme point
                // asked for along a direction that is not unit length is not something
                // the help makes any promise about.
                Vec3 inPart = frame.ToPart.TransformDirection(direction).Normalised();

                if (inPart.Length < Precision.Epsilon)
                {
                    continue;
                }

                double x, y, z;

                if (!body.GetExtremePoint(inPart.X, inPart.Y, inPart.Z, out x, out y, out z))
                {
                    continue;
                }

                var found = new Vec3(
                    Units.MetresToMillimetres(x),
                    Units.MetresToMillimetres(y),
                    Units.MetresToMillimetres(z));

                points.Add(frame.ToJob.Transform(found));
            }
        }
    }
}
