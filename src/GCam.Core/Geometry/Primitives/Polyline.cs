using System;
using System.Collections.Generic;
using System.Linq;

namespace GCam.Core.Geometry.Primitives
{
    /// <summary>
    /// A chain of points, open or closed.
    /// </summary>
    /// <remarks>
    /// What a selected contour becomes once SOLIDWORKS' curves have been tessellated, and
    /// what the 2D strategies work in. Millimetres.
    ///
    /// **A closed polyline does not repeat its first point.** Closure is a property, not a
    /// duplicated vertex - so the point count is the segment count for a closed chain and
    /// one more than it for an open one. Storing the repeat instead would make every
    /// length, offset and area calculation have to remember whether it was there.
    ///
    /// Immutable. A strategy builds one and hands it on; nothing edits one in place.
    /// </remarks>
    public sealed class Polyline
    {
        private readonly Vec3[] _points;

        public Polyline(IEnumerable<Vec3> points, bool closed = false)
        {
            _points = (points ?? Enumerable.Empty<Vec3>()).ToArray();
            IsClosed = closed;
        }

        public IReadOnlyList<Vec3> Points => _points;

        /// <summary>True when the last point joins back to the first.</summary>
        public bool IsClosed { get; }

        public int Count => _points.Length;

        /// <summary>True when there is not enough here to draw or cut.</summary>
        public bool IsEmpty => _points.Length < 2;

        /// <summary>How many straight segments the chain has.</summary>
        public int SegmentCount =>
            _points.Length < 2 ? 0 : IsClosed ? _points.Length : _points.Length - 1;

        public Vec3 this[int index] => _points[index];

        /// <summary>The point at the far end of segment <paramref name="index"/>.</summary>
        public Vec3 EndOfSegment(int index) => _points[(index + 1) % _points.Length];

        public double Length
        {
            get
            {
                double total = 0;

                for (int i = 0; i < SegmentCount; i++)
                {
                    total += (EndOfSegment(i) - _points[i]).Length;
                }

                return total;
            }
        }

        /// <summary>
        /// Twice the signed area of the chain projected onto XY. Positive is
        /// counter-clockwise seen from +Z.
        /// </summary>
        /// <remarks>
        /// The shoelace sum, left undivided because only its sign is ever wanted: which way
        /// round a contour runs decides the direction of travel, and with it climb or
        /// conventional, and halving it to get a real area would only add a rounding step
        /// to a comparison against zero.
        /// </remarks>
        public double SignedAreaXy2
        {
            get
            {
                double sum = 0;

                for (int i = 0; i < SegmentCount; i++)
                {
                    Vec3 a = _points[i];
                    Vec3 b = EndOfSegment(i);
                    sum += (a.X * b.Y) - (b.X * a.Y);
                }

                return sum;
            }
        }

        /// <summary>True when the chain runs counter-clockwise seen from above.</summary>
        public bool IsCounterClockwise => SignedAreaXy2 > 0;

        /// <summary>The same chain the other way round.</summary>
        public Polyline Reversed() => new Polyline(_points.Reverse(), IsClosed);

        /// <summary>The same chain running the way asked for.</summary>
        public Polyline WithDirection(bool counterClockwise) =>
            IsCounterClockwise == counterClockwise ? this : Reversed();

        /// <summary>
        /// The point on this chain closest to <paramref name="to"/>, measured in XY.
        /// </summary>
        /// <remarks>
        /// Z is ignored, not projected: the callers are 2D - "where is the wall relative to
        /// the cutter" - and the two chains involved are usually at different heights by
        /// design, since a toolpath is the profile moved sideways and down.
        /// </remarks>
        public Vec3 NearestPointXy(Vec3 to)
        {
            if (_points.Length == 0)
            {
                return to;
            }

            Vec3 best = _points[0];
            double bestDistance = double.MaxValue;

            for (int i = 0; i < SegmentCount; i++)
            {
                Vec3 a = _points[i];
                Vec3 b = EndOfSegment(i);

                double dx = b.X - a.X;
                double dy = b.Y - a.Y;
                double lengthSquared = (dx * dx) + (dy * dy);

                double at = lengthSquared <= double.Epsilon
                    ? 0
                    : (((to.X - a.X) * dx) + ((to.Y - a.Y) * dy)) / lengthSquared;

                at = Math.Max(0, Math.Min(1, at));

                var candidate = new Vec3(a.X + (at * dx), a.Y + (at * dy), a.Z);
                double distance = ((to.X - candidate.X) * (to.X - candidate.X))
                                  + ((to.Y - candidate.Y) * (to.Y - candidate.Y));

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            return best;
        }

        /// <summary>The same chain at a different height.</summary>
        public Polyline AtZ(double z) =>
            new Polyline(_points.Select(p => new Vec3(p.X, p.Y, z)), IsClosed);

        public Bounds? Extent
        {
            get
            {
                if (_points.Length == 0)
                {
                    return null;
                }

                Vec3 min = _points[0];
                Vec3 max = min;

                foreach (Vec3 p in _points)
                {
                    min = new Vec3(Math.Min(min.X, p.X), Math.Min(min.Y, p.Y), Math.Min(min.Z, p.Z));
                    max = new Vec3(Math.Max(max.X, p.X), Math.Max(max.Y, p.Y), Math.Max(max.Z, p.Z));
                }

                return new Bounds(min, max);
            }
        }

        public override string ToString() =>
            $"Polyline ({_points.Length} points{(IsClosed ? ", closed" : string.Empty)})";
    }
}
