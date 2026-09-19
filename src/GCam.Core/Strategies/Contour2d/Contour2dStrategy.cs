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
    /// 2D contouring: follow a profile at a series of depths, with the cutter offset to
    /// one side of it.
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
    /// **Open profiles cut as well as closed ones.** Several separate fragments are cut as
    /// several passes, each with its own lead-in, lead-out and retract - the tool does not
    /// stay down to link between them, which needs a rule for when a link would gouge.
    ///
    /// **Not yet implemented, and deliberately visible as gaps rather than as wrong
    /// numbers:** ramped entry, multiple finishing passes, tabs, chamfering, rest
    /// machining, and staying down between fragments. Each is a parameter that is read and
    /// then refused in <see cref="Contour2dSettings.Validate"/> or reported here.
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

            IReadOnlyList<ResolvedContour> profiles = context.Contours;

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
            Vec3 first = profiles[0].Path.Points[0];
            path.Add(Move.Rapid(new Vec3(first.X, first.Y, heights.Clearance)));

            int step = 0;
            int total = Math.Max(1, profiles.Count * depths.Count);

            foreach (ResolvedContour profile in profiles)
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
            double bottom = heights.Bottom + settings.EffectiveVerticalStockToLeave;
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
            ResolvedContour profile,
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

            // Which way the leads may swing. Worked out once per pass from where the wall
            // is, and used by both the entry and the exit arc.
            bool turnLeft = LeadTurnsLeft(profile.Path, cutterPath)
                            != CutterHasCrossedOver(radius, settings);

            LeadSettings leadIn = settings.LeadIn;
            var entryArc = default(LeadArc);

            bool leadingIn = leadIn.Enabled
                             && leadIn.Radius > Precision.Epsilon
                             && TryLeadIn(atDepth, leadIn.Radius, turnLeft, out entryArc);

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
                    new ArcData(entryArc.Centre, clockwise: !turnLeft)));
            }

            foreach (Move move in ProfileMoves(atDepth, cutting.CuttingFeed))
            {
                path.Add(move);
            }

            LeadSettings leadOut = settings.EffectiveLeadOut;
            var exitArc = default(LeadArc);

            if (leadOut.Enabled
                && leadOut.Radius > Precision.Epsilon
                && TryLeadOut(atDepth, leadOut.Radius, turnLeft, out exitArc))
            {
                path.Add(Move.Lead(
                    exitArc.Away,
                    Feed(cutting.ExitFeed, cutting.CuttingFeed),
                    new ArcData(exitArc.Centre, clockwise: !turnLeft)));
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
        /// The offset is the cutter's radius plus whatever is being left on the wall.
        /// Which side it lands on is decided differently for the two kinds of contour,
        /// because a closed one has an inside and an open one does not:
        ///
        /// - **Closed.** Orientation carries the side. The contour is run
        ///   counter-clockwise for a climb cut and clockwise otherwise, and a single
        ///   positive offset then lands the cutter correctly without a sign to get
        ///   backwards.
        /// - **Open.** There is no inside, so orientation says nothing and the side is
        ///   named outright. Climb puts the material on the left of travel - a cutter
        ///   turning clockwise seen from above then has its edge moving with the feed at
        ///   the point of contact, which is what climb means - so the cutter centre goes
        ///   to the right.
        ///
        /// <see cref="ResolvedContour.Reversed"/> flips whichever of those applies, and is
        /// applied last so it always wins: for a closed profile that swaps inside for
        /// outside, and for an open one it swaps hands.
        ///
        /// All of that lives in <see cref="Contour2dOffsetting"/> rather than here,
        /// because the cut-direction arrows on the Geometry tab have to land on the same
        /// side as this does. Only the distance is decided here.
        /// </remarks>
        private Polyline OffsetForCutter(
            ResolvedContour profile, double radius, Contour2dSettings settings)
        {
            return Contour2dOffsetting.Offset(
                profile,
                settings.Direction == CutDirection.Climb,
                CutterOffset(radius, settings),
                _offsetter,
                ArcTolerance);
        }

        /// <summary>How far the cutter centre runs from the profile, and on which side.</summary>
        private static double CutterOffset(double radius, Contour2dSettings settings) =>
            radius + settings.EffectiveStockToLeave;

        /// <summary>
        /// True when stock to leave is negative enough to carry the cutter through the
        /// profile and out the other side.
        /// </summary>
        /// <remarks>
        /// <b>The leads keep the side they would have had at zero stock to leave.</b>
        /// <see cref="LeadTurnsLeft"/> reads the side off the geometry - it asks where the
        /// wall is, seen from the cutter path - which is exactly right until the cutter
        /// crosses the profile, at which point the wall appears on the other side and the
        /// leads follow it across. What that looked like was a lead flipping hands the
        /// moment radial stock to leave passed -radius, while everything else about the
        /// pass simply moved further over.
        ///
        /// Deriving the crossing is safe where deriving the *side* would not be: this is
        /// one sign, known exactly from the distance the offset was asked for, and it says
        /// nothing about climb, reverse or which hand an open path takes. Measuring it
        /// instead would mean offsetting a second time to somewhere the cutter is not.
        /// </remarks>
        private static bool CutterHasCrossedOver(double radius, Contour2dSettings settings) =>
            CutterOffset(radius, settings) < 0;

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
        private static bool TryLeadIn(
            Polyline profile, double radius, bool turnLeft, out LeadArc arc)
        {
            arc = default(LeadArc);

            Vec3 at = profile[0];
            Vec3 along = Direction(at, profile[1 % profile.Count]);

            if (along.Length <= Precision.Epsilon)
            {
                return false;
            }

            Vec3 centre = at + (Sideways(along, turnLeft) * radius);

            arc = new LeadArc { Centre = centre, Away = centre - (along * radius) };
            return true;
        }

        /// <summary>
        /// The arc that takes the cutter off the end of the profile.
        /// </summary>
        private static bool TryLeadOut(
            Polyline profile, double radius, bool turnLeft, out LeadArc arc)
        {
            arc = default(LeadArc);

            Vec3 at = LastPointOf(profile);
            Vec3 along = Direction(SecondLastPointOf(profile), at);

            if (along.Length <= Precision.Epsilon)
            {
                return false;
            }

            Vec3 centre = at + (Sideways(along, turnLeft) * radius);

            arc = new LeadArc { Centre = centre, Away = centre + (along * radius) };
            return true;
        }

        /// <summary>
        /// Ninety degrees to one side of travel - the side the lead arc turns about.
        /// </summary>
        /// <remarks>
        /// A centre to the left is swept counter-clockwise and one to the right clockwise,
        /// which is what keeps the arc meeting the profile tangentially either way. Both
        /// <see cref="Move.Lead"/> calls take their sense from the same flag for that
        /// reason.
        /// </remarks>
        private static Vec3 Sideways(Vec3 along, bool left) =>
            left ? new Vec3(-along.Y, along.X, 0) : new Vec3(along.Y, -along.X, 0);

        /// <summary>
        /// Which way a lead may swing: away from the wall, never into it.
        /// </summary>
        /// <remarks>
        /// **Measured, not derived from the settings.** Where the material sits relative to
        /// the cutter's travel moves with the climb/conventional choice, with whether the
        /// contour was reversed, and - for an open path - with the side chosen outright.
        /// Deriving it means reproducing all three rules here and keeping them in step with
        /// the offsetting code forever. The wall is simply wherever the profile is, seen
        /// from the cutter path that was offset from it, so reading it off the geometry is
        /// both shorter and immune to the next rule anyone adds.
        ///
        /// A lead turns towards the side the wall is not on.
        ///
        /// This was wrong until 2026-09-13: the arc centre was hard-coded one radius to the
        /// left of travel, so on every outside profile the lead swung into the part and cut
        /// a bite out of it on the way in.
        /// </remarks>
        private static bool LeadTurnsLeft(Polyline profile, Polyline cutterPath)
        {
            Vec3 at = cutterPath[0];
            Vec3 along = Direction(at, cutterPath[1 % cutterPath.Count]);

            if (along.Length <= Precision.Epsilon)
            {
                return false;
            }

            Vec3 wall = profile.NearestPointXy(at);

            double cross = (along.X * (wall.Y - at.Y)) - (along.Y * (wall.X - at.X));

            // Dead ahead means the offset collapsed to nothing useful - there is no side to
            // prefer, so take the one an outside profile wants.
            return cross < -Precision.Epsilon;
        }

        private static Vec3 LastPointOf(Polyline profile) =>
            profile.IsClosed ? profile[0] : profile[profile.Count - 1];

        /// <summary>
        /// The point before <see cref="LastPointOf"/>, which is what gives the direction
        /// the cutter is travelling as it leaves the profile.
        /// </summary>
        /// <remarks>
        /// Different for the two kinds of chain, and getting it wrong is silent. A closed
        /// contour ends back at its first point, so the one before that is the last in the
        /// list; an open one ends at the last in the list, so the one before is the one
        /// before that.
        ///
        /// Returning the list's last point for both - which it did until 2026-09-13 - makes
        /// an open profile's lead-out start and end at the same place. The direction comes
        /// out zero-length, <see cref="TryLeadOut"/> refuses, and the operation quietly
        /// gets no lead-out at all: no error, just a cutter that stops dead on the finished
        /// wall and retracts up it.
        /// </remarks>
        private static Vec3 SecondLastPointOf(Polyline profile) =>
            profile.IsClosed ? profile[profile.Count - 1] : profile[profile.Count - 2];

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
