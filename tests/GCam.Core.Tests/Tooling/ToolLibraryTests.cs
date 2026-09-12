using System;
using System.Linq;
using GCam.Core.Tooling;
using Xunit;

namespace GCam.Core.Tests.Tooling
{
    public class ToolLibraryTests
    {
        private static Tool SampleTool(string id = "t1", int number = 1)
        {
            return new Tool
            {
                Id = id,
                Number = number,
                Name = "10mm flat",
                Type = ToolType.FlatEndMill,
                Geometry = new ToolGeometry { Diameter = 10, FluteLength = 25, FluteCount = 4 },
                Cutting = new CuttingData { SpindleRpm = 8000, CuttingFeed = 1200 },
            };
        }

        private static ToolLibrary SampleLibrary()
        {
            var library = new ToolLibrary { Id = "lib-1", Name = "Shop" };
            library.Tools.Add(SampleTool());
            return library;
        }

        [Fact]
        public void CheckOut_stamps_the_source_library_so_the_copy_can_be_traced_back()
        {
            Tool embedded = SampleLibrary().CheckOut("t1");

            Assert.Equal("lib-1", embedded.SourceLibraryId);
            Assert.Equal("t1", embedded.Id);
        }

        [Fact]
        public void CheckOut_returns_a_deep_copy_so_later_library_edits_cannot_change_a_job()
        {
            ToolLibrary library = SampleLibrary();
            Tool embedded = library.CheckOut("t1");

            // Someone edits the library afterwards.
            library.Tools[0].Geometry.Diameter = 12;
            library.Tools[0].Cutting.CuttingFeed = 99;
            library.Tools[0].Name = "changed";

            // The job's copy is untouched - this is the whole point of embedding.
            Assert.Equal(10, embedded.Geometry.Diameter, 6);
            Assert.Equal(1200, embedded.Cutting.CuttingFeed, 6);
            Assert.Equal("10mm flat", embedded.Name);
        }

        [Fact]
        public void Holders_are_deep_copied_too()
        {
            ToolLibrary library = SampleLibrary();
            library.Tools[0].Holder = new Holder
            {
                Id = "h1",
                Name = "ER32",
                Segments = { new HolderSegment(30, 20, 30) },
            };

            Tool embedded = library.CheckOut("t1");
            library.Tools[0].Holder.Segments[0].Length = 999;

            Assert.Equal(30, embedded.Holder.Segments[0].Length, 6);
        }

        [Fact]
        public void CheckOut_of_an_unknown_tool_is_an_error()
        {
            Assert.Throws<ArgumentException>(() => SampleLibrary().CheckOut("missing"));
        }

        [Fact]
        public void Find_by_id_and_number_both_work_and_miss_cleanly()
        {
            ToolLibrary library = SampleLibrary();

            Assert.NotNull(library.FindById("t1"));
            Assert.NotNull(library.FindByNumber(1));
            Assert.Null(library.FindById("nope"));
            Assert.Null(library.FindByNumber(99));
            Assert.Null(library.FindById(null));
        }

        [Fact]
        public void Duplicate_tool_ids_are_reported_because_they_break_re_linking()
        {
            var library = new ToolLibrary { Id = "lib" };
            library.Tools.Add(SampleTool("same", 1));
            library.Tools.Add(SampleTool("same", 2));

            Assert.Contains(library.Validate(), p => p.Contains("Duplicate tool id"));
        }

        [Fact]
        public void Validation_messages_name_the_tool_they_came_from()
        {
            var library = new ToolLibrary { Id = "lib" };
            Tool broken = SampleTool();
            broken.Geometry.Diameter = -1;
            library.Tools.Add(broken);

            string problem = Assert.Single(library.Validate().Where(p => p.Contains("Diameter")));
            Assert.Contains("T1", problem);
        }

        [Fact]
        public void Feed_per_tooth_is_derived_and_degrades_quietly_when_inputs_are_missing()
        {
            var cutting = new CuttingData { SpindleRpm = 8000, CuttingFeed = 1600 };

            Assert.Equal(0.05, cutting.FeedPerTooth(4), 6);
            Assert.Equal(0, cutting.FeedPerTooth(0), 6);
            Assert.Equal(0, new CuttingData().FeedPerTooth(4), 6);
        }
    }
}
