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
            IEnumerable<Polyline> segments, double tolerance = DefaultTolerance) =>
            ChainWithSources(segments, tolerance).Select(c => c.Path).ToList();

        /// <summary>
        /// The same chaining, saying which of the input segments went into each chain.
        /// </summary>
        /// <remarks>
        /// Chaining pools every piece it is given, so one chain can be built from several
        /// separately picked entities - four edges of a rectangle are four picks and one
        /// loop. Anything that has to carry a per-pick decision through to the result needs
        /// to know which picks ended up where, and only this method can say: by the time
        /// there is a <see cref="Polyline"/>, the pieces have been reversed, reordered and
        /// merged past recognition.
        ///
        /// That is what makes the Reverse button work. **A chain has exactly one direction**
        /// - it is cut as one continuous move - so reversing any one of the picks that
        /// built it reverses the whole chain, which is the only coherent answer.
        ///
        /// Indices are into the sequence as passed in, counting the null and empty entries
        /// that are skipped, so they line up with the caller's own list.
        /// </remarks>
        public static IReadOnlyList<Chain> ChainWithSources(
            IEnumerable<Polyline> segments, double tolerance = DefaultTolerance)
        {
            var remaining = new List<Piece>();
            int index = 0;

            foreach (Polyline segment in segments ?? Enumerable.Empty<Polyline>())
            {
                if (segment != null && !segment.IsEmpty)
                {
                    remaining.Add(new Piece(segment.Points.ToList(), index));
                }

                index++;
            }

            var chains = new List<Chain>();

            while (remaining.Count > 0)
            {
                Piece chain = remaining[0];
                remaining.RemoveAt(0);

                // Keep sweeping: joining one piece on can make an earlier one fit too.
                bool grew = true;
                while (grew)
                {
                    grew = false;

                    for (int i = 0; i < remaining.Count; i++)
                    {
                        if (TryJoin(chain.Points, remaining[i].Points, tolerance))
                        {
                            chain.Sources.AddRange(remaining[i].Sources);
                            remaining.RemoveAt(i);
                            grew = true;
                            break;
                        }
                    }
                }

                List<Vec3> points = chain.Points;
                bool closed = points.Count > 2 && Near(points[0], points[points.Count - 1], tolerance);

                if (closed)
                {
                    // A closed polyline does not repeat its first point.
                    points.RemoveAt(points.Count - 1);
                }

                chain.Sources.Sort();
                chains.Add(new Chain(new Polyline(points, closed), chain.Sources));
            }

            return chains;
        }

        /// <summary>A chain, and which of the input segments it was built from.</summary>
        public sealed class Chain
        {
            public Chain(Polyline path, IReadOnlyList<int> sources)
            {
                Path = path;
                Sources = sources;
            }

            public Polyline Path { get; }

            /// <summary>Indices into the sequence handed to <see cref="ChainWithSources"/>.</summary>
            public IReadOnlyList<int> Sources { get; }
        }

        /// <summary>A chain under construction, and where its points came from.</summary>
        private sealed class Piece
        {
            public Piece(List<Vec3> points, int source)
            {
                Points = points;
                Sources = new List<int> { source };
            }

            public List<Vec3> Points { get; }

            public List<int> Sources { get; }
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
