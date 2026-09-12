using System.Collections.Generic;
using System.Linq;
using GCam.Core.Tooling;
using Xunit;

namespace GCam.Core.Tests.Tooling
{
    /// <summary>
    /// The cutter silhouette runs to the top of the tool body, and the plain shank above
    /// it is separate geometry. Both were wrong or missing until a chamfer mill in a real
    /// library made it obvious.
    /// </summary>
    public class ShankAndBodyTests
    {
        [Fact]
        public void Chamfer_mill_body_extends_past_the_cutting_cone()
        {
            // The regression: flute-length 3.175 but shoulder-length 26. Running the
            // profile to the flute length alone drew the cone with nothing above it.
            Tool chamfer = HsmLibraryReaderTests.ReadExampleLibrary().Tools
                .Single(t => t.Id.StartsWith("b5c6ce17"));

            Assert.Equal(3.175, chamfer.Geometry.FluteLength, 6);
            Assert.Equal(26, chamfer.Geometry.ShoulderLength, 6);

            CutterProfile profile = chamfer.GetProfile();

            Assert.Equal(26, profile.Height, 6);

            // Full diameter from the top of the cone all the way up the straight section.
            Assert.Equal(3.175, profile.RadiusAt(3.175), 4);
            Assert.Equal(3.175, profile.RadiusAt(15), 4);
            Assert.Equal(3.175, profile.RadiusAt(26), 4);
        }

        [Fact]
        public void Tools_whose_flute_and_shoulder_agree_are_unchanged()
        {
            // Every other tool in the library has them equal, which is why only the
            // chamfer mill looked wrong.
            foreach (Tool tool in HsmLibraryReaderTests.ReadExampleLibrary().Tools
                         .Where(t => t.Id.StartsWith("b5c6ce17") == false))
            {
                Assert.Equal(tool.Geometry.FluteLength, tool.GetProfile().Height, 6);
            }
        }

        [Fact]
        public void Shank_starts_at_the_body_top_and_ends_at_the_holder_face()
        {
            Tool chamfer = HsmLibraryReaderTests.ReadExampleLibrary().Tools
                .Single(t => t.Id.StartsWith("b5c6ce17"));

            IReadOnlyList<ProfilePoint> shank = chamfer.GetShankProfile();

            Assert.Equal(2, shank.Count);
            Assert.Equal(26, shank[0].Height, 6);            // top of the body
            Assert.Equal(30.26, shank[1].Height, 6);         // stickout, i.e. holder face
            Assert.Equal(6.35 / 2, shank[0].Radius, 6);      // shaft-diameter
        }

        [Fact]
        public void Shank_uses_the_shaft_diameter_not_the_cutting_diameter()
        {
            var tool = new Tool
            {
                Type = ToolType.FlatEndMill,
                Geometry = new ToolGeometry
                {
                    Diameter = 12,
                    FluteLength = 20,
                    ShankDiameter = 6,      // reduced shank
                    BodyLength = 50,
                    FluteCount = 4,
                },
            };

            Assert.Equal(3, tool.GetShankProfile()[0].Radius, 6);
        }

        [Fact]
        public void Shank_falls_back_to_the_cutting_diameter_when_none_is_recorded()
        {
            var tool = new Tool
            {
                Type = ToolType.FlatEndMill,
                Geometry = new ToolGeometry { Diameter = 10, FluteLength = 20, BodyLength = 40, FluteCount = 4 },
            };

            Assert.Equal(5, tool.GetShankProfile()[0].Radius, 6);
        }

        [Fact]
        public void No_shank_when_none_is_exposed_below_the_holder()
        {
            // Stickout equal to the body length means the holder starts where the
            // flutes end - nothing plain to draw.
            var tool = new Tool
            {
                Type = ToolType.FlatEndMill,
                Geometry = new ToolGeometry { Diameter = 10, FluteLength = 30, BodyLength = 30, FluteCount = 4 },
            };

            Assert.Empty(tool.GetShankProfile());
        }

        [Fact]
        public void Shank_never_overlaps_the_cutting_body()
        {
            foreach (Tool tool in HsmLibraryReaderTests.ReadExampleLibrary().Tools)
            {
                IReadOnlyList<ProfilePoint> shank = tool.GetShankProfile();
                if (shank.Count == 0)
                {
                    continue;
                }

                Assert.True(
                    shank[0].Height >= tool.GetProfile().Height - 1e-9,
                    tool.Name + ": shank starts below the top of the cutting body");
            }
        }
    }
}
