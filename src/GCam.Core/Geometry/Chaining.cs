using System;
using System.Collections.Generic;
using System.Linq;
using GCam.Core.Geometry.Primitives;

namespace GCam.Core.Geometry
{
    /// <summary>
    /// Joins loose pieces of curve into contours.
    /// </summary>
    /// <remarks>
    /// Selecting a profile in SOLIDWORKS gives a handful of edges in no particular order
    /// and no particular direction. A strategy needs closed loops running one way, so
    /// somebody has to sort them out - and it is arithmetic with a right answer, so it is
    /// in Core where a test can reach it rather than next to the COM that produced the
    /// pieces.
    ///
    /// The tolerance is a *chaining* tolerance: how far apart two ends may be and still be
    /// the same point. It exists because tessellating two edges separately does not
    /// produce bit-identical endpoints even when the edges genuinely meet. HSMWorks
    /// carries the same parameter, at 0.01mm, for the same reason.
    /// </remarks>
    public static class Chaining
    {
        /// <summary>Ends closer than this are the same point, mm. HSMWorks' default.</summary>
        public const double DefaultTolerance = 0.01;

        /// <summary>
        /// Joins segments end to end into the longest chains they make.
        /// </summary>
        /// <remarks>
        /// Greedy: take a segment, extend it at both ends for as long as something fits,
        /// then start again with whatever is left. That gives the right answer whenever
        /// the pieces form simple chains, which is what a selected profile is. A junction
        /// where three edges meet at a point is ambiguous by nature, and this takes
        /// whichever it finds first rather than pretending to know.
        ///
        /// A chain whose ends meet comes back closed, with the duplicate endpoint removed,
        /// because that is what <see cref="Polyline.IsClosed"/> means.
        /// </remarks>
        public static IReadOnlyList<Polyline> ChainIntoLoops(
            IEnumerable<Polyline> segments, double tolerance = DefaultTolerance)
        {
            var remaining = (segments ?? Enumerable.Empty<Polyline>())
                .Where(s => s != null && !s.IsEmpty)
                .Select(s => s.Points.ToList())
                .ToList();

            var chains = new List<Polyline>();

            while (remaining.Count > 0)
            {
                List<Vec3> chain = remaining[0];
                remaining.RemoveAt(0);

                // Keep sweeping: joining one piece on can make an earlier one fit too.
                bool grew = true;
                while (grew)
                {
                    grew = false;

                    for (int i = 0; i < remaining.Count; i++)
                    {
                        if (TryJoin(chain, remaining[i], tolerance))
                        {
                            remaining.RemoveAt(i);
                            grew = true;
                            break;
                        }
                    }
                }

                bool closed = chain.Count > 2 && Near(chain[0], chain[chain.Count - 1], tolerance);

                if (closed)
                {
                    // A closed polyline does not repeat its first point.
                    chain.RemoveAt(chain.Count - 1);
                }

                chains.Add(new Polyline(chain, closed));
            }

            return chains;
        }

        /// <summary>
        /// Adds a piece to whichever end of the chain it meets, reversing it if it joins
        /// the wrong way round.
        /// </summary>
        private static bool TryJoin(List<Vec3> chain, List<Vec3> piece, double tolerance)
        {
            Vec3 chainStart = chain[0];
            Vec3 chainEnd = chain[chain.Count - 1];
            Vec3 pieceStart = piece[0];
            Vec3 pieceEnd = piece[piece.Count - 1];

            if (Near(chainEnd, pieceStart, tolerance))
            {
                Append(chain, piece, reversed: false);
                return true;
            }

            if (Near(chainEnd, pieceEnd, tolerance))
            {
                Append(chain, piece, reversed: true);
                return true;
            }

            if (Near(chainStart, pieceEnd, tolerance))
            {
                Prepend(chain, piece, reversed: false);
                return true;
            }

            if (Near(chainStart, pieceStart, tolerance))
            {
                Prepend(chain, piece, reversed: true);
                return true;
            }

            return false;
        }

        private static void Append(List<Vec3> chain, List<Vec3> piece, bool reversed)
        {
            IEnumerable<Vec3> points = reversed ? Enumerable.Reverse(piece) : piece;

            // Skip the shared point - it is already the end of the chain.
            chain.AddRange(points.Skip(1));
        }

        private static void Prepend(List<Vec3> chain, List<Vec3> piece, bool reversed)
        {
            List<Vec3> points = (reversed ? Enumerable.Reverse(piece) : piece).ToList();

            points.RemoveAt(points.Count - 1);
            chain.InsertRange(0, points);
        }

        private static bool Near(Vec3 a, Vec3 b, double tolerance) =>
            (a - b).Length <= Math.Max(tolerance, Precision.Epsilon);
    }
}
