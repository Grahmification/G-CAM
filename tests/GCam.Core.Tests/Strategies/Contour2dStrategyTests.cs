using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using GCam.Core.Diagnostics;
using GCam.Core.Geometry.Primitives;
using GCam.Core.Model;
using GCam.Core.Model.Heights;
using GCam.Core.Strategies;
using GCam.Core.Strategies.Contour2d;
using GCam.Core.Strategies.Shared;
using GCam.Core.Tooling;
using Xunit;

namespace GCam.Core.Tests.Strategies
{
    public class Contour2dStrategyTests
    {
        private const double ToolDiameter = 10;

        /// <summary>A 100 x 60 rectangle at Z 0, counter-clockwise.</summary>
        private static Polyline Rectangle()
        {
            return new Polyline(
                new[]
                {
                    new Vec3(0, 0, 0),
                    new Vec3(100, 0, 0),
                    new Vec3(100, 60, 0),
                    new Vec3(0, 60, 0),
                },
                closed: true);
        }

        private static GenerationContext Context(
            Contour2dSettings settings = null,
            Polyline contour = null,
            ResolvedHeights heights = null,
            double diameter = ToolDiameter)
        {
            settings = settings ?? new Contour2dSettings();

            var operation = new Operation(settings)
            {
                Name = "2D Contour1",
                ToolId = "tool-4",
                Cutting = { SpindleRpm = 7500, CuttingFeed = 1800, PlungeFeed = 600 },
            };

            var tool = new Tool
            {
                Id = "tool-4",
                Number = 4,
                Type = ToolType.FlatEndMill,
                Geometry = { Diameter = diameter, FluteLength = 30 },
            };

            return new GenerationContext(
                new Job { Name = "Job 1" },
                operation,
                tool,
                // clearance 40, retract 35, feed 32, top 30, bottom 0
                heights ?? new ResolvedHeights(40, 35, 32, 30, 0),
                new Bounds(new Vec3(-5, -5, 0), new Vec3(105, 65, 30)),
                new[] { contour ?? Rectangle() });
        }

        private static Toolpath Generate(GenerationContext context)
        {
            return new Contour2dStrategy().Generate(context, null, CancellationToken.None);
        }

        [Fact]
        public void It_produces_a_toolpath()
        {
            Toolpath path = Generate(Context());

            Assert.False(path.IsEmpty);
            Assert.Contains(path.Moves, m => m.Kind == MoveKind.Cutting);
        }

        [Fact]
        public void The_cutter_runs_a_radius_outside_the_profile()
        {
            // A 100x60 rectangle cut on the outside with a 10mm cutter puts the centre
            // 5mm out on every side: -5..105 by -5..65.
            Toolpath path = Generate(Context());

            IEnumerable<Vec3> cutting = path.Moves
                .Where(m => m.Kind == MoveKind.Cutting)
                .Select(m => m.End);

            Assert.Equal(-5, cutting.Min(p => p.X), 2);
            Assert.Equal(105, cutting.Max(p => p.X), 2);
            Assert.Equal(-5, cutting.Min(p => p.Y), 2);
            Assert.Equal(65, cutting.Max(p => p.Y), 2);
        }

        [Fact]
        public void Stock_to_leave_moves_the_cutter_further_out()
        {
            var settings = new Contour2dSettings { StockToLeave = 0.5 };

            Toolpath path = Generate(Context(settings));

            double left = path.Moves
                .Where(m => m.Kind == MoveKind.Cutting)
                .Min(m => m.End.X);

            Assert.Equal(-5.5, left, 2);
        }

        [Fact]
        public void Stock_to_leave_past_the_cutter_radius_carries_an_open_cut_to_the_other_side()
        {
            // An open path has no area to shrink, so a negative offset cannot be handed
            // to the offsetter as-is - the side is flipped and the distance made positive.
            // Climb puts the cutter to the right of travel, at Y -5 with a 10mm cutter;
            // -8mm of stock leaves 3mm on the far side instead.
            var settings = new Contour2dSettings { StockToLeave = -8 };
            var line = new Polyline(new[] { new Vec3(0, 0, 0), new Vec3(100, 0, 0) });

            Toolpath path = Generate(Context(settings, line));

            IEnumerable<Vec3> cutting = path.Moves
                .Where(m => m.Kind == MoveKind.Cutting)
                .Select(m => m.End);

            Assert.All(cutting, p => Assert.Equal(3, p.Y, 2));
        }

        [Fact]
        public void Stock_to_leave_turned_off_is_ignored_without_being_cleared()
        {
            var settings = new Contour2dSettings
            {
                StockToLeaveEnabled = false,
                StockToLeave = 0.5,
                VerticalStockToLeave = 0.3,
            };

            Toolpath path = Generate(Context(settings));

            IEnumerable<Move> cutting = path.Moves.Where(m => m.Kind == MoveKind.Cutting);

            Assert.Equal(-5, cutting.Min(m => m.End.X), 2);
            Assert.All(cutting, m => Assert.Equal(0, m.End.Z, 6));
        }

        [Fact]
        public void Cutting_happens_at_the_bottom_height()
        {
            Toolpath path = Generate(Context());

            Assert.All(
                path.Moves.Where(m => m.Kind == MoveKind.Cutting),
                m => Assert.Equal(0, m.End.Z, 6));
        }

        [Fact]
        public void Vertical_stock_to_leave_raises_the_floor()
        {
            var settings = new Contour2dSettings { VerticalStockToLeave = 0.3 };

            Toolpath path = Generate(Context(settings));

            Assert.All(
                path.Moves.Where(m => m.Kind == MoveKind.Cutting),
                m => Assert.Equal(0.3, m.End.Z, 6));
        }

        [Fact]
        public void Multiple_depths_cut_the_profile_once_per_step()
        {
            var settings = new Contour2dSettings();
            settings.MultipleDepths.Enabled = true;
            settings.MultipleDepths.MaximumStepdown = 10;

            Toolpath path = Generate(Context(settings));

            // 30mm of depth in steps of at most 10 is three passes.
            double[] depths = path.Moves
                .Where(m => m.Kind == MoveKind.Cutting)
                .Select(m => Math.Round(m.End.Z, 6))
                .Distinct()
                .OrderByDescending(z => z)
                .ToArray();

            Assert.Equal(new[] { 20.0, 10.0, 0.0 }, depths);
        }

        [Fact]
        public void The_last_pass_lands_exactly_on_the_bottom()
        {
            // A float's width above it would leave a witness ridge no operator could
            // explain, and stepdowns that do not divide evenly are the normal case.
            var settings = new Contour2dSettings();
            settings.MultipleDepths.Enabled = true;
            settings.MultipleDepths.MaximumStepdown = 7;
            settings.MultipleDepths.UseEvenStepdowns = false;

            Toolpath path = Generate(Context(settings));

            double deepest = path.Moves
                .Where(m => m.Kind == MoveKind.Cutting)
                .Min(m => m.End.Z);

            Assert.Equal(0, deepest, 9);
        }

        [Fact]
        public void Nothing_to_remove_yields_an_empty_path_rather_than_a_wrong_one()
        {
            // Stock to leave deeper than the cut. The queue turns an empty path into a
            // warning, which is the honest answer.
            var settings = new Contour2dSettings { VerticalStockToLeave = 50 };

            Toolpath path = Generate(Context(settings));

            Assert.DoesNotContain(path.Moves, m => m.Kind == MoveKind.Cutting);
        }

        [Fact]
        public void It_rapids_at_clearance_before_going_anywhere()
        {
            Toolpath path = Generate(Context());

            Assert.Equal(MoveKind.Rapid, path.Moves[0].Kind);
            Assert.Equal(40, path.Moves[0].End.Z, 6);
        }

        [Fact]
        public void It_never_rapids_below_the_feed_height()
        {
            // Rapiding down into the material is how a cutter meets a clamp.
            Toolpath path = Generate(Context());

            Assert.All(
                path.Moves.Where(m => m.Kind == MoveKind.Rapid),
                m => Assert.True(m.End.Z >= 32 - 1e-6, $"rapid to Z{m.End.Z}"));
        }

        [Fact]
        public void It_plunges_at_the_plunge_feed_not_the_cutting_feed()
        {
            Toolpath path = Generate(Context());

            Move plunge = path.Moves.First(m => m.Kind == MoveKind.Plunge);

            Assert.Equal(600, plunge.Feed, 6);
        }

        [Fact]
        public void A_feed_left_at_zero_falls_back_to_the_cutting_feed()
        {
            // G1 F0 stops the machine dead in the cut.
            GenerationContext context = Context();
            context.Operation.Cutting.PlungeFeed = 0;
            context.Operation.Cutting.RetractFeed = 0;

            Toolpath path = Generate(context);

            Assert.All(
                path.Moves.Where(m => m.IsFeedMove),
                m => Assert.True(m.Feed > 0, $"{m.Kind} at F{m.Feed}"));
        }

        [Fact]
        public void It_ends_at_clearance_rather_than_wherever_the_cut_finished()
        {
            Toolpath path = Generate(Context());

            Move last = path.Moves[path.Moves.Count - 1];

            Assert.Equal(MoveKind.Rapid, last.Kind);
            Assert.Equal(40, last.End.Z, 6);
        }

        [Fact]
        public void It_retracts_between_passes()
        {
            var settings = new Contour2dSettings();
            settings.MultipleDepths.Enabled = true;
            settings.MultipleDepths.MaximumStepdown = 10;

            Toolpath path = Generate(Context(settings));

            Assert.Equal(3, path.Moves.Count(m => m.Kind == MoveKind.Retract));
        }

        [Fact]
        public void Climb_and_conventional_run_the_profile_opposite_ways()
        {
            var climb = new Contour2dSettings { Direction = CutDirection.Climb };
            var conventional = new Contour2dSettings { Direction = CutDirection.Conventional };

            Vec3[] climbPath = Generate(Context(climb))
                .Moves.Where(m => m.Kind == MoveKind.Cutting).Select(m => m.End).ToArray();
            Vec3[] otherPath = Generate(Context(conventional))
                .Moves.Where(m => m.Kind == MoveKind.Cutting).Select(m => m.End).ToArray();

            Assert.NotEqual(
                Winding(climbPath), Winding(otherPath));
        }

        [Fact]
        public void Both_directions_still_cut_the_same_side_of_the_wall()
        {
            // The direction decides which way round, not which side: a conventional cut of
            // an outside profile is still outside it.
            var conventional = new Contour2dSettings { Direction = CutDirection.Conventional };

            Toolpath path = Generate(Context(conventional));

            IEnumerable<Vec3> cutting = path.Moves
                .Where(m => m.Kind == MoveKind.Cutting).Select(m => m.End);

            Assert.Equal(-5, cutting.Min(p => p.X), 2);
            Assert.Equal(105, cutting.Max(p => p.X), 2);
        }

        [Fact]
        public void A_lead_in_is_an_arc_when_one_is_asked_for()
        {
            var settings = new Contour2dSettings();
            settings.LeadIn.Enabled = true;
            settings.LeadIn.Radius = 3;

            Toolpath path = Generate(Context(settings));

            Assert.Contains(path.Moves, m => m.Kind == MoveKind.Lead && m.IsArc);
        }

        [Fact]
        public void The_plunge_lands_off_the_profile_when_there_is_a_lead_in()
        {
            // The bug this pins: the plunge used to land on the profile start and the
            // lead-in arc then ran from that point back to itself - a zero-length arc,
            // which the tessellator correctly reads as a full circle. So the cutter
            // plunged onto the finished wall and looped all the way round it.
            var settings = new Contour2dSettings();
            settings.LeadIn.Enabled = true;
            settings.LeadIn.Radius = 3;

            Toolpath path = Generate(Context(settings));

            Move plunge = path.Moves.First(m => m.Kind == MoveKind.Plunge);
            Move lead = path.Moves.First(m => m.Kind == MoveKind.Lead);

            // The touch-down is one radius back and one to the side: r * sqrt(2) away.
            double away = Flat(lead.End, plunge.End).Length;

            Assert.Equal(3 * Math.Sqrt(2), away, 3);
        }

        [Fact]
        public void The_lead_in_arc_is_not_a_full_circle()
        {
            var settings = new Contour2dSettings();
            settings.LeadIn.Enabled = true;
            settings.LeadIn.Radius = 3;

            Toolpath path = Generate(Context(settings));

            int leadIndex = path.Moves.ToList().FindIndex(m => m.Kind == MoveKind.Lead);
            Vec3 from = path.Moves[leadIndex - 1].End;
            Vec3 to = path.Moves[leadIndex].End;

            Assert.True(
                Flat(from, to).Length > Precision.Epsilon,
                "a lead arc that starts where it ends sweeps the whole way round");
        }

        [Fact]
        public void The_lead_in_arrives_at_the_start_of_the_cut()
        {
            var settings = new Contour2dSettings();
            settings.LeadIn.Enabled = true;
            settings.LeadIn.Radius = 3;

            Toolpath path = Generate(Context(settings));

            int leadIndex = path.Moves.ToList().FindIndex(m => m.Kind == MoveKind.Lead);
            Move firstCut = path.Moves.First(m => m.Kind == MoveKind.Cutting);

            // Whatever the lead does, it has to hand over exactly where cutting begins.
            Vec3 arrival = path.Moves[leadIndex].End;
            Vec3 cutFrom = path.Moves[path.Moves.ToList().IndexOf(firstCut) - 1].End;

            Assert.Equal(arrival, cutFrom);
        }

        [Fact]
        public void The_lead_in_turns_about_a_centre_one_radius_from_the_profile()
        {
            var settings = new Contour2dSettings();
            settings.LeadIn.Enabled = true;
            settings.LeadIn.Radius = 3;

            Toolpath path = Generate(Context(settings));

            Move lead = path.Moves.First(m => m.Kind == MoveKind.Lead);

            // Tangential arrival means the centre is exactly one radius from where it
            // lands, square to the direction of travel.
            Assert.Equal(3, Flat(lead.End, lead.Arc.Centre).Length, 6);
        }

        [Fact]
        public void Without_a_lead_in_the_cutter_plunges_on_the_profile()
        {
            // Still legitimate, and still what happens when leads are off.
            var settings = new Contour2dSettings();
            settings.LeadIn.Enabled = false;
            settings.LeadOutMatchesLeadIn = false;
            settings.LeadOut.Enabled = false;

            Toolpath path = Generate(Context(settings));

            // Nothing between going down and starting to cut - which is what "plunges on
            // the profile" means. Where that is depends on where Clipper2 chose to start
            // the offset contour, which is not the input's start point and not worth
            // asserting.
            int firstCut = path.Moves.ToList().FindIndex(m => m.Kind == MoveKind.Cutting);

            Assert.Equal(MoveKind.Plunge, path.Moves[firstCut - 1].Kind);
            Assert.Equal(0, path.Moves[firstCut - 1].End.Z, 6);
        }

        [Fact]
        public void No_lead_is_produced_when_it_is_switched_off()
        {
            var settings = new Contour2dSettings();
            settings.LeadIn.Enabled = false;
            settings.LeadOut.Enabled = false;
            settings.LeadOutMatchesLeadIn = false;

            Toolpath path = Generate(Context(settings));

            Assert.DoesNotContain(path.Moves, m => m.Kind == MoveKind.Lead);
        }

        [Fact]
        public void A_tool_with_no_diameter_is_refused_rather_than_producing_a_path_on_the_wall()
        {
            var error = Assert.Throws<GCamUserException>(
                () => Generate(Context(diameter: 0)));

            Assert.Contains("diameter", error.Message);
        }

        [Fact]
        public void An_operation_whose_contours_did_not_resolve_is_refused()
        {
            var context = new GenerationContext(
                new Job { Name = "Job 1" },
                new Operation(new Contour2dSettings()) { Name = "2D Contour1" },
                new Tool { Id = "tool-4", Geometry = { Diameter = 10 } },
                new ResolvedHeights(40, 35, 32, 30, 0),
                new Bounds(new Vec3(0, 0, 0), new Vec3(100, 60, 30)));

            var error = Assert.Throws<GCamUserException>(() => Generate(context));

            Assert.Contains("contours could be resolved", error.Message);
        }

        [Fact]
        public void Cancelling_stops_it()
        {
            var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(
                () => new Contour2dStrategy().Generate(Context(), null, cancellation.Token));
        }

        [Fact]
        public void Progress_reaches_one()
        {
            var reports = new List<double>();
            var settings = new Contour2dSettings();
            settings.MultipleDepths.Enabled = true;
            settings.MultipleDepths.MaximumStepdown = 10;

            new Contour2dStrategy().Generate(
                Context(settings), new Reporter(reports.Add), CancellationToken.None);

            Assert.Equal(1.0, reports.Last(), 6);
        }

        [Fact]
        public void The_same_context_always_gives_the_same_path()
        {
            // Strategies are pure. Regenerating an unchanged operation must not move the
            // toolpath, or "stale" could never mean anything.
            Toolpath first = Generate(Context());
            Toolpath second = Generate(Context());

            Assert.Equal(first.Moves.Count, second.Moves.Count);
            Assert.All(
                first.Moves.Zip(second.Moves, (a, b) => (a, b)),
                pair =>
                {
                    Assert.Equal(pair.a.Kind, pair.b.Kind);
                    Assert.Equal(pair.a.End.X, pair.b.End.X, 9);
                    Assert.Equal(pair.a.End.Y, pair.b.End.Y, 9);
                    Assert.Equal(pair.a.End.Z, pair.b.End.Z, 9);
                });
        }

        [Fact]
        public void The_catalogue_now_offers_a_strategy_that_can_generate()
        {
            StrategyCatalog.CreateDefault().TryGet(StrategyId.Contour2d, out StrategyDescriptor d);

            Assert.True(d.HasStrategy);
            Assert.IsType<Contour2dStrategy>(d.CreateStrategy());
        }

        /// <summary>The distance between two points in plan, ignoring depth.</summary>
        private static Vec3 Flat(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, 0);

        private static double Winding(IReadOnlyList<Vec3> points) =>
            Math.Sign(new Polyline(points, closed: true).SignedAreaXy2);

        private sealed class Reporter : IProgress<double>
        {
            private readonly Action<double> _onReport;

            public Reporter(Action<double> onReport) => _onReport = onReport;

            public void Report(double value) => _onReport(value);
        }
    }
}
