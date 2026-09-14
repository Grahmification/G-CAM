using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
    /// <summary>
    /// Which side of the profile the lead arcs swing to.
    /// </summary>
    /// <remarks>
    /// **The bug these exist for:** the lead arc centre was hard-coded one radius to the
    /// left of travel, so on an outside profile the lead swung into the part and took a
    /// bite out of the finished wall on the way in. The whole point of a lead is that the
    /// entry mark lands off the wall, so a lead on the wrong side is worse than none.
    ///
    /// The property asserted is the one that matters and is independent of how the side is
    /// worked out: **no lead move may come closer to the profile than the cut itself does.**
    /// </remarks>
    public class Contour2dLeadSideTests
    {
        private const double ToolDiameter = 10;
        private const double LeadRadius = 4;

        /// <summary>A 100 x 60 rectangle at Z 0, counter-clockwise.</summary>
        private static Polyline Rectangle() =>
            new Polyline(
                new[]
                {
                    new Vec3(0, 0, 0),
                    new Vec3(100, 0, 0),
                    new Vec3(100, 60, 0),
                    new Vec3(0, 60, 0),
                },
                closed: true);

        private static Polyline Line() =>
            new Polyline(new[] { new Vec3(0, 0, 0), new Vec3(100, 0, 0) });

        private static Contour2dSettings Leading(CutDirection direction)
        {
            var settings = new Contour2dSettings { Direction = direction };

            settings.LeadIn.Enabled = true;
            settings.LeadIn.Radius = LeadRadius;
            settings.LeadOutMatchesLeadIn = true;

            return settings;
        }

        private static Toolpath Generate(ResolvedContour contour, Contour2dSettings settings)
        {
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
                Geometry = { Diameter = ToolDiameter, FluteLength = 30 },
            };

            var context = new GenerationContext(
                new Job { Name = "Job 1" },
                operation,
                tool,
                new ResolvedHeights(40, 35, 32, 30, 0),
                new Bounds(new Vec3(-30, -30, 0), new Vec3(130, 90, 30)),
                new[] { contour });

            return new Contour2dStrategy().Generate(context, null, CancellationToken.None);
        }

        /// <summary>
        /// How close the cut itself comes to the profile - the wall, plus whatever is being
        /// left on it. Nothing may come nearer than this.
        /// </summary>
        private static double Clearance(Contour2dSettings settings) =>
            (ToolDiameter / 2) + settings.StockToLeave;

        /// <remarks>
        /// **Plunges are checked, not just leads.** A lead-in's own end is the start of the
        /// cut, which sits at the offset distance whichever side the arc came from - so
        /// checking only lead moves tests nothing about the lead-in at all. The point the
        /// side decides is where the tool comes *down*: the far end of the entry arc, which
        /// is the plunge. That omission hid this bug on open profiles when these tests were
        /// first written.
        ///
        /// Every point of the arc between them is then safe by construction: the centre
        /// sits a radius beyond the touch-down on the free side, so the whole sweep stays
        /// at least the offset distance off the wall.
        /// </remarks>
        private static void AssertLeadsStayOffTheWall(
            Polyline profile, Contour2dSettings settings, Toolpath path)
        {
            IReadOnlyList<Move> leads =
                path.Moves.Where(m => m.Kind == MoveKind.Lead).ToList();

            IReadOnlyList<Move> plunges =
                path.Moves.Where(m => m.Kind == MoveKind.Plunge).ToList();

            Assert.NotEmpty(leads);
            Assert.NotEmpty(plunges);

            foreach (Move move in leads.Concat(plunges))
            {
                double distance = (move.End - profile.NearestPointXy(move.End)).Length;

                Assert.True(
                    distance >= Clearance(settings) - 0.01,
                    $"A {move.Kind} move ended {distance:0.###}mm from the profile, inside " +
                    $"the {Clearance(settings):0.###}mm the cut itself keeps.");
            }
        }

        private static void AssertLeadsInAndOut(Toolpath path)
        {
            // One of each per pass. A missing lead-out is silent - the cutter simply stops
            // on the finished wall and retracts up it.
            Assert.Equal(2, path.Moves.Count(m => m.Kind == MoveKind.Lead));
        }

        [Theory]
        [InlineData(CutDirection.Climb)]
        [InlineData(CutDirection.Conventional)]
        public void A_lead_never_swings_into_the_wall_on_a_closed_profile(CutDirection direction)
        {
            Contour2dSettings settings = Leading(direction);
            var contour = new ResolvedContour(Rectangle());

            AssertLeadsStayOffTheWall(Rectangle(), settings, Generate(contour, settings));
        }


        [Theory]
        [InlineData(CutDirection.Climb)]
        [InlineData(CutDirection.Conventional)]
        public void A_lead_never_swings_into_the_wall_on_an_open_profile(CutDirection direction)
        {
            Contour2dSettings settings = Leading(direction);
            var contour = new ResolvedContour(Line());

            Toolpath path = Generate(contour, settings);

            AssertLeadsStayOffTheWall(Line(), settings, path);
            AssertLeadsInAndOut(path);
        }

        [Fact]
        public void An_open_profile_gets_a_lead_out_as_well_as_a_lead_in()
        {
            // It did not: the point giving the exit direction was read as if every chain
            // were closed, so an open one produced a zero-length direction and the lead-out
            // was skipped without a word.
            Toolpath path = Generate(
                new ResolvedContour(Line()), Leading(CutDirection.Climb));

            AssertLeadsInAndOut(path);

            Move leadOut = path.Moves.Last(m => m.Kind == MoveKind.Lead);

            // It leaves from the far end of the cut, not the near one.
            Assert.True(
                leadOut.End.X > 100,
                $"The lead-out ended at X {leadOut.End.X:0.###}, not past the end of the cut.");
        }

        [Fact]
        public void Nor_when_the_contour_is_reversed()
        {
            // Reversing swaps which side is being cut, so it swaps which side is free.
            Contour2dSettings settings = Leading(CutDirection.Climb);
            var contour = new ResolvedContour(Rectangle(), reversed: true);

            AssertLeadsStayOffTheWall(Rectangle(), settings, Generate(contour, settings));
        }

        [Fact]
        public void Nor_with_stock_left_on_the_wall()
        {
            Contour2dSettings settings = Leading(CutDirection.Climb);
            settings.StockToLeave = 2;

            AssertLeadsStayOffTheWall(
                Rectangle(), settings, Generate(new ResolvedContour(Rectangle()), settings));
        }

        [Fact]
        public void The_lead_arc_turns_the_way_its_centre_lies()
        {
            // The two have to agree or the arc meets the profile from the wrong quarter.
            // An outside cut on this rectangle leads from the right of travel, so both
            // arcs sweep clockwise.
            Contour2dSettings settings = Leading(CutDirection.Climb);

            Toolpath path = Generate(new ResolvedContour(Rectangle()), settings);

            Assert.All(
                path.Moves.Where(m => m.Kind == MoveKind.Lead),
                m => Assert.True(m.Arc.Clockwise, "A lead arc swept the wrong way."));
        }

        [Fact]
        public void A_lead_still_touches_the_profile_where_the_cut_begins()
        {
            // Moving the arc to the other side must not detach it: the lead-in still has to
            // arrive exactly at the first cutting point, tangentially.
            Contour2dSettings settings = Leading(CutDirection.Climb);

            Toolpath path = Generate(new ResolvedContour(Rectangle()), settings);

            Move leadIn = path.Moves.First(m => m.Kind == MoveKind.Lead);
            Move firstCut = path.Moves.First(m => m.Kind == MoveKind.Cutting);

            // The lead ends where cutting starts, so the first cut leaves from there.
            double gap = (leadIn.End - path.Moves
                .TakeWhile(m => m != firstCut)
                .Last()
                .End).Length;

            Assert.Equal(0, gap, 6);
        }
    }
}
