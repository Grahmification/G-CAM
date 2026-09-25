using System;
using System.IO;
using System.Linq;
using GCam.Core.Tooling;
using GCam.Core.Tooling.Import;
using Xunit;

namespace GCam.Core.Tests.Tooling
{
    /// <summary>
    /// Runs against the real Tormach library in docs/example_files, not a hand-written
    /// fixture - the point is to prove G-CAM reads a file HSMWorks actually produced.
    /// </summary>
    public class HsmLibraryReaderTests
    {
        private static string ExampleFilePath()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "G-CAM.sln")))
            {
                dir = dir.Parent;
            }

            Assert.NotNull(dir);
            return Path.Combine(dir.FullName, "docs", "example_files", "tool_library_hsmworks.hsmlib");
        }

        /// <summary>The example library, for tests in other classes that need real data.</summary>
        internal static ToolLibrary ReadExampleLibrary() => ReadExample().Library;

        private static ToolLibraryReadResult ReadExample()
        {
            string path = ExampleFilePath();
            Assert.True(File.Exists(path), "Example library missing: " + path);

            using (FileStream stream = File.OpenRead(path))
            {
                return new HsmLibraryReader().Read(stream, path);
            }
        }

        [Fact]
        public void Reads_every_tool_in_the_example_file()
        {
            ToolLibraryReadResult result = ReadExample();

            // 11 <tool> elements, all of supported types.
            Assert.Equal(11, result.Library.Tools.Count);
            Assert.Empty(result.Warnings);
        }

        [Fact]
        public void Uses_the_nc_number_as_the_tool_number_not_the_library_index()
        {
            // Tool with id="1" has nc number="4". Confusing these puts the wrong T word
            // in the posted program.
            Tool tool = ReadExample().Library.Tools.Single(t => t.Id.StartsWith("7a7b06c9"));

            Assert.Equal(4, tool.Number);
            Assert.Equal("1", tool.Extra["hsm.id"]);
        }

        [Fact]
        public void Reads_a_flat_end_mill_completely()
        {
            Tool tool = ReadExample().Library.Tools.Single(t => t.Id.StartsWith("7a7b06c9"));

            Assert.Equal(ToolType.FlatEndMill, tool.Type);
            Assert.Equal("Aluminum", tool.Name);
            Assert.Equal("carbide", tool.Material);

            Assert.Equal(12.7, tool.Geometry.Diameter, 6);
            Assert.Equal(31.75, tool.Geometry.FluteLength, 6);
            Assert.Equal(31.75, tool.Geometry.ShoulderLength, 6);
            Assert.Equal(12.7, tool.Geometry.ShankDiameter, 6);
            Assert.Equal(38, tool.Geometry.BodyLength, 6);
            Assert.Equal(60.53, tool.Geometry.OverallLength, 6);
            Assert.Equal(3, tool.Geometry.FluteCount);

            Assert.Equal(7500, tool.Cutting.SpindleRpm, 6);
            Assert.Equal(1800, tool.Cutting.CuttingFeed, 6);
            Assert.Equal(600, tool.Cutting.PlungeFeed, 6);
            Assert.Equal(900, tool.Cutting.RampFeed, 6);
            Assert.Equal(600, tool.Cutting.RetractFeed, 6);
            Assert.True(tool.Cutting.SpindleClockwise);
            Assert.Equal(FeedMode.PerMinute, tool.Cutting.FeedMode);
            Assert.Equal(CoolantMode.Flood, tool.Cutting.Coolant);

            Assert.Equal(4, tool.Machine.DiameterOffset);
            Assert.Equal(4, tool.Machine.LengthOffset);
            Assert.True(tool.Machine.BreakControl);
            Assert.True(tool.Machine.ManualToolChange);
        }

        [Fact]
        public void Drill_taper_angle_is_read_as_an_included_point_angle()
        {
            Tool drill = ReadExample().Library.Tools.Single(t => t.Id.StartsWith("b1dc7827"));

            Assert.Equal(ToolType.Drill, drill.Type);
            Assert.Equal(118, drill.Geometry.TipAngle, 6);

            // A 2.5mm drill at 118 degrees included has a point about 0.75mm tall.
            Assert.Equal(0.751, drill.GetProfile().HeightAt(1.25), 2);
        }

        [Fact]
        public void Chamfer_mill_taper_angle_is_doubled_into_an_included_angle()
        {
            // HSM stores 45 for this tool, meaning 45 degrees from the axis. The proof
            // is the tool's own flute length: a 6.35mm cutter reaching full diameter in
            // 3.175mm can only be a 90 degree included cone.
            Tool chamfer = ReadExample().Library.Tools.Single(t => t.Id.StartsWith("b5c6ce17"));

            Assert.Equal(ToolType.ChamferMill, chamfer.Type);
            Assert.Equal(90, chamfer.Geometry.TipAngle, 6);

            CutterProfile profile = chamfer.GetProfile();
            Assert.Equal(3.175, profile.HeightAt(3.175), 4);
        }

        [Fact]
        public void Spot_drill_keeps_both_of_its_angles()
        {
            Tool spot = ReadExample().Library.Tools.Single(t => t.Id.StartsWith("f30fef9d"));

            Assert.Equal(ToolType.SpotDrill, spot.Type);
            Assert.Equal(90, spot.Geometry.TipAngle, 6);
            Assert.Equal(60, spot.Geometry.SecondTipAngle, 6);
            Assert.Equal(0.5, spot.Geometry.TipDiameter, 6);
        }

        [Fact]
        public void Taps_are_imported_with_their_thread_pitch()
        {
            var taps = ReadExample().Library.Tools.Where(t => t.Type == ToolType.Tap).ToList();

            Assert.Equal(2, taps.Count);

            Tool m3 = taps.Single(t => t.Name == "M3");
            Assert.Equal(3, m3.Geometry.Diameter, 6);
            Assert.Equal(0.5, m3.Geometry.ThreadPitch, 6);
            Assert.Equal("Spiral Flute", m3.ProductId);

            Tool m6 = taps.Single(t => t.Name == "M6");
            Assert.Equal(1.0, m6.Geometry.ThreadPitch, 6);
        }

        [Fact]
        public void Holders_are_shared_between_tools_not_duplicated()
        {
            ToolLibrary library = ReadExample().Library;

            // The file repeats four distinct holder guids across eleven tools.
            Assert.Equal(4, library.Holders.Count);

            var ttsUsers = library.Tools.Where(t => t.Holder != null && t.Holder.Name.StartsWith("TTS")).ToList();
            Assert.True(ttsUsers.Count > 1);

            // Same instance, not copies - editing the holder once updates every tool.
            Assert.Same(ttsUsers[0].Holder, ttsUsers[1].Holder);
        }

        [Fact]
        public void Holder_sections_become_segments_with_steps_collapsed()
        {
            Tool tool = ReadExample().Library.Tools.Single(t => t.Id.StartsWith("7a7b06c9"));
            Holder holder = tool.Holder;

            Assert.Equal("TTS 3/4\" -ER20", holder.Name);

            // 15 sections, five of which are zero-length steps, leaving ten real stages.
            Assert.Equal(10, holder.Segments.Count);

            // Heights of the ten non-zero sections sum to the holder height:
            // 1.5 + 16.3 + 1.5 + 12.15 + 1.5 + 9.82 + 8.72 + 1 + 34.5 + 1
            Assert.Equal(87.99, holder.Height, 4);
            Assert.Equal(38.25, holder.MaxDiameter, 4);

            // First real stage tapers 25.2 -> 33.88 over 1.5mm.
            Assert.Equal(1.5, holder.Segments[0].Length, 6);
            Assert.Equal(25.2, holder.Segments[0].LowerDiameter, 6);
            Assert.Equal(33.88, holder.Segments[0].UpperDiameter, 6);
        }

        [Fact]
        public void Unmapped_fields_are_preserved_rather_than_discarded()
        {
            Tool tool = ReadExample().Library.Tools.Single(t => t.Id.StartsWith("7a7b06c9"));

            Assert.Equal("flat end mill", tool.Extra["hsm.type"]);
            Assert.Equal("1.1", tool.Extra["hsm.tool-version"]);
            Assert.Equal("Example Tool Library", tool.Holder.Extra["hsm.library-name"]);
            Assert.Contains("tormach.com", tool.Holder.Vendor);
        }

        [Fact]
        public void Stepover_and_stepdown_arrive_empty_because_HSM_stores_them_per_operation()
        {
            Tool tool = ReadExample().Library.Tools.First();

            Assert.Equal(0, tool.Cutting.Stepover, 6);
            Assert.Equal(0, tool.Cutting.Stepdown, 6);
        }

        [Fact]
        public void Every_imported_tool_produces_a_usable_cutter_profile()
        {
            foreach (Tool tool in ReadExample().Library.Tools)
            {
                CutterProfile profile = tool.GetProfile();
                Assert.True(profile.MaxRadius > 0, tool.Name + " has no width");
                Assert.True(profile.Points.Count >= 2, tool.Name + " has a degenerate profile");
            }
        }

        [Fact]
        public void Unsupported_tool_types_are_reported_not_silently_dropped()
        {
            const string xml = @"<?xml version='1.0'?>
<tool-table xmlns='http://www.hsmworks.com/xml/2004/cnc/tool-library' version='1.0'>
  <tool version='1.1' type='reamer' unit='millimeters' guid='{11111111-1111-1111-1111-111111111111}' id='1'>
    <description>M6 Reamer</description>
    <body diameter='6' flute-length='30' number-of-flutes='4'/>
  </tool>
</tool-table>";

            using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml)))
            {
                ToolLibraryReadResult result = new HsmLibraryReader().Read(stream, "x.hsmlib");

                Assert.Empty(result.Library.Tools);
                string warning = Assert.Single(result.Warnings);
                Assert.Contains("M6 Reamer", warning);
                Assert.Contains("reamer", warning);
            }
        }

        [Fact]
        public void Inch_tools_convert_because_units_are_declared_per_tool()
        {
            const string xml = @"<?xml version='1.0'?>
<tool-table xmlns='http://www.hsmworks.com/xml/2004/cnc/tool-library' version='1.0'>
  <tool version='1.1' type='flat end mill' unit='inches' guid='{22222222-2222-2222-2222-222222222222}' id='1'>
    <description>Half inch</description>
    <body diameter='0.5' flute-length='1' number-of-flutes='4'/>
    <motion spindle-rpm='5000' cutting-feedrate='40'/>
  </tool>
</tool-table>";

            using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml)))
            {
                Tool tool = new HsmLibraryReader().Read(stream, "x.hsmlib").Library.Tools.Single();

                Assert.Equal(12.7, tool.Geometry.Diameter, 6);
                Assert.Equal(25.4, tool.Geometry.FluteLength, 6);
                Assert.Equal(40 * 25.4, tool.Cutting.CuttingFeed, 4);

                // RPM is not a length.
                Assert.Equal(5000, tool.Cutting.SpindleRpm, 6);
            }
        }

        [Fact]
        public void Imported_tools_survive_a_round_trip_through_the_native_format()
        {
            ToolLibrary imported = ReadExample().Library;

            var buffer = new MemoryStream();
            new GcamXmlLibraryWriter().Write(imported, buffer);
            buffer.Position = 0;

            ToolLibrary reloaded = new GcamXmlLibraryReader().Read(buffer, "rt.gcamtools").Library;

            Assert.Equal(imported.Tools.Count, reloaded.Tools.Count);

            Tool before = imported.Tools.Single(t => t.Id.StartsWith("7a7b06c9"));
            Tool after = reloaded.Tools.Single(t => t.Id.StartsWith("7a7b06c9"));

            Assert.Equal(before.Number, after.Number);
            Assert.Equal(before.Material, after.Material);
            Assert.Equal(before.Geometry.ShoulderLength, after.Geometry.ShoulderLength, 9);
            Assert.Equal(before.Geometry.BodyLength, after.Geometry.BodyLength, 9);
            Assert.Equal(before.Cutting.RetractFeed, after.Cutting.RetractFeed, 9);
            Assert.Equal(before.Machine.DiameterOffset, after.Machine.DiameterOffset);
            Assert.Equal(before.Machine.ManualToolChange, after.Machine.ManualToolChange);

            // The property bag survives too - that is the whole point of it.
            Assert.Equal(before.Extra["hsm.type"], after.Extra["hsm.type"]);

            Tool tapBefore = imported.Tools.Single(t => t.Name == "M6" && t.Type == ToolType.Tap);
            Tool tapAfter = reloaded.Tools.Single(t => t.Name == "M6" && t.Type == ToolType.Tap);
            Assert.Equal(tapBefore.Geometry.ThreadPitch, tapAfter.Geometry.ThreadPitch, 9);
        }
    }
}
