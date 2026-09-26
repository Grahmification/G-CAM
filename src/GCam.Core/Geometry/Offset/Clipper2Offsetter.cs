using System;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;
using GCam.Core.Geometry.Primitives;

namespace GCam.Core.Geometry.Offset
{
    /// <summary>
    /// Contour offsetting, done by Clipper2.
    /// </summary>
    /// <remarks>
    /// The one bought-in algorithm in the geometry kernel, and the one worth buying: an
    /// offset that removes its own self-intersections is a solved problem with a lot of
    /// edge cases, and getting it subtly wrong produces a toolpath that looks right and
    /// gouges the part.
    ///
    /// **Clipper2 works in fixed point.** Its double-precision entry points scale by a
    /// decimal precision and compute in 64-bit integers. At <see cref="Precision"/> = 4
    /// the grid is 0.0001mm - two orders finer than any machining tolerance, and nowhere
    /// near overflowing a long for parts measured in metres.
    ///
    /// **Z is carried, not computed.** Clipper2 is a 2D library; the contour's Z is taken
    /// from its first point and put back on every result. Contouring only ever offsets in
    /// plan, so nothing is lost, and pretending otherwise would invite someone to offset a
    /// ramp.
    /// </remarks>
    public sealed class Clipper2Offsetter : IContourOffsetter
    {
        /// <summary>Decimal places Clipper2 keeps. 4 is a tenth of a micron.</summary>
        public const int Precision = 4;

        public IReadOnlyList<Polyline> Offset(Polyline contour, double distance, double arcTolerance)
        {
            var results = new List<Polyline>();

            if (contour == null || contour.IsEmpty)
            {
                return results;
            }

            if (Math.Abs(distance) <= Precision_Epsilon)
            {
                // Nothing to do. Returning the contour itself rather than running it
                // through the library keeps "no offset" exact - a round trip through a
                // fixed-point grid would move points by up to half a grid step.
                results.Add(contour.WithDirection(counterClockwise: true));
                return results;
            }

            double z = contour.Points[0].Z;

            var path = new PathD(contour.Points.Select(p => new PointD(p.X, p.Y)));
            var paths = new PathsD { path };

            PathsD offset = Clipper.InflatePaths(
                paths,
                distance,
                JoinType.Round,
                EndType.Polygon,
                miterLimit: 2.0,
                precision: Precision,
                arcTolerance: Math.Max(arcTolerance, 1e-4));

            // **Clipper2 hands back the orientation it was given**: a clockwise contour
            // offsets to clockwise outlines with counter-clockwise holes. Measured on
            // 2.0.0 - Butt and Joined ends, by contrast, always come back counter-clockwise.
            // Turned round here so every region this returns has one convention, because a
            // non-zero fill over outlines running opposite ways cancels where they overlap.
            bool backwards = !contour.IsCounterClockwise;

            foreach (PathD result in offset)
            {
                if (result.Count >= 3)
                {
                    var outline = new Polyline(
                        result.Select(p => new Vec3(p.x, p.y, z)), closed: true);

                    results.Add(backwards ? outline.Reversed() : outline);
                }
            }

            return results;
        }

        /// <summary>
        /// One side of an open path, extracted from the ribbon Clipper2 offsets it into.
        /// </summary>
        /// <remarks>
        /// **Clipper2 has no single-sided offset, and neither does any other general
        /// offsetting library.** Inflating an open path with <c>EndType.Butt</c> produces a
        /// closed ribbon: the left offset, an end cap, the right offset, another end cap.
        /// That is the right answer to the question those libraries are asked and the
        /// wrong one for a cutter, which runs down one side only.
        ///
        /// So the ribbon is cut open again. The two ends of the wanted side are known
        /// exactly - they are the path's own endpoints pushed <paramref name="distance"/>
        /// along the side normal - so the ribbon is walked between the vertices nearest
        /// those two points, and of the two ways round, the one lying on the wanted side is
        /// kept. What this buys is the part worth buying: Clipper has already removed the
        /// self-intersections that a naive parallel curve produces wherever the distance
        /// exceeds the local curvature, and those are what gouge a part.
        ///
        /// Returning nothing is a legitimate answer - an offset larger than the path's own
        /// features can consume that side completely - and is preferred to guessing when
        /// neither way round looks like the side that was asked for.
        /// </remarks>
        public IReadOnlyList<Polyline> OffsetOpen(
            Polyline path, double distance, OffsetSide side, double arcTolerance)
        {
            var results = new List<Polyline>();

            if (path == null || path.IsEmpty || path.IsClosed)
            {
                return results;
            }

            if (Math.Abs(distance) <= Precision_Epsilon)
            {
                results.Add(path);
                return results;
            }

            double z = path.Points[0].Z;

            PathsD ribbon = Clipper.InflatePaths(
                new PathsD { new PathD(path.Points.Select(p => new PointD(p.X, p.Y))) },
                distance,
                JoinType.Round,
                // Butt, not Round or Square: the offset then starts and ends exactly
                // level with the path's own ends, which is what makes the two anchor
                // points below computable rather than approximate.
                EndType.Butt,
                miterLimit: 2.0,
                precision: Precision,
                arcTolerance: Math.Max(arcTolerance, 1e-4));

            PointD wantedStart = EndOffset(path, side, distance, atStart: true);
            PointD wantedEnd = EndOffset(path, side, distance, atStart: false);

            PathD outline = NearestOutline(ribbon, wantedStart);

            if (outline == null || outline.Count < 3)
            {
                return results;
            }

            int from = NearestVertex(outline, wantedStart);
            int to = NearestVertex(outline, wantedEnd);

            if (from == to)
            {
                return results;
            }

            Polyline forward = Walk(outline, from, to, forward: true, z: z);
            Polyline backward = Walk(outline, from, to, forward: false, z: z);

            if (IsOnSide(path, forward, side))
            {
                results.Add(forward);
            }
            else if (IsOnSide(path, backward, side))
            {
                results.Add(backward);
            }

            return results;
        }

        public IReadOnlyList<Polyline> Band(Polyline path, double halfWidth, double arcTolerance)
        {
            var results = new List<Polyline>();

            if (path == null || path.IsEmpty || halfWidth <= Precision_Epsilon)
            {
                return results;
            }

            double z = path.Points[0].Z;

            // Round across an open path's ends, not Butt: see the interface. The band's
            // side edges are the same either way, so the edge on the cutter's side is still
            // exactly the path OffsetOpen returns, and a cutter path clipped by it ends on
            // that one.
            PathsD band = Clipper.InflatePaths(
                new PathsD { new PathD(path.Points.Select(p => new PointD(p.X, p.Y))) },
                halfWidth,
                JoinType.Round,
                path.IsClosed ? EndType.Joined : EndType.Round,
                miterLimit: 2.0,
                precision: Precision,
                arcTolerance: Math.Max(arcTolerance, 1e-4));

            foreach (PathD outline in band)
            {
                if (outline.Count >= 3)
                {
                    results.Add(new Polyline(outline.Select(p => new Vec3(p.x, p.y, z)), closed: true));
                }
            }

            return results;
        }

        /// <remarks>
        /// **The path goes in as an open subject even when it is closed**, with its first
        /// point repeated to close it: Clipper2 clips open paths against closed regions and
        /// returns the pieces as open paths, which is the question being asked. A closed
        /// subject would be intersected as an area instead.
        ///
        /// Clipper2 makes no promise about which way round an open piece comes back, so
        /// each is compared against the path it came from and turned round if needed -
        /// direction of travel is climb or conventional, and losing it would be silent.
        /// </remarks>
        public IReadOnlyList<Polyline> Outside(Polyline path, IReadOnlyList<Polyline> region)
        {
            if (path == null || path.IsEmpty)
            {
                return new Polyline[0];
            }

            PathsD clip = ToPaths(region);

            if (clip.Count == 0)
            {
                return new[] { path };
            }

            double z = path.Points[0].Z;

            var subject = new PathD(path.Points.Select(p => new PointD(p.X, p.Y)));

            if (path.IsClosed)
            {
                subject.Add(subject[0]);
            }

            // Collinear points kept: every vertex that survives is one of the path's own,
            // so an untouched stretch comes back exactly as it went in.
            var clipper = new ClipperD(Precision) { PreserveCollinear = true };
            clipper.AddOpenSubject(subject);
            clipper.AddClip(clip);

            var closedResult = new PathsD();
            var openResult = new PathsD();
            clipper.Execute(ClipType.Difference, FillRule.NonZero, closedResult, openResult);

            List<Polyline> pieces = openResult
                .Where(p => p.Count >= 2)
                .Select(p => new Polyline(p.Select(q => new Vec3(q.x, q.y, z))))
                .Where(p => p.Length > Precision_Epsilon)
                .ToList();

            if (pieces.Count == 1 && Math.Abs(pieces[0].Length - path.Length) <= UntouchedTolerance)
            {
                // Nothing was taken. The path itself rather than the copy: a closed one
                // stays closed, and nothing is moved by the round trip to the grid.
                return new[] { path };
            }

            return pieces.Select(p => RunsWith(path, p) ? p : p.Reversed()).ToList();
        }

        public IReadOnlyList<Polyline> Subtract(IReadOnlyList<Polyline> region, IReadOnlyList<Polyline> minus)
        {
            List<Polyline> outlines = (region ?? new Polyline[0]).Where(r => r != null && r.Count >= 3).ToList();

            if (outlines.Count == 0)
            {
                return new Polyline[0];
            }

            double z = outlines[0].Points[0].Z;

            // A boolean's solution has outlines counter-clockwise and holes clockwise
            // whatever it was given, which is the convention everything here returns.
            PathsD difference = Clipper.Difference(
                ToPaths(outlines), ToPaths(minus), FillRule.NonZero, Precision);

            return difference
                .Where(p => p.Count >= 3)
                .Select(p => new Polyline(p.Select(q => new Vec3(q.x, q.y, z)), closed: true))
                .ToList();
        }

        private static PathsD ToPaths(IEnumerable<Polyline> region) =>
            new PathsD(
                (region ?? new Polyline[0])
                .Where(r => r != null && r.Count >= 3)
                .Select(r => new PathD(r.Points.Select(p => new PointD(p.X, p.Y)))));

        /// <summary>
        /// Whether a piece clipped from a path runs the same way as it, judged on the
        /// piece's longest segment against the nearest segment of the path.
        /// </summary>
        private static bool RunsWith(Polyline path, Polyline piece)
        {
            int longest = 0;
            double best = -1;

            for (int i = 0; i < piece.SegmentCount; i++)
            {
                double length = (piece.EndOfSegment(i) - piece[i]).Length;

                if (length > best)
                {
                    best = length;
                    longest = i;
                }
            }

            Vec3 a = piece[longest];
            Vec3 b = piece.EndOfSegment(longest);
            var middle = new Vec3((a.X + b.X) / 2, (a.Y + b.Y) / 2, a.Z);

            double nearest = double.MaxValue;
            double along = 0;

            for (int i = 0; i < path.SegmentCount; i++)
            {
                Vec3 p = path[i];
                Vec3 q = path.EndOfSegment(i);

                double distance = SquaredDistanceToSegment(p, q, middle, out _, out _);

                if (distance < nearest)
                {
                    nearest = distance;
                    along = ((b.X - a.X) * (q.X - p.X)) + ((b.Y - a.Y) * (q.Y - p.Y));
                }
            }

            return along >= 0;
        }

        /// <summary>
        /// How close a single clipped piece's length must be to the whole path's for
        /// nothing to have been taken, mm. Ten grid steps: the round trip moves no vertex,
        /// so anything more is a real piece gone.
        /// </summary>
        private const double UntouchedTolerance = 1e-3;

        /// <summary>
        /// Where the offset of <paramref name="side"/> begins or ends: an endpoint of the
        /// path pushed sideways from the segment that meets it.
        /// </summary>
        private static PointD EndOffset(
            Polyline path, OffsetSide side, double distance, bool atStart)
        {
            int last = path.Count - 1;

            Vec3 at = atStart ? path[0] : path[last];
            Vec3 a = atStart ? path[0] : path[last - 1];
            Vec3 b = atStart ? path[1] : path[last];

            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            double length = Math.Sqrt((dx * dx) + (dy * dy));

            if (length <= double.Epsilon)
            {
                return new PointD(at.X, at.Y);
            }

            // Left of travel is a quarter turn counter-clockwise from it.
            double nx = -dy / length;
            double ny = dx / length;

            if (side == OffsetSide.Right)
            {
                nx = -nx;
                ny = -ny;
            }

            return new PointD(at.X + (nx * distance), at.Y + (ny * distance));
        }

        private static PathD NearestOutline(PathsD ribbon, PointD to)
        {
            PathD best = null;
            double bestDistance = double.MaxValue;

            foreach (PathD candidate in ribbon)
            {
                int vertex = NearestVertex(candidate, to);

                if (vertex < 0)
                {
                    continue;
                }

                double distance = SquaredDistance(candidate[vertex], to);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            return best;
        }

        private static int NearestVertex(PathD outline, PointD to)
        {
            int best = -1;
            double bestDistance = double.MaxValue;

            for (int i = 0; i < outline.Count; i++)
            {
                double distance = SquaredDistance(outline[i], to);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }

            return best;
        }

        /// <summary>The outline from one vertex to another, one way round or the other.</summary>
        private static Polyline Walk(PathD outline, int from, int to, bool forward, double z)
        {
            var points = new List<Vec3>();
            int count = outline.Count;
            int at = from;

            for (int step = 0; step <= count; step++)
            {
                points.Add(new Vec3(outline[at].x, outline[at].y, z));

                if (at == to)
                {
                    break;
                }

                at = forward ? (at + 1) % count : ((at - 1) + count) % count;
            }

            return new Polyline(points);
        }

        /// <summary>
        /// Whether a candidate lies on the wanted side of the path.
        /// </summary>
        /// <remarks>
        /// Both ways round the ribbon sit <paramref name="side"/>-distance from the path,
        /// so distance cannot tell them apart - only the sign can. The test is taken at the
        /// candidate's midpoint rather than an end, because the ends are the anchors and
        /// sit on the boundary between the two.
        /// </remarks>
        private static bool IsOnSide(Polyline path, Polyline candidate, OffsetSide side)
        {
            if (candidate == null || candidate.IsEmpty)
            {
                return false;
            }

            // Several probes rather than one, each voting equally. A single sample can land
            // somewhere ambiguous - on an end cap, or where two segments are equidistant -
            // and one bad reading would then decide the whole answer.
            double votes = 0;

            foreach (Vec3 probe in Probes(candidate))
            {
                votes += SideOf(path, probe);
            }

            return side == OffsetSide.Left ? votes > 0 : votes < 0;
        }

        /// <summary>Interior samples along a candidate, avoiding both anchored ends.</summary>
        private static IEnumerable<Vec3> Probes(Polyline candidate)
        {
            const int Samples = 9;

            if (candidate.Count <= 2)
            {
                yield return candidate[candidate.Count / 2];
                yield break;
            }

            for (int i = 1; i <= Samples; i++)
            {
                yield return candidate[i * (candidate.Count - 1) / (Samples + 1)];
            }
        }

        /// <summary>Which side of the path a point lies on: +1 left, -1 right.</summary>
        /// <remarks>
        /// Measured from the <b>nearest point</b> on the nearest segment, not from that
        /// segment's start. At a corner the nearest point is the corner itself and the
        /// vector out to the probe is radial, which still gives the right sign - whereas
        /// anchoring at the segment start makes every probe outside a convex corner look
        /// like it is off the end of the path, where the sign means nothing. That mistake
        /// cost this method every outside corner it was given.
        /// </remarks>
        private static double SideOf(Polyline path, Vec3 probe)
        {
            double bestDistance = double.MaxValue;
            double cross = 0;

            for (int i = 0; i < path.SegmentCount; i++)
            {
                Vec3 a = path[i];
                Vec3 b = path.EndOfSegment(i);

                double distance = SquaredDistanceToSegment(a, b, probe, out double qx, out double qy);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    cross = ((b.X - a.X) * (probe.Y - qy)) - ((b.Y - a.Y) * (probe.X - qx));
                }
            }

            // Signed, not scaled: a long segment must not outvote a short one.
            return Math.Sign(cross);
        }

        private static double SquaredDistanceToSegment(
            Vec3 a, Vec3 b, Vec3 p, out double qx, out double qy)
        {
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            double lengthSquared = (dx * dx) + (dy * dy);

            double at = lengthSquared <= double.Epsilon
                ? 0
                : (((p.X - a.X) * dx) + ((p.Y - a.Y) * dy)) / lengthSquared;

            at = Math.Max(0, Math.Min(1, at));

            qx = a.X + (at * dx);
            qy = a.Y + (at * dy);

            return ((p.X - qx) * (p.X - qx)) + ((p.Y - qy) * (p.Y - qy));
        }

        private static double SquaredDistance(PointD a, PointD b) =>
            ((a.x - b.x) * (a.x - b.x)) + ((a.y - b.y) * (a.y - b.y));

        /// <summary>
        /// Below this an offset is no offset. One grid step of
        /// <see cref="Precision"/>, because a smaller distance cannot be represented
        /// anyway.
        /// </summary>
        private const double Precision_Epsilon = 1e-4;
    }
}
