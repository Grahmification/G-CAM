using System;
using GCam.Core;
using GCam.Core.Geometry.Primitives;
using SolidWorks.Interop.sldworks;

namespace GCam.SolidWorks.Extraction
{
    /// <summary>
    /// Turns a coordinate system feature into transforms Core can use.
    /// </summary>
    /// <remarks>
    /// <b>The transform is measured, not decoded.</b> IMathTransform exposes its 16
    /// doubles through ArrayData, and the help says only that "the first 9 elements
    /// define the 3x3 rotation matrix" - it never says whether those nine are stored by
    /// row or by column. Reading them the wrong way round gives a matrix that is the
    /// transpose of the right one, which is *correct for every axis-aligned coordinate
    /// system* and wrong only once one is rotated. That is the worst possible failure
    /// mode: it works on every test part anyone builds on purpose.
    ///
    /// So instead of decoding the array, this asks SOLIDWORKS where four known points
    /// land - the origin and the three unit axes - and reads the answer off. A transform
    /// is fully described by what it does to those four, and IMathPoint::MultiplyTransform
    /// documents its own convention ("rotated, scaled, and then translated"), so nothing
    /// is being guessed at. Four COM calls per direction, once per preview, which is
    /// nothing next to being quietly wrong.
    ///
    /// The convention is <b>From docs</b>. That the technique gets a rotated coordinate
    /// system right is <b>Verified</b> on SOLIDWORKS 2025 SP3 - the case that would have
    /// exposed a transposed matrix, and the one an axis-aligned test part cannot. See
    /// docs/solidworks-api/coordinate-systems.md.
    /// </remarks>
    internal static class CoordinateSystems
    {
        /// <summary>
        /// The transforms for a job's coordinate system, both directions.
        /// </summary>
        /// <remarks>
        /// A blank name means the part origin, which is the identity in both directions.
        /// A name the part no longer has does too - the caller has already logged the
        /// missing feature, and machining at the part origin is a visible, recoverable
        /// wrong answer where refusing to draw anything is just a mystery.
        /// </remarks>
        public static JobFrame Resolve(SldWorks swApp, ModelDoc2 model, string coordinateSystemName)
        {
            if (swApp == null || model == null || string.IsNullOrWhiteSpace(coordinateSystemName))
            {
                return JobFrame.PartOrigin;
            }

            var maths = swApp.GetMathUtility() as MathUtility;

            if (maths == null)
            {
                return JobFrame.PartOrigin;
            }

            var toPart = model.Extension
                .GetCoordinateSystemTransformByName(coordinateSystemName) as MathTransform;

            if (toPart == null)
            {
                return JobFrame.PartOrigin;
            }

            var toJob = toPart.Inverse() as MathTransform;

            if (toJob == null)
            {
                return JobFrame.PartOrigin;
            }

            return new JobFrame(Measure(maths, toPart), Measure(maths, toJob));
        }

        /// <summary>
        /// Works out what a SOLIDWORKS transform does by putting four known points
        /// through it.
        /// </summary>
        /// <remarks>
        /// The image of the origin is the translation; the image of each unit axis, less
        /// that origin, is the column the axis maps to. Subtracting the origin is what
        /// makes this independent of units as well as of layout: the probe points are a
        /// metre long because SOLIDWORKS works in metres, but the differences come out
        /// dimensionless, so the rotation needs no scaling and only the translation is
        /// converted into millimetres.
        /// </remarks>
        private static Matrix4 Measure(MathUtility maths, MathTransform transform)
        {
            Vec3 origin = Apply(maths, transform, 0, 0, 0);

            Vec3 x = Apply(maths, transform, 1, 0, 0) - origin;
            Vec3 y = Apply(maths, transform, 0, 1, 0) - origin;
            Vec3 z = Apply(maths, transform, 0, 0, 1) - origin;

            return Matrix4.FromAxes(ToMillimetres(origin), x, y, z);
        }

        private static Vec3 Apply(
            MathUtility maths, MathTransform transform, double x, double y, double z)
        {
            var point = maths.CreatePoint(new[] { x, y, z }) as MathPoint;

            if (point == null)
            {
                throw new InvalidOperationException(
                    "SOLIDWORKS would not create a math point, so a coordinate system cannot be read.");
            }

            var moved = point.MultiplyTransform(transform) as MathPoint;

            if (moved == null)
            {
                throw new InvalidOperationException(
                    "SOLIDWORKS would not apply a coordinate system transform.");
            }

            var data = moved.ArrayData as double[];

            if (data == null || data.Length < 3)
            {
                throw new InvalidOperationException(
                    "A transformed math point came back without coordinates.");
            }

            return new Vec3(data[0], data[1], data[2]);
        }

        private static Vec3 ToMillimetres(Vec3 metres) =>
            new Vec3(
                Units.MetresToMillimetres(metres.X),
                Units.MetresToMillimetres(metres.Y),
                Units.MetresToMillimetres(metres.Z));
    }
}
