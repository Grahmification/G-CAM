using System;
using System.IO;
using System.Text;
using GCam.Core.Diagnostics;
using GCam.Core.Tooling;
using GCam.Core.Tooling.Import;
using Xunit;

namespace GCam.Core.Tests.Tooling
{
    public class GcamXmlLibraryTests
    {
        private static ToolLibrary Read(string xml)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml)))
            {
                return new GcamXmlLibraryReader().Read(stream, "test.gcamtools").Library;
            }
        }

        private const string MinimalLibrary = @"
<gcamToolLibrary version='1' units='mm' id='lib-1' name='Shop tools'>
  <holders>
    <holder id='h1' name='BT40 ER32'>
      <segment length='30' lowerDiameter='20' upperDiameter='30' />
      <segment length='40' lowerDiameter='40' upperDiameter='40' />
    </holder>
  </holders>
  <tools>
    <tool id='t1' number='3' name='10mm bull nose' type='BullNoseEndMill' holderId='h1'>
      <geometry diameter='10' cornerRadius='2' fluteLength='25' fluteCount='4' />
      <cutting spindleRpm='8000' cuttingFeed='1200' plungeFeed='300'
               stepover='4' stepdown='2' coolant='Flood' />
    </tool>
  </tools>
</gcamToolLibrary>";

        [Fact]
        public void Reads_tools_holders_and_cutting_data()
        {
            ToolLibrary library = Read(MinimalLibrary);

            Assert.Equal("lib-1", library.Id);
            Assert.Equal("Shop tools", library.Name);

            Tool tool = Assert.Single(library.Tools);
            Assert.Equal(3, tool.Number);
            Assert.Equal(ToolType.BullNoseEndMill, tool.Type);
            Assert.Equal(10, tool.Geometry.Diameter, 6);
            Assert.Equal(2, tool.Geometry.CornerRadius, 6);
            Assert.Equal(8000, tool.Cutting.SpindleRpm, 6);
            Assert.Equal(CoolantMode.Flood, tool.Cutting.Coolant);
        }

        [Fact]
        public void Resolves_the_holder_reference()
        {
            Tool tool = Assert.Single(Read(MinimalLibrary).Tools);

            Assert.NotNull(tool.Holder);
            Assert.Equal("BT40 ER32", tool.Holder.Name);
            Assert.Equal(70, tool.Holder.Height, 6);
            Assert.Equal(40, tool.Holder.MaxDiameter, 6);
        }

        [Fact]
        public void Inch_libraries_are_converted_to_millimetres_on_load()
        {
            string xml = MinimalLibrary
                .Replace("units='mm'", "units='inch'")
                .Replace("diameter='10'", "diameter='0.5'")
                .Replace("cornerRadius='2'", "cornerRadius='0'")
                .Replace("type='BullNoseEndMill'", "type='FlatEndMill'");

            Tool tool = Assert.Single(Read(xml).Tools);

            Assert.Equal(12.7, tool.Geometry.Diameter, 6);

            // Feeds are a length per minute, so they scale too.
            Assert.Equal(1200 * 25.4, tool.Cutting.CuttingFeed, 4);

            // Spindle speed is not a length and must not scale.
            Assert.Equal(8000, tool.Cutting.SpindleRpm, 6);
        }

        [Fact]
        public void Numbers_parse_invariantly_regardless_of_machine_locale()
        {
            // A German-locale machine writing "10,5" would be a bug; the reader must
            // only accept the invariant form, so a library moves between machines.
            string xml = MinimalLibrary.Replace("diameter='10'", "diameter='10.5'");

            Tool tool = Assert.Single(Read(xml).Tools);
            Assert.Equal(10.5, tool.Geometry.Diameter, 6);
        }

        [Fact]
        public void A_newer_file_format_is_refused_with_an_explanation()
        {
            string xml = MinimalLibrary.Replace("version='1'", "version='99'");

            GCamUserException ex = Assert.Throws<GCamUserException>(() => Read(xml));
            Assert.Contains("newer version", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void A_dangling_holder_reference_is_reported_not_ignored()
        {
            string xml = MinimalLibrary.Replace("holderId='h1'", "holderId='nope'");

            GCamUserException ex = Assert.Throws<GCamUserException>(() => Read(xml));
            Assert.Contains("nope", ex.Message);
        }

        [Fact]
        public void An_unknown_tool_type_lists_the_types_that_are_known()
        {
            string xml = MinimalLibrary.Replace("type='BullNoseEndMill'", "type='PlasmaCutter'");

            GCamUserException ex = Assert.Throws<GCamUserException>(() => Read(xml));
            Assert.Contains("PlasmaCutter", ex.Message);
            Assert.Contains("BallEndMill", ex.Message);
        }

        [Fact]
        public void Invalid_geometry_fails_the_load_with_a_readable_message()
        {
            // Corner radius bigger than the tool radius.
            string xml = MinimalLibrary.Replace("cornerRadius='2'", "cornerRadius='9'");

            GCamUserException ex = Assert.Throws<GCamUserException>(() => Read(xml));
            Assert.Contains("Corner radius", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Malformed_xml_is_a_user_error_not_a_crash()
        {
            Assert.Throws<GCamUserException>(() => Read("<gcamToolLibrary><tools>"));
        }

        [Fact]
        public void A_file_that_is_not_a_tool_library_is_rejected_clearly()
        {
            GCamUserException ex = Assert.Throws<GCamUserException>(() => Read("<someOtherThing />"));
            Assert.Contains("gcamToolLibrary", ex.Message);
        }

        [Fact]
        public void Round_trips_through_write_and_read_unchanged()
        {
            ToolLibrary original = Read(MinimalLibrary);

            var buffer = new MemoryStream();
            new GcamXmlLibraryWriter().Write(original, buffer);
            buffer.Position = 0;

            ToolLibrary reloaded = new GcamXmlLibraryReader().Read(buffer, "roundtrip.gcamtools").Library;

            Assert.Equal(original.Id, reloaded.Id);
            Assert.Equal(original.Name, reloaded.Name);
            Assert.Equal(original.Tools.Count, reloaded.Tools.Count);

            Tool before = original.Tools[0];
            Tool after = reloaded.Tools[0];

            Assert.Equal(before.Id, after.Id);
            Assert.Equal(before.Number, after.Number);
            Assert.Equal(before.Type, after.Type);
            Assert.Equal(before.Geometry.Diameter, after.Geometry.Diameter, 9);
            Assert.Equal(before.Geometry.CornerRadius, after.Geometry.CornerRadius, 9);
            Assert.Equal(before.Cutting.CuttingFeed, after.Cutting.CuttingFeed, 9);
            Assert.Equal(before.Holder.Name, after.Holder.Name);
            Assert.Equal(before.Holder.Segments.Count, after.Holder.Segments.Count);
        }

        [Fact]
        public void Awkward_values_survive_a_round_trip()
        {
            var library = new ToolLibrary { Id = "lib", Name = "Precision" };
            library.Tools.Add(new Tool
            {
                Id = "t",
                Number = 1,
                Name = "odd",
                Type = ToolType.FlatEndMill,
                Geometry = new ToolGeometry
                {
                    Diameter = 6.35,          // 1/4 inch in mm
                    FluteLength = 19.0499999,
                    FluteCount = 3,
                },
                Cutting = new CuttingData { SpindleRpm = 10000, CuttingFeed = 1234.5678 },
            });

            var buffer = new MemoryStream();
            new GcamXmlLibraryWriter().Write(library, buffer);
            buffer.Position = 0;

            Tool reloaded = new GcamXmlLibraryReader().Read(buffer, null).Library.Tools[0];

            Assert.Equal(6.35, reloaded.Geometry.Diameter, 10);
            Assert.Equal(19.0499999, reloaded.Geometry.FluteLength, 10);
            Assert.Equal(1234.5678, reloaded.Cutting.CuttingFeed, 10);
        }
    }
}
