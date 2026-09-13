using System;
using System.Globalization;

namespace GCam.Core.Geometry.Primitives
{
    /// <summary>
    /// An affine transform in millimetres - the thing that carries a point from one
    /// coordinate system into another.
    /// </summary>
    /// <remarks>
    /// <b>Convention, stated once so nothing has to guess.</b> Points are column vectors
    /// and a transform is applied on the left: <c>p' = M * p</c>. The elements are named
    /// <c>MRowColumn</c>, so <see cref="M14"/> is the X translation and the last row is
    /// always (0, 0, 0, 1). The first three columns are the images of the X, Y and Z
    /// axes; the fourth is the image of the origin.
    ///
    /// That last sentence is the useful one: a frame built from an origin and three axes
    /// is just those four vectors written into the columns, which is exactly what
    /// <see cref="FromAxes"/> does and how the SOLIDWORKS side builds one without having
    /// to know how SOLIDWORKS packs its own matrices.
    ///
    /// There is deliberately no inverse. Every caller so far gets both directions of a
    /// transform from whoever supplied it, and a general 4x4 inverse is a page of code
    /// with a degenerate case to get wrong. Add one when a caller genuinely has only one
    /// direction - not before.
    ///
    /// Translation is in millimetres like every other length in Core. The rotation part
    /// is dimensionless, so a transform assembled from unit axes needs no scaling.
    /// </remarks>
    public struct Matrix4 : IEquatable<Matrix4>
    {
        public Matrix4(
            double m11, double m12, double m13, double m14,
            double m21, double m22, double m23, double m24,
            double m31, double m32, double m33, double m34)
        {
            M11 = m11; M12 = m12; M13 = m13; M14 = m14;
            M21 = m21; M22 = m22; M23 = m23; M24 = m24;
            M31 = m31; M32 = m32; M33 = m33; M34 = m34;
        }

        public double M11 { get; }

        public double M12 { get; }

        public double M13 { get; }

        /// <summary>X translation, in millimetres.</summary>
        public double M14 { get; }

        public double M21 { get; }

        public double M22 { get; }

        public double M23 { get; }

        /// <summary>Y translation, in millimetres.</summary>
        public double M24 { get; }

        public double M31 { get; }

        public double M32 { get; }

        public double M33 { get; }

        /// <summary>Z translation, in millimetres.</summary>
        public double M34 { get; }

        public static Matrix4 Identity => new Matrix4(
            1, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, 1, 0);

        /// <summary>
        /// The transform that carries the local axes onto the ones given.
        /// </summary>
        /// <remarks>
        /// The axes go into the columns and the origin into the fourth, which is what
        /// makes this the natural way to build a coordinate system's transform: measure
        /// where its origin and axes land, and write them down.
        ///
        /// Nothing here checks that the axes are unit length or perpendicular. A frame
        /// built from axes that are neither still transforms points consistently, which
        /// is all this type promises.
        /// </remarks>
        public static Matrix4 FromAxes(Vec3 origin, Vec3 xAxis, Vec3 yAxis, Vec3 zAxis)
        {
            return new Matrix4(
                xAxis.X, yAxis.X, zAxis.X, origin.X,
                xAxis.Y, yAxis.Y, zAxis.Y, origin.Y,
                xAxis.Z, yAxis.Z, zAxis.Z, origin.Z);
        }

        /// <summary>A point carried into the target coordinate system.</summary>
        public Vec3 Transform(Vec3 point)
        {
            return new Vec3(
                (M11 * point.X) + (M12 * point.Y) + (M13 * point.Z) + M14,
                (M21 * point.X) + (M22 * point.Y) + (M23 * point.Z) + M24,
                (M31 * point.X) + (M32 * point.Y) + (M33 * point.Z) + M34);
        }

        /// <summary>
        /// A direction carried into the target coordinate system - rotated, not moved.
        /// </summary>
        /// <remarks>
        /// The distinction matters. Transforming a direction as if it were a point adds
        /// the origin offset to it, which turns "up" into "somewhere near the origin,
        /// pointing who knows where" and is the classic way a transformed normal or
        /// search direction goes wrong.
        /// </remarks>
        public Vec3 TransformDirection(Vec3 direction)
        {
            return new Vec3(
                (M11 * direction.X) + (M12 * direction.Y) + (M13 * direction.Z),
                (M21 * direction.X) + (M22 * direction.Y) + (M23 * direction.Z),
                (M31 * direction.X) + (M32 * direction.Y) + (M33 * direction.Z));
        }

        /// <summary>
        /// This transform followed by <paramref name="then"/> - that is,
        /// <c>then * this</c>, so that
        /// <c>a.Then(b).Transform(p)</c> equals <c>b.Transform(a.Transform(p))</c>.
        /// </summary>
        /// <remarks>
        /// Named for the order it reads in rather than for the order the matrices
        /// multiply in. Matrix multiplication runs right to left and that has caught
        /// enough people that spelling the intent into the method name is worth the
        /// slightly unusual signature.
        /// </remarks>
        public Matrix4 Then(Matrix4 then)
        {
            return new Matrix4(
                (then.M11 * M11) + (then.M12 * M21) + (then.M13 * M31),
                (then.M11 * M12) + (then.M12 * M22) + (then.M13 * M32),
                (then.M11 * M13) + (then.M12 * M23) + (then.M13 * M33),
                (then.M11 * M14) + (then.M12 * M24) + (then.M13 * M34) + then.M14,

                (then.M21 * M11) + (then.M22 * M21) + (then.M23 * M31),
                (then.M21 * M12) + (then.M22 * M22) + (then.M23 * M32),
                (then.M21 * M13) + (then.M22 * M23) + (then.M23 * M33),
                (then.M21 * M14) + (then.M22 * M24) + (then.M23 * M34) + then.M24,

                (then.M31 * M11) + (then.M32 * M21) + (then.M33 * M31),
                (then.M31 * M12) + (then.M32 * M22) + (then.M33 * M32),
                (then.M31 * M13) + (then.M32 * M23) + (then.M33 * M33),
                (then.M31 * M14) + (then.M32 * M24) + (then.M33 * M34) + then.M34);
        }

        /// <summary>
        /// Equal within <see cref="Precision.Epsilon"/> element by element. The same
        /// reasoning as <see cref="Vec3.Equals(Vec3)"/>: == stays bitwise so a reader can
        /// see which was meant.
        /// </summary>
        public bool Equals(Matrix4 other)
        {
            return Close(M11, other.M11) && Close(M12, other.M12)
                && Close(M13, other.M13) && Close(M14, other.M14)
                && Close(M21, other.M21) && Close(M22, other.M22)
                && Close(M23, other.M23) && Close(M24, other.M24)
                && Close(M31, other.M31) && Close(M32, other.M32)
                && Close(M33, other.M33) && Close(M34, other.M34);
        }

        private static bool Close(double a, double b) => Math.Abs(a - b) < Precision.Epsilon;

        public override bool Equals(object obj) => obj is Matrix4 other && Equals(other);

        public override int GetHashCode()
        {
            // Coarse for the same reason as Vec3: epsilon-tolerant equality and hashing
            // cannot be made to agree, so this exists only to keep collections honest.
            unchecked
            {
                int hash = M11.GetHashCode();
                hash = (hash * 397) ^ M22.GetHashCode();
                hash = (hash * 397) ^ M33.GetHashCode();
                hash = (hash * 397) ^ M14.GetHashCode();
                hash = (hash * 397) ^ M24.GetHashCode();
                hash = (hash * 397) ^ M34.GetHashCode();
                return hash;
            }
        }

        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "[{0:0.###} {1:0.###} {2:0.###} | {3:0.###}] [{4:0.###} {5:0.###} {6:0.###} | {7:0.###}] " +
                "[{8:0.###} {9:0.###} {10:0.###} | {11:0.###}]",
                M11, M12, M13, M14, M21, M22, M23, M24, M31, M32, M33, M34);
        }
    }
}
