using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using GCam.Core.Diagnostics;
using GCam.Core.Geometry.Offset;
using GCam.Core.Geometry.Primitives;
using GCam.Core.Model;
using GCam.Core.Model.Heights;
using GCam.Core.Strategies.Shared;
using GCam.Core.Tooling;

namespace GCam.Core.Strategies.Contour2d
{
    /// <summary>
    /// 2D contouring: follow a closed profile at a series of depths, with the cutter
    /// offset to one side of it.
    /// </summary>
    /// <remarks>
    /// The first strategy that computes anything, and the shape the others follow: take a
    /// resolved context, produce a <see cref="Toolpath"/>, touch nothing else. Pure - the
    /// same context always gives the same path.
    ///
    /// What one pass looks like, in order:
    ///
    /// 1. rapid across at clearance height, then down to the feed height above the start
    /// 2. plunge or lead down to the pass depth
    /// 3. lead in, cut the profile, lead out
    /// 4. retract to the retract height
    ///
    /// Depths come from <see cref="MultipleDepthsSettings"/>; each one repeats the above.
    /// Between passes the tool retracts, because a contour is not guaranteed to be able to
    /// stay down - the profile may pass outside the stock.
    ///
    /// **Not yet implemented, and deliberately visible as gaps rather than as wrong
    /// numbers:** open contours (only closed profiles cut), ramped entry, multiple
    /// finishing passes, tabs, chamfering, and rest machining. Each is a parameter that is
    /// read and then refused in <see cref="Contour2dSettings.Validate"/> or reported here.
    /// </remarks>
    public sealed class Contour2dStrategy : IToolpathStrategy
    {
        private readonly IContourOffsetter _offsetter;

        public Contour2dStrategy(IContourOffsetter offsetter = null)
        {
            _offsetter = offsetter ?? new Clipper2Offsetter();
        }

        public StrategyId Id => StrategyId.Contour2d;

        public Toolpath Generate(
            GenerationContext context,
            IProgress<double> progress,
            CancellationToken cancellation)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            Contour2dSettings settings = context.SettingsAs<Contour2dSettings>();
            ResolvedHeights heights = context.Heights;

            IReadOnlyList<Polyline> profiles = context.Contours;

            if (profiles == null || profiles.Count == 0)
            {
                throw new GCamUserException(
                    "None of this operation's contours could be resolved in the model.");
            }

            double radius = CutterRadius(context.Tool);

            if (radius <= Precision.Epsilon)
            {
                throw new GCamUserException(
                    $"'{context.Tool.DisplayName}' has no diameter, so its path cannot be offset.");
            }

            IReadOnlyList<double> depths = PassDepths(settings, heights);

            var path = new Toolpath();
            var start = new Vec3(profiles[0].Points[0].X, profiles[0].Points[0].Y, heights.Clearance);
            path.Add(Move.Rapid(start));

            int step = 0;
            int total = Math.Max(1, profiles.Count * depths.Count);

            foreach (Polyline profile in profiles)
            {
                foreach (double depth in depths)
                {
                    cancellation.ThrowIfCancellationRequested();

                    CutOnePass(path, profile, depth, radius, settings, heights, context.Cutting);

                    progress?.Report((double)++step / total);
                }
            }

            // Leave the tool somewhere safe rather than wherever the last pass ended.
            if (path.Moves.Count > 1)
            {
                Vec3 last = path.Moves[path.Moves.Count - 1].End;
                path.Add(Move.Rapid(new Vec3(last.X, last.Y, heights.Clearance)));
            }

            return path;
        }

        /// <summary>
        /// The depths each pass cuts at, deepest last.
        /// </summary>
        /// <remarks>
        /// One pass at the bottom when multiple depths are off. When they are on, the
        /// stepdowns come from the shared settings so that every strategy splits a depth
        /// the same way - including the "even" rule, which exists because a final pass of
        /// 0.05mm loads the cutter differently from every pass before it.
        ///
        /// Vertical stock to leave raises the *bottom*: material left on the floor.
        /// </remarks>
        private static IReadOnlyList<double> PassDepths(
            Contour2dSettings settings, ResolvedHeights heights)
        {
            double bottom = heights.Bottom + settings.VerticalStockToLeave;
            double depth = heights.Top - bottom;

            if (depth <= Precision.Epsilon)
            {
                // Stock to leave deeper than the cut. Nothing to remove is a legitimate
                // answer; the queue reports an empty path as a warning.
                return new double[0];
            }

            IReadOnlyList<double> steps = settings.MultipleDepths.Stepdowns(depth);

            if (steps.Count == 0)
            {
                return new[] { bottom };
            }

            var depths = new List<double>();
            double z = heights.Top;

            foreach (double stepdown in steps)
            {
                z -= stepdown;
                depths.Add(z);
            }

            // The last pass lands exactly on the bottom rather than a float's width above
            // it, which would leave a witness ridge no operator could explain.
            depths[depths.Count - 1] = bottom;

            return depths;
        }

        private void CutOnePass(
            Toolpath path,
            Polyline profile,
            double depth,
            double radius,
            Contour2dSettings settings,
            ResolvedHeights heights,
            CuttingData cutting)
        {
            Polyline cutterPath = OffsetForCutter(profile, radius, settings);

            if (cutterPath == null || cutterPath.IsEmpty)
            {
                return;
            }

            Polyline atDepth = cutterPath.AtZ(depth);
            Vec3 profileStart = atDepth.Points[0];

            LeadSettings leadIn = settings.LeadIn;
            var entryArc = default(LeadArc);

            bool leadingIn = leadIn.Enabled
                             && leadIn.Radius > Precision.Epsilon
                             && TryLeadIn(atDepth, leadIn.Radius, out entryArc);

            // **The tool goes down off the profile when there is a lead.** That is the
            // whole point of one: plunging onto the wall leaves the entry mark exactly
            // where the finished surface is.
            Vec3 entry = leadingIn ? entryArc.Away : profileStart;

            // Across at clearance, down to feed height, then into the material. Rapiding
            // straight to depth is how a cutter meets a clamp.
            path.Add(Move.Rapid(new Vec3(entry.X, entry.Y, heights.Clearance)));
            path.Add(Move.Rapid(new Vec3(entry.X, entry.Y, heights.Feed)));
            path.Add(Move.Plunge(entry, Feed(cutting.PlungeFeed, cutting.CuttingFeed)));

            if (leadingIn)
            {
                // A quarter turn from the plunge point onto the profile, tangential where
                // it arrives, so the cutter is already moving along the wall.
                path.Add(Move.Lead(
                    profileStart,
                    Feed(cutting.EntryFeed, cutting.CuttingFeed),
                    new ArcData(entryArc.Centre, clockwise: false)));
            }

            foreach (Move move in ProfileMoves(atDepth, cutting.CuttingFeed))
            {
                path.Add(move);
            }

            LeadSettings leadOut = settings.EffectiveLeadOut;
            var exitArc = default(LeadArc);

            if (leadOut.Enabled
                && leadOut.Radius > Precision.Epsilon
                && TryLeadOut(atDepth, leadOut.Radius, out exitArc))
            {
                path.Add(Move.Lead(
                    exitArc.Away,
                    Feed(cutting.ExitFeed, cutting.CuttingFeed),
                    new ArcData(exitArc.Centre, clockwise: false)));
            }

            Vec3 end = path.Moves[path.Moves.Count - 1].End;
            path.Add(Move.Retract(
                new Vec3(end.X, end.Y, heights.Retract),
                Feed(cutting.RetractFeed, cutting.CuttingFeed)));
        }

        /// <summary>
        /// The profile moved sideways so the cutter's edge runs along it.
        /// </summary>
        /// <remarks>
        /// The offset is the cutter's radius plus whatever is being left on the wall. Its
        /// direction comes from which way the contour runs: a contour is oriented
        /// counter-clockwise for a climb cut and clockwise otherwise, so a single positive
        /// offset always lands the cutter on the correct side.
        /// </remarks>
        private Polyline OffsetForCutter(
            Polyline profile, double radius, Contour2dSettings settings)
        {
            bool counterClockwise = settings.Direction == CutDirection.Climb;
            Polyline oriented = profile.WithDirection(counterClockwise);

            double distance = radius + settings.StockToLeave;

            IReadOnlyList<Polyline> offset = _offsetter.Offset(
                oriented, distance, ArcTolerance);

            if (offset.Count == 0)
            {
                return null;
            }

            // A pinched shape can offset into several. The longest is the one that is
            // recognisably the profile; the rest are slivers left by the pinch.
            return offset.OrderByDescending(p => p.Length).First().WithDirection(counterClockwise);
        }

        private static IEnumerable<Move> ProfileMoves(Polyline profile, double feed)
        {
            for (int i = 1; i < profile.Count; i++)
            {
                yield return Move.Cut(profile[i], feed);
            }

            if (profile.IsClosed)
            {
                // Back to where it started. A closed contour does not repeat its first
                // point, so the closing move has to be added rather than walked to.
                yield return Move.Cut(profile[0], feed);
            }
        }

        /// <summary>
        /// A quarter-turn lead: where it touches down away from the profile, and what it
        /// turns about.
        /// </summary>
        private struct LeadArc
        {
            /// <summary>The end of the lead that is off the profile.</summary>
            public Vec3 Away;

            public Vec3 Centre;
        }

        /// <summary>
        /// The arc that brings the cutter onto the start of the profile.
        /// </summary>
        /// <remarks>
        /// Tangential where it arrives, so the cutter is already travelling along the wall
        /// when it reaches it - which is what keeps the entry mark off the finished
        /// surface.
        ///
        /// A quarter turn of the lead radius. The centre sits one radius to the left of
        /// travel at the profile start, and the arc begins one radius back along the
        /// approach from there - so the touch-down point is r&#8730;2 away from the profile,
        /// diagonally back and to the side.
        ///
        /// The sweep and perpendicular settings are read but not honoured: a quarter turn
        /// is what comes out. A wrong arc would be worse than a plain one.
        /// </remarks>
        private static bool TryLeadIn(Polyline profile, double radius, out LeadArc arc)
        {
            arc = default(LeadArc);

            Vec3 at = profile[0];
            Vec3 along = Direction(at, profile[1 % profile.Count]);

            if (along.Length <= Precision.Epsilon)
            {
                return false;
            }

            Vec3 centre = at + (Left(along) * radius);

            arc = new LeadArc { Centre = centre, Away = centre - (along * radius) };
            return true;
        }

        /// <summary>
        /// The arc that takes the cutter off the end of the profile.
        /// </summary>
        private static bool TryLeadOut(Polyline profile, double radius, out LeadArc arc)
        {
            arc = default(LeadArc);

            Vec3 at = LastPointOf(profile);
            Vec3 along = Direction(SecondLastPointOf(profile), at);

            if (along.Length <= Precision.Epsilon)
            {
                return false;
            }

            Vec3 centre = at + (Left(along) * radius);

            arc = new LeadArc { Centre = centre, Away = centre + (along * radius) };
            return true;
        }

        /// <summary>
        /// Ninety degrees left of travel, which is the side a counter-clockwise arc turns
        /// about to meet the path tangentially.
        /// </summary>
        private static Vec3 Left(Vec3 along) => new Vec3(-along.Y, along.X, 0);

        private static Vec3 LastPointOf(Polyline profile) =>
            profile.IsClosed ? profile[0] : profile[profile.Count - 1];

        private static Vec3 SecondLastPointOf(Polyline profile) =>
            profile[profile.Count - 1];

        private static Vec3 Direction(Vec3 from, Vec3 to)
        {
            var delta = new Vec3(to.X - from.X, to.Y - from.Y, 0);

            return delta.Length <= Precision.Epsilon ? delta : delta.Normalised();
        }

        /// <summary>
        /// The cutter's radius, or zero when the tool has no diameter to speak of.
        /// </summary>
        private static double CutterRadius(Tool tool) => (tool?.Geometry?.Diameter ?? 0) / 2.0;

        /// <summary>
        /// A feed, falling back to the cutting feed when the specific one was left at zero.
        /// </summary>
        /// <remarks>
        /// Zero in a feed field means "not set", not "do not move". Emitting G1 F0 would
        /// stop the machine dead in the cut.
        /// </remarks>
        private static double Feed(double specific, double cutting) =>
            specific > Precision.Epsilon ? specific : cutting;

        /// <summary>How closely an offset's rounded corners follow a true arc, mm.</summary>
        private const double ArcTolerance = 0.01;
    }
}
