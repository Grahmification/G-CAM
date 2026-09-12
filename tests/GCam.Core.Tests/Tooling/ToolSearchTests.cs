using GCam.Core.Tooling;
using Xunit;

namespace GCam.Core.Tests.Tooling
{
    public class ToolSearchTests
    {
        private static Tool Sample(
            string name = "Aluminum rougher",
            ToolType type = ToolType.FlatEndMill,
            int number = 4,
            double diameter = 12.7)
        {
            return new Tool
            {
                Name = name,
                Type = type,
                Number = number,
                Geometry = new ToolGeometry { Diameter = diameter, FluteLength = 30, FluteCount = 3 },
            };
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void An_empty_query_matches_everything(string query)
        {
            Assert.True(ToolSearch.Matches(Sample(), query));
        }

        [Fact]
        public void Matches_on_description_regardless_of_case()
        {
            Assert.True(ToolSearch.Matches(Sample(), "ALUMINUM"));
            Assert.True(ToolSearch.Matches(Sample(), "rougher"));
        }

        [Fact]
        public void Matches_on_the_type_as_displayed()
        {
            // The list shows "Flat end mill", so searching that must work - not just
            // the enum spelling.
            Assert.True(ToolSearch.Matches(Sample(), "flat end mill"));
            Assert.True(ToolSearch.Matches(Sample(type: ToolType.BullNoseEndMill), "bull nose"));
        }

        [Fact]
        public void Matches_on_tool_number_and_diameter()
        {
            Assert.True(ToolSearch.Matches(Sample(number: 17), "17"));
            Assert.True(ToolSearch.Matches(Sample(diameter: 6.35), "6.35"));

            // People search for the number on the label, not the stored precision.
            Assert.True(ToolSearch.Matches(Sample(diameter: 6.35), "6.4"));
        }

        [Fact]
        public void All_terms_must_match_so_a_query_narrows()
        {
            Tool drill = Sample("M3 tap drill", ToolType.Drill, number: 15, diameter: 2.5);

            Assert.True(ToolSearch.Matches(drill, "drill 2.5"));
            Assert.True(ToolSearch.Matches(drill, "2.5 drill"));

            // "drill" matches but "12" does not, so the tool is excluded.
            Assert.False(ToolSearch.Matches(drill, "drill 12"));
        }

        [Fact]
        public void Non_matching_query_is_rejected()
        {
            Assert.False(ToolSearch.Matches(Sample(), "titanium"));
        }

        [Fact]
        public void A_null_tool_never_matches()
        {
            Assert.False(ToolSearch.Matches(null, "anything"));
            Assert.False(ToolSearch.Matches(null, ""));
        }

        [Fact]
        public void Display_names_are_readable_for_every_type()
        {
            Assert.Equal("Flat end mill", ToolSearch.DisplayName(ToolType.FlatEndMill));
            Assert.Equal("Bull nose end mill", ToolSearch.DisplayName(ToolType.BullNoseEndMill));
            Assert.Equal("Spot drill", ToolSearch.DisplayName(ToolType.SpotDrill));
            Assert.Equal("Tap", ToolSearch.DisplayName(ToolType.Tap));

            foreach (ToolType type in System.Enum.GetValues(typeof(ToolType)))
            {
                Assert.False(
                    string.IsNullOrWhiteSpace(ToolSearch.DisplayName(type)),
                    type + " has no display name");
            }
        }
    }
}
