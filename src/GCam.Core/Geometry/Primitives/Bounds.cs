using System;
using System.Collections.Generic;

namespace GCam.Core.Geometry.Primitives
{
    /// <summary>
    /// An axis-aligned box in millimetres, used for the model extent and the stock
    /// around it.
    /// </summary>
    /// <remarks>
    /// Axis-aligned to the job's coordinate system, not to the part. Whoever builds one
    /// is responsible for having transformed its points first - this type does no
    /// transforming of its own.
    /// </remarks>
    public struct Bounds
    {
        public Bounds(Vec3 min, Vec3 max)
        {
            Min = min;
            Max = max;
        }

        public Vec3 Min { get; }

        public Vec3 Max { get; }

        /// <summary>Width, depth and height, as a vector. Never negative.</summary>
        public Vec3 Size => new Vec3(
            Math.Max(0, Max.X - Min.X),
            Math.Max(0, Max.Y - Min.Y),
            Math.Max(0, Max.Z - Min.Z));

        public Vec3 Centre => new Vec3(
            (Min.X + Max.X) / 2.0,
            (Min.Y + Max.Y) / 2.0,
            (Min.Z + Max.Z) / 2.0);

        /// <summary>
        /// True when the box has no extent on at least one axis - a single point, a flat
        /// face, or a box built from nothing.
        /// </summary>
        public bool IsEmpty
        {
            get
            {
                Vec3 size = Size;
                return size.X < Precision.Epsilon
                       || size.Y < Precision.Epsilon
                       || size.Z < Precision.Epsilon;
            }
        }

        /// <summary>
        /// The smallest box containing every point given.
        /// </summary>
        /// <exception cref="ArgumentException">No points were given.</exception>
        public static Bounds FromPoints(IEnumerable<Vec3> points)
        {
            if (points == null)
            {
                throw new ArgumentNullException(nameof(points));
            }

            bool any = false;
            double minX = 0, minY = 0, minZ = 0, maxX = 0, maxY = 0, maxZ = 0;

            foreach (Vec3 p in points)
            {
                if (!any)
                {
                    minX = maxX = p.X;
                    minY = maxY = p.Y;
                    minZ = maxZ = p.Z;
                    any = true;
                    continue;
                }

                minX = Math.Min(minX, p.X);
                minY = Math.Min(minY, p.Y);
                minZ = Math.Min(minZ, p.Z);
                maxX = Math.Max(maxX, p.X);
                maxY = Math.Max(maxY, p.Y);
                maxZ = Math.Max(maxZ, p.Z);
            }

            if (!any)
            {
                throw new ArgumentException("Cannot build bounds from an empty set of points.", nameof(points));
            }

            return new Bounds(new Vec3(minX, minY, minZ), new Vec3(maxX, maxY, maxZ));
        }

        /// <summary>The smallest box containing both.</summary>
        public Bounds Union(Bounds other)
        {
            return new Bounds(
                new Vec3(
                    Math.Min(Min.X, other.Min.X),
                    Math.Min(Min.Y, other.Min.Y),
                    Math.Min(Min.Z, other.Min.Z)),
                new Vec3(
                    Math.Max(Max.X, other.Max.X),
                    Math.Max(Max.Y, other.Max.Y),
                    Math.Max(Max.Z, other.Max.Z)));
        }

        /// <summary>
        /// Grown by a different amount on each face. Negative amounts shrink it.
        /// </summary>
        public Bounds Expanded(
            double minusX, double plusX,
            double minusY, double plusY,
            double minusZ, double plusZ)
        {
            return new Bounds(
                new Vec3(Min.X - minusX, Min.Y - minusY, Min.Z - minusZ),
                new Vec3(Max.X + plusX, Max.Y + plusY, Max.Z + plusZ));
        }

        public override string ToString() => $"{Min} to {Max}";
    }
}
