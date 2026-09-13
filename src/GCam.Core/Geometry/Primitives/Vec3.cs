using System;
using System.Globalization;

namespace GCam.Core.Geometry.Primitives
{
    /// <summary>
    /// A point or vector in millimetres.
    /// </summary>
    /// <remarks>
    /// The first piece of the geometry kernel. Deliberately minimal - it carries only
    /// what stock calculation needs today, and grows when a caller needs more rather
    /// than in anticipation.
    ///
    /// A struct because these are values, compared by what they hold rather than by
    /// identity, and toolpaths will eventually hold millions of them.
    /// </remarks>
    public struct Vec3 : IEquatable<Vec3>
    {
        public Vec3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public double X { get; }

        public double Y { get; }

        public double Z { get; }

        public static Vec3 Zero => new Vec3(0, 0, 0);

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        public static Vec3 operator *(Vec3 v, double scale) => new Vec3(v.X * scale, v.Y * scale, v.Z * scale);

        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

        public double Dot(Vec3 other) => (X * other.X) + (Y * other.Y) + (Z * other.Z);

        /// <summary>
        /// The vector perpendicular to both, right-handed: X cross Y gives Z.
        /// </summary>
        public Vec3 Cross(Vec3 other) => new Vec3(
            (Y * other.Z) - (Z * other.Y),
            (Z * other.X) - (X * other.Z),
            (X * other.Y) - (Y * other.X));

        /// <summary>
        /// Some unit vector at right angles to this one.
        /// </summary>
        /// <remarks>
        /// Which one is unspecified and callers must not care - it exists to start a
        /// basis, as when sweeping a circle around an axis. The reference vector is
        /// chosen to be the axis this one leans on least, because crossing with a nearly
        /// parallel vector gives something short and numerically poor.
        /// </remarks>
        public Vec3 AnyPerpendicular()
        {
            Vec3 reference = Math.Abs(X) < 0.9 ? new Vec3(1, 0, 0) : new Vec3(0, 1, 0);

            return Cross(reference).Normalised();
        }

        /// <summary>
        /// The same direction with unit length.
        /// </summary>
        /// <remarks>
        /// A zero-length vector has no direction, so it comes back unchanged rather than
        /// as NaN. Callers asking for a direction from a degenerate vector have a bug
        /// further up; returning zero keeps it from spreading silently through the
        /// arithmetic that follows.
        /// </remarks>
        public Vec3 Normalised()
        {
            double length = Length;

            return length < Precision.Epsilon ? Zero : new Vec3(X / length, Y / length, Z / length);
        }

        /// <summary>
        /// Equal within <see cref="Precision.Epsilon"/> on every axis.
        /// </summary>
        /// <remarks>
        /// Exact equality on doubles is a trap, so == is not overloaded: a caller that
        /// writes it gets the compiler's bitwise comparison and a reader can see that is
        /// what was meant. This method is the one that tolerates arithmetic.
        /// </remarks>
        public bool Equals(Vec3 other)
        {
            return Math.Abs(X - other.X) < Precision.Epsilon
                   && Math.Abs(Y - other.Y) < Precision.Epsilon
                   && Math.Abs(Z - other.Z) < Precision.Epsilon;
        }

        public override bool Equals(object obj) => obj is Vec3 other && Equals(other);

        public override int GetHashCode()
        {
            // Deliberately coarse. Epsilon-tolerant equality cannot produce a hash that
            // agrees with it, so this type is a poor dictionary key and the hash exists
            // only so that putting one in a collection does not misbehave outright.
            unchecked
            {
                int hash = X.GetHashCode();
                hash = (hash * 397) ^ Y.GetHashCode();
                hash = (hash * 397) ^ Z.GetHashCode();
                return hash;
            }
        }

        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture, "({0:0.###}, {1:0.###}, {2:0.###})", X, Y, Z);
        }
    }
}
