using System.Collections.Generic;
using System.Linq;
using GCam.Core.Tooling;
using Xunit;

namespace GCam.Core.Tests.Tooling
{
    public class HolderProfileTests
    {
        private static Holder TwoStage()
        {
            return new Holder
            {
                Name = "test",
                Segments =
                {
                    new HolderSegment(10, 20, 20),   // cylinder Ø20, 10 tall
                    new HolderSegment(5, 20, 30),    // cone Ø20 → Ø30, 5 tall
                },
            };
        }

        [Fact]
        public void Profile_runs_from_the_lower_face_upward()
        {
            IReadOnlyList<ProfilePoint> profile = TwoStage().GetProfile();

            Assert.Equal(0, profile.First().Height, 6);
            Assert.Equal(15, profile.Last().Height, 6);
            Assert.Equal(15, profile.Max(p => p.Radius), 6);
        }

        [Fact]
        public void A_diameter_step_becomes_a_vertical_jump()
        {
            // Upper of one segment differs from lower of the next: a collet nut shoulder.
            var holder = new Holder
            {
                Segments =
                {
                    new HolderSegment(10, 20, 20),
                    new HolderSegment(10, 40, 40),
                },
            };

            IReadOnlyList<ProfilePoint> profile = holder.GetProfile();

            // Two points share height 10 with different radii - that is the step.
            var atStep = profile.Where(p => System.Math.Abs(p.Height - 10) < 1e-9).ToList();
            Assert.Equal(2, atStep.Count);
            Assert.Contains(atStep, p => System.Math.Abs(p.Radius - 10) < 1e-9);
            Assert.Contains(atStep, p => System.Math.Abs(p.Radius - 20) < 1e-9);
        }

        [Fact]
        public void An_empty_holder_produces_an_empty_profile_rather_than_throwing()
        {
            Assert.Empty(new Holder().GetProfile());
        }

        [Fact]
        public void Real_HSM_holder_profile_matches_its_reported_extents()
        {
            Tool tool = HsmLibraryReaderTests.ReadExampleLibrary().Tools
                .Single(t => t.Id.StartsWith("7a7b06c9"));

            IReadOnlyList<ProfilePoint> profile = tool.Holder.GetProfile();

            Assert.Equal(tool.Holder.Height, profile.Max(p => p.Height), 4);
            Assert.Equal(tool.Holder.MaxDiameter / 2, profile.Max(p => p.Radius), 4);
        }

        [Fact]
        public void Stickout_is_the_body_length_leaving_the_rest_in_the_holder()
        {
            // body-length 38, overall-length 60.53 → 22.53mm gripped.
            Tool tool = HsmLibraryReaderTests.ReadExampleLibrary().Tools
                .Single(t => t.Id.StartsWith("7a7b06c9"));

            Assert.Equal(38, tool.Stickout, 6);
            Assert.Equal(22.53, tool.Geometry.OverallLength - tool.Stickout, 4);
        }

        [Fact]
        public void Stickout_never_reports_less_than_the_cutting_length()
        {
            // A holder overlapping the flutes would be nonsense, however odd the data.
            var tool = new Tool
            {
                Type = ToolType.FlatEndMill,
                Geometry = new ToolGeometry { Diameter = 10, FluteLength = 40, BodyLength = 5, FluteCount = 2 },
            };

            Assert.Equal(40, tool.Stickout, 6);
        }

        [Fact]
        public void Stickout_falls_back_when_body_length_is_missing()
        {
            var tool = new Tool
            {
                Type = ToolType.FlatEndMill,
                Geometry = new ToolGeometry { Diameter = 10, FluteLength = 20, OverallLength = 70, FluteCount = 2 },
            };

            Assert.Equal(70, tool.Stickout, 6);
        }
    }
}
