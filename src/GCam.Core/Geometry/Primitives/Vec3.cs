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
