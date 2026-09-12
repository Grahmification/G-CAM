using System.Linq;
using GCam.Core.Tooling;
using Xunit;

namespace GCam.Core.Tests.Tooling
{
    public class ToolDisplayNameTests
    {
        private static Tool Make(ToolType type, ToolGeometry geometry)
        {
            return new Tool { Type = type, Geometry = geometry };
        }

        [Fact]
        public void End_mills_read_as_diameter_then_type()
        {
            Tool flat = Make(
                ToolType.FlatEndMill,
                new ToolGeometry { Diameter = 12.7, FluteLength = 30, FluteCount = 3 });

            Assert.Equal("Ø12.7 flat end mill", flat.DisplayName);
        }

        [Fact]
        public void A_bull_nose_shows_its_corner_radius_because_that_is_what_distinguishes_it()
        {
            Tool bull = Make(
                ToolType.BullNoseEndMill,
                new ToolGeometry { Diameter = 10, CornerRadius = 2, FluteLength = 30, FluteCount = 4 });

            Assert.Equal("Ø10 R2 bull nose end mill", bull.DisplayName);
        }

        [Fact]
        public void A_ball_nose_does_not_repeat_its_radius_since_it_is_always_half_the_diameter()
        {
            Tool ball = Make(
                ToolType.BallEndMill,
                new ToolGeometry { Diameter = 6, CornerRadius = 3, FluteLength = 30, FluteCount = 2 });

            Assert.Equal("Ø6 ball end mill", ball.DisplayName);
        }

        [Fact]
        public void Conical_tools_show_their_point_angle()
        {
            Tool drill = Make(
                ToolType.Drill,
                new ToolGeometry { Diameter = 2.5, TipAngle = 118, FluteLength = 35, FluteCount = 2 });

            Assert.Equal("Ø2.5 118° drill", drill.DisplayName);
        }

        [Fact]
        public void A_tap_shows_its_thread_pitch()
        {
            Tool tap = Make(
                ToolType.Tap,
                new ToolGeometry { Diameter = 3, ThreadPitch = 0.5, FluteLength = 14.5, FluteCount = 2 });

            Assert.Equal("Ø3 × 0.5 tap", tap.DisplayName);
        }

        [Fact]
        public void Every_tool_in_the_real_library_gets_a_sensible_label()
        {
            foreach (Tool tool in HsmLibraryReaderTests.ReadExampleLibrary().Tools)
            {
                string label = tool.DisplayName;

                Assert.False(string.IsNullOrWhiteSpace(label));
                Assert.StartsWith("Ø", label);
                Assert.DoesNotContain("  ", label);
            }
        }

        [Fact]
        public void The_label_says_what_the_tool_is_where_the_description_may_not()
        {
            // "Aluminum" is what the library author typed; it says what the tool is for,
            // not what it is. This is the whole reason the column exists.
            Tool tool = HsmLibraryReaderTests.ReadExampleLibrary().Tools
                .Single(t => t.Id.StartsWith("7a7b06c9"));

            Assert.Equal("Aluminum", tool.Name);
            Assert.Equal("Ø12.7 flat end mill", tool.DisplayName);
        }

        [Fact]
        public void The_label_is_searchable()
        {
            Tool bull = Make(
                ToolType.BullNoseEndMill,
                new ToolGeometry { Diameter = 10, CornerRadius = 2, FluteLength = 30, FluteCount = 4 });

            Assert.True(ToolSearch.Matches(bull, "bull nose"));
            Assert.True(ToolSearch.Matches(bull, "R2"));
        }

        [Fact]
        public void Missing_geometry_does_not_throw()
        {
            Assert.False(string.IsNullOrWhiteSpace(new Tool { Geometry = null }.DisplayName));
        }
    }
}
