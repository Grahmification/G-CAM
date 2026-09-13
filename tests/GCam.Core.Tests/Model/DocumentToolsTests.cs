using System.Linq;
using GCam.Core.Diagnostics;
using GCam.Core.Model;
using GCam.Core.Strategies.Contour2d;
using GCam.Core.Tooling;
using Xunit;

namespace GCam.Core.Tests.Model
{
    public class DocumentToolsTests
    {
        private static ToolLibrary Library()
        {
            var library = new ToolLibrary { Id = "lib-1", Name = "Shop tools" };

            library.Tools.Add(new Tool
            {
                Id = "tool-4",
                Number = 4,
                Name = "Aluminium",
                Type = ToolType.FlatEndMill,
                Geometry = { Diameter = 12.7, FluteLength = 31.75 },
                Cutting = { SpindleRpm = 7500, CuttingFeed = 1800, PlungeFeed = 600 },
            });

            return library;
        }

        private static Operation NewOperation(string name) =>
            new Operation(new Contour2dSettings()) { Name = name };

        [Fact]
        public void A_tool_checked_out_of_a_library_lands_in_the_part_list()
        {
            var document = new JobDocument();
            ToolLibrary library = Library();

            Tool copy = document.AddTool(library.CheckOut("tool-4"));

            Assert.Same(copy, Assert.Single(document.Tools));
            Assert.Equal("lib-1", copy.SourceLibraryId);
            Assert.Equal("tool-4", copy.Id);

            // A copy, not the library's own object: editing the part's tool must not
            // reach back into the library.
            Assert.NotSame(library.FindById("tool-4"), copy);
        }

        [Fact]
        public void Checking_the_same_tool_out_twice_does_not_make_a_second_copy()
        {
            // The rule that makes a shared tool list mean anything: a second operation
            // wanting the same cutter uses the one already here.
            var document = new JobDocument();
            ToolLibrary library = Library();

            Tool first = document.AddTool(library.CheckOut("tool-4"));
            Tool second = document.AddTool(library.CheckOut("tool-4"));

            Assert.Same(first, second);
            Assert.Single(document.Tools);
        }

        [Fact]
        public void Tools_are_found_by_the_id_operations_point_at()
        {
            var document = new JobDocument();
            Tool tool = document.AddTool(Library().CheckOut("tool-4"));

            Assert.Same(tool, document.FindTool("tool-4"));
            Assert.Null(document.FindTool("tool-9"));
            Assert.Null(document.FindTool(null));
        }

        [Fact]
        public void An_unused_tool_can_be_removed()
        {
            var document = new JobDocument();
            Tool tool = document.AddTool(Library().CheckOut("tool-4"));

            Assert.True(document.RemoveTool(tool));
            Assert.Empty(document.Tools);
        }

        [Fact]
        public void A_tool_in_use_refuses_to_be_removed_and_says_where_it_is_used()
        {
            var document = new JobDocument();
            Tool tool = document.AddTool(Library().CheckOut("tool-4"));

            Job job = document.AddNew();
            Operation operation = NewOperation("2D Contour1");
            operation.UseTool(tool);
            job.Operations.Add(operation);

            var error = Assert.Throws<GCamUserException>(() => document.RemoveTool(tool));

            Assert.Contains("2D Contour1", error.Message);
            Assert.Single(document.Tools);
        }

        [Fact]
        public void Two_tools_sharing_a_number_are_reported()
        {
            // The machine has one pocket 4. A part claiming two different cutters live
            // there cannot be set up as written.
            var document = new JobDocument();
            document.AddTool(new Tool { Id = "a", Number = 4, Type = ToolType.FlatEndMill });
            document.AddTool(new Tool { Id = "b", Number = 4, Type = ToolType.Drill });

            string problem = Assert.Single(document.ValidateTools());

            Assert.Contains("Tool number 4", problem);
        }

        [Fact]
        public void Unnumbered_tools_do_not_count_as_clashing_with_each_other()
        {
            // Zero means "not assigned yet", not "pocket zero".
            var document = new JobDocument();
            document.AddTool(new Tool { Id = "a", Type = ToolType.FlatEndMill });
            document.AddTool(new Tool { Id = "b", Type = ToolType.Drill });

            Assert.Empty(document.ValidateTools());
        }

        [Fact]
        public void Choosing_a_tool_copies_its_feeds_onto_the_operation()
        {
            var document = new JobDocument();
            Tool tool = document.AddTool(Library().CheckOut("tool-4"));
            Operation operation = NewOperation("2D Contour1");

            operation.UseTool(tool);

            Assert.Equal("tool-4", operation.ToolId);
            Assert.Equal(7500, operation.Cutting.SpindleRpm, 9);
            Assert.Equal(1800, operation.Cutting.CuttingFeed, 9);
        }

        [Fact]
        public void An_operations_feeds_are_its_own_once_chosen()
        {
            // The split that makes sharing safe: geometry is the tool's, feeds are the
            // operation's. Slowing a finishing pass must not slow the roughing one.
            var document = new JobDocument();
            Tool tool = document.AddTool(Library().CheckOut("tool-4"));

            Operation roughing = NewOperation("Roughing");
            Operation finishing = NewOperation("Finishing");
            roughing.UseTool(tool);
            finishing.UseTool(tool);

            finishing.Cutting.SpindleRpm = 12000;

            Assert.Equal(7500, roughing.Cutting.SpindleRpm, 9);
            Assert.Equal(7500, tool.Cutting.SpindleRpm, 9);
            Assert.Equal(finishing.ToolId, roughing.ToolId);
        }

        [Fact]
        public void Changing_tool_re_seeds_the_feeds()
        {
            var document = new JobDocument();
            Tool endMill = document.AddTool(Library().CheckOut("tool-4"));
            Tool drill = document.AddTool(new Tool
            {
                Id = "tool-7",
                Number = 7,
                Type = ToolType.Drill,
                Cutting = { SpindleRpm = 2000, CuttingFeed = 200 },
            });

            Operation operation = NewOperation("2D Contour1");
            operation.UseTool(endMill);
            operation.Cutting.CuttingFeed = 1234;
            operation.UseTool(drill);

            // Carrying a 12.7mm cutter's feeds onto a small drill would break the drill.
            Assert.Equal(2000, operation.Cutting.SpindleRpm, 9);
            Assert.Equal(200, operation.Cutting.CuttingFeed, 9);
        }
    }
}
