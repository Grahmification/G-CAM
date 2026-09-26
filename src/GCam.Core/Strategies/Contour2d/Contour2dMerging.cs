using System;
using System.Collections.Generic;
using System.Linq;
using GCam.Core.Geometry;
using GCam.Core.Geometry.Offset;
using GCam.Core.Geometry.Primitives;

namespace GCam.Core.Strategies.Contour2d
{
    /// <summary>
    /// One cutter path, and the earliest of the contours it was cut from.
    /// </summary>
    internal sealed class MergedCutterPath
    {
        public MergedCutterPath(Polyline path, int source)
        {
            Path = path;
            Source = source;
        }

        /// <summary>Running the way the cutter travels it.</summary>
        public Polyline Path { get; }

        /// <summary>Index into the contours handed to <see cref="Contour2dMerging.CutterPaths"/>.</summary>
        public int Source { get; }
    }

    /// <summary>
    /// The cutter paths for several contours cut at the same depths, merged where they
    /// would cut into one another.
    /// </summary>
    /// <remarks>
    /// HSMWorks' behaviour, as measured against it: select two concentric outlines and
    /// the inner one's path, which runs through the material inside the outer one, is not
    /// cut; select two overlapping bosses and one path runs round the outside of both.
    /// Both are one rule - a cutter path is cut only where no other contour forbids it -
    /// and what is left is joined up where the pieces meet.
    ///
    /// **What each contour forbids the others:**
    ///
    /// | Contour | Forbids |
    /// | --- | --- |
    /// | Closed, cut outside (a boss) | Its whole grown shape - the region its own path encloses |
    /// | Closed, cut inside (a pocket) | To another pocket, its whole shrunk shape, so pockets merge into their union. To anything else, a band either side of its wall |
    /// | Open | A band either side of it |
    ///
    /// A band is the offset distance wide on each side: a cutter centre inside it puts the
    /// cutter across the wall. An open contour's band is rounded past its ends, because a
    /// picked edge ends at a corner of the part rather than in air. A pocket forbids only a
    /// band to a boss or an open contour,
    /// not everything outside it, because a boss elsewhere on the part is not inside the
    /// pocket's material in any sense that should stop it being cut. An open contour has
    /// no inside, so a band is all it can say.
    ///
    /// **The pieces join head to tail**, because every contour's path keeps the material on
    /// the same hand: on the left of travel for climb, the right for conventional. So where
    /// one path stops at another's, the other carries on in the same direction, and the
    /// result is the combined outline. A path that runs into the far side of an open
    /// contour's band stops there, short of the wall, with nothing to join.
    ///
    /// **Only at the same depths.** The caller groups contours by their heights before
    /// asking - two chains cut at different depths do not meet.
    /// </remarks>
    internal static class Contour2dMerging
    {
        /// <summary>
        /// The cutter paths for <paramref name="contours"/>, merged. Ordered by the
        /// earliest contour each was cut from, so the cut follows the selection.
        /// </summary>
        /// <param name="distance">
        /// How far the cutter centre runs from each contour. At zero or below - stock to
        /// leave past the cutter radius, which carries the cutter across the wall - the
        /// paths are not merged: the cutter is inside the material on purpose, so there is
        /// nothing it should be kept out of.
        /// </param>
        public static IReadOnlyList<MergedCutterPath> CutterPaths(
            IReadOnlyList<ResolvedContour> contours,
            bool climb,
            double distance,
            IContourOffsetter offsetter,
            double arcTolerance)
        {
            List<IReadOnlyList<Polyline>> own = contours
                .Select(c => Contour2dOffsetting.OffsetAll(c, climb, distance, offsetter, arcTolerance))
                .ToList();

            if (contours.Count < 2 || distance <= Precision.Epsilon)
            {
                return own
                    .SelectMany((paths, i) => paths.Select(p => new MergedCutterPath(p, i)))
                    .ToList();
            }

            var forbids = new Forbidden(contours, climb, distance, offsetter, arcTolerance);
            var pieces = new List<MergedCutterPath>();

            for (int i = 0; i < contours.Count; i++)
            {
                foreach (Polyline path in own[i])
                {
                    IReadOnlyList<Polyline> region = forbids.To(i, path);

                    foreach (Polyline piece in offsetter.Outside(path, region))
                    {
                        pieces.Add(new MergedCutterPath(piece, i));
                    }
                }
            }

            return Joined(pieces)
                .OrderBy(p => p.Source)
                .ToList();
        }

        /// <summary>What the kind of contour it is makes it forbid.</summary>
        private enum Kind
        {
            Boss,
            Pocket,
            Open,
        }

        /// <summary>
        /// The regions each contour forbids, worked out once each and only when asked for.
        /// </summary>
        private sealed class Forbidden
        {
            private readonly IReadOnlyList<ResolvedContour> _contours;
            private readonly double _distance;
            private readonly IContourOffsetter _offsetter;
            private readonly double _arcTolerance;
            private readonly Kind[] _kinds;
            private readonly Polyline[] _walked;
            private readonly IReadOnlyList<Polyline>[] _shapes;
            private readonly IReadOnlyList<Polyline>[] _bands;

            public Forbidden(
                IReadOnlyList<ResolvedContour> contours,
                bool climb,
                double distance,
                IContourOffsetter offsetter,
                double arcTolerance)
            {
                _contours = contours;
                _distance = distance;
                _offsetter = offsetter;
                _arcTolerance = arcTolerance;

                _kinds = contours.Select(c =>
                        !c.Path.IsClosed ? Kind.Open
                        : Contour2dOffsetting.IsOutside(c, climb) ? Kind.Boss
                        : Kind.Pocket)
                    .ToArray();

                _walked = contours.Select(Contour2dOffsetting.Walked).ToArray();
                _shapes = new IReadOnlyList<Polyline>[contours.Count];
                _bands = new IReadOnlyList<Polyline>[contours.Count];
            }

            /// <summary>
            /// Everything the other contours forbid <paramref name="path"/>, one of
            /// contour <paramref name="contour"/>'s cutter paths.
            /// </summary>
            /// <remarks>
            /// A contour nowhere near the path is skipped before anything is computed for
            /// it. That is most of them, most of the time, and it leaves a path nothing
            /// comes near exactly as it was offset.
            /// </remarks>
            public IReadOnlyList<Polyline> To(int contour, Polyline path)
            {
                var region = new List<Polyline>();
                Bounds? reach = path.Extent;

                for (int j = 0; j < _contours.Count; j++)
                {
                    if (j == contour || reach == null || !Near(reach.Value, _contours[j].Path))
                    {
                        continue;
                    }

                    region.AddRange(By(j, _kinds[contour]));
                }

                return region;
            }

            private IReadOnlyList<Polyline> By(int j, Kind cutting)
            {
                switch (_kinds[j])
                {
                    case Kind.Boss:
                        return Shape(j);
                    case Kind.Pocket:
                        return cutting == Kind.Pocket ? Shape(j) : Band(j);
                    default:
                        return Band(j);
                }
            }

            /// <summary>The region contour <paramref name="j"/>'s own path encloses.</summary>
            private IReadOnlyList<Polyline> Shape(int j) =>
                _shapes[j] ?? (_shapes[j] = _offsetter.Offset(
                    _walked[j], _kinds[j] == Kind.Boss ? _distance : -_distance, _arcTolerance));

            private IReadOnlyList<Polyline> Band(int j) =>
                _bands[j] ?? (_bands[j] = _offsetter.Band(_walked[j], _distance, _arcTolerance));

            /// <summary>
            /// Whether anything contour <paramref name="other"/> forbids could reach into
            /// <paramref name="reach"/>: its outline grown by the offset distance, which
            /// bounds its shape and its band alike.
            /// </summary>
            private bool Near(Bounds reach, Polyline other)
            {
                Bounds? extent = other.Extent;

                if (extent == null)
                {
                    return false;
                }

                double margin = _distance + Precision.Epsilon;

                return extent.Value.Min.X - margin <= reach.Max.X
                       && reach.Min.X <= extent.Value.Max.X + margin
                       && extent.Value.Min.Y - margin <= reach.Max.Y
                       && reach.Min.Y <= extent.Value.Max.Y + margin;
            }
        }

        /// <summary>
        /// Pieces joined end to start wherever one begins where another stops, closing any
        /// that come back round to where they began.
        /// </summary>
        /// <remarks>
        /// **Never reversed to make a fit.** Every piece already runs the way the cutter
        /// must travel it, so a piece that would only join backwards is not part of the
        /// same outline - which is why this is not <see cref="Chaining"/>, which reverses
        /// freely because its pieces have no direction yet.
        ///
        /// Ends are compared in XY. Contours at the same depths can lie at different Z,
        /// and it is the depth they are cut at, not the Z they were drawn at, that decides
        /// whether their paths meet.
        /// </remarks>
        private static IEnumerable<MergedCutterPath> Joined(IReadOnlyList<MergedCutterPath> pieces)
        {
            var remaining = new List<MergedCutterPath>();

            foreach (MergedCutterPath piece in pieces)
            {
                if (piece.Path.IsClosed)
                {
                    yield return piece;
                }
                else
                {
                    remaining.Add(piece);
                }
            }

            while (remaining.Count > 0)
            {
                var points = new List<Vec3>(remaining[0].Path.Points);
                int source = remaining[0].Source;
                remaining.RemoveAt(0);

                bool grew = true;

                while (grew)
                {
                    grew = false;

                    for (int i = 0; i < remaining.Count; i++)
                    {
                        IReadOnlyList<Vec3> next = remaining[i].Path.Points;

                        if (Meets(points[points.Count - 1], next[0]))
                        {
                            points.AddRange(next.Skip(1));
                        }
                        else if (Meets(next[next.Count - 1], points[0]))
                        {
                            points.InsertRange(0, next.Take(next.Count - 1));
                        }
                        else
                        {
                            continue;
                        }

                        source = Math.Min(source, remaining[i].Source);
                        remaining.RemoveAt(i);
                        grew = true;
                        break;
                    }
                }

                bool closed = points.Count > 2 && Meets(points[points.Count - 1], points[0]);

                if (closed)
                {
                    // A closed polyline does not repeat its first point.
                    points.RemoveAt(points.Count - 1);
                }

                yield return new MergedCutterPath(new Polyline(points, closed), source);
            }
        }

        private static bool Meets(Vec3 a, Vec3 b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;

            return (dx * dx) + (dy * dy) <= Chaining.DefaultTolerance * Chaining.DefaultTolerance;
        }
    }
}
