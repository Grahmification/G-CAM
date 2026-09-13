using System.Collections.Generic;
using System.Linq;
using GCam.Core.Model;
using GCam.Core.Strategies.Contour2d;
using GCam.Core.Tooling;
using Xunit;

namespace GCam.Core.Tests.Model
{
    public class ToolUsageTests
    {
        private static Tool Tool(string id, int number, ToolType type = ToolType.FlatEndMill) =>
            new Tool
            {
                Id = id,
                Number = number,
                Type = type,
                Geometry = { Diameter = 6 },
            };

        private static Operation Using(Job job, string name, Tool tool)
        {
            var operation = new Operation(new Contour2dSettings()) { Name = name };

            if (tool != null)
            {
                operation.UseTool(tool);
            }

            job.Operations.Add(operation);
            return operation;
        }

        [Fact]
        public void Every_tool_is_listed_with_the_operations_cutting_with_it()
        {
            var document = new JobDocument();
            Tool endMill = document.AddTool(Tool("tool-4", 4));
            Tool drill = document.AddTool(Tool("tool-7", 7, ToolType.Drill));

            Job job = document.AddNew();
            Using(job, "Roughing", endMill);
            Using(job, "Finishing", endMill);
            Using(job, "Drill holes", drill);

            IReadOnlyList<ToolUsage> usage = ToolUsage.ForDocument(document);

            Assert.Equal(2, usage.Count);
            Assert.Equal(new[] { "Roughing", "Finishing" },
                usage[0].Uses.Select(u => u.Operation.Name));
            Assert.Equal("Drill holes", Assert.Single(usage[1].Uses).Operation.Name);
        }

        [Fact]
        public void Usage_spans_every_job_in_the_part()
        {
            // The list is per part, not per job, so a tool used in two setups shows both.
            var document = new JobDocument();
            Tool tool = document.AddTool(Tool("tool-4", 4));

            Job first = document.AddNew();
            Job second = document.AddNew();
            Using(first, "Op A", tool);
            Using(second, "Op B", tool);

            ToolUsage usage = Assert.Single(ToolUsage.ForDocument(document));

            Assert.Equal(2, usage.Uses.Count);
            Assert.Equal(new[] { first.Name, second.Name },
                usage.Uses.Select(u => u.Job.Name));
        }

        [Fact]
        public void A_use_names_the_job_as_well_as_the_operation()
        {
            // "2D Contour1" alone does not say which setup it is in.
            var document = new JobDocument();
            Tool tool = document.AddTool(Tool("tool-4", 4));
            Job job = document.AddNew();
            Using(job, "2D Contour1", tool);

            ToolUse use = ToolUsage.ForDocument(document).Single().Uses.Single();

            Assert.Equal("Job 1 > 2D Contour1", use.ToString());
        }

        [Fact]
        public void A_tool_nothing_cuts_with_is_listed_as_unused()
        {
            var document = new JobDocument();
            document.AddTool(Tool("tool-4", 4));

            ToolUsage usage = Assert.Single(ToolUsage.ForDocument(document));

            Assert.True(usage.IsUnused);
            Assert.Empty(usage.Uses);
        }

        [Fact]
        public void Tools_are_ordered_by_number_for_whoever_loads_the_carousel()
        {
            var document = new JobDocument();
            document.AddTool(Tool("c", 7));
            document.AddTool(Tool("a", 2));
            document.AddTool(Tool("b", 4));

            Assert.Equal(new[] { 2, 4, 7 },
                ToolUsage.ForDocument(document).Select(u => u.Tool.Number));
        }

        [Fact]
        public void An_unnumbered_tool_sorts_last_not_first()
        {
            // Zero means unassigned. Sorting it to the top would put the one tool that
            // cannot be loaded at the head of the list someone loads from.
            var document = new JobDocument();
            document.AddTool(Tool("unnumbered", 0));
            document.AddTool(Tool("a", 4));

            Assert.Equal(new[] { 4, 0 },
                ToolUsage.ForDocument(document).Select(u => u.Tool.Number));
        }

        [Fact]
        public void An_operation_pointing_at_a_tool_that_is_gone_is_reported_separately()
        {
            var document = new JobDocument();
            Job job = document.AddNew();
            Operation orphan = Using(job, "Orphan", null);
            orphan.ToolId = "tool-that-left";

            ToolUse missing = Assert.Single(ToolUsage.WithMissingTools(document));

            Assert.Equal("Orphan", missing.Operation.Name);
        }

        [Fact]
        public void An_operation_with_no_tool_chosen_yet_is_not_a_missing_tool()
        {
            // An unfinished operation is an ordinary state; Operation.Validate says so.
            // Only a reference that points at nothing is a broken link.
            var document = new JobDocument();
            Using(document.AddNew(), "Not set up yet", null);

            Assert.Empty(ToolUsage.WithMissingTools(document));
        }

        [Fact]
        public void Uses_of_a_tool_can_be_asked_for_directly()
        {
            var document = new JobDocument();
            Tool tool = document.AddTool(Tool("tool-4", 4));
            Using(document.AddNew(), "Roughing", tool);

            Assert.Single(ToolUsage.UsesOf(document, "tool-4"));
            Assert.Empty(ToolUsage.UsesOf(document, "tool-9"));
            Assert.Empty(ToolUsage.UsesOf(document, null));
        }

        [Fact]
        public void A_document_with_no_tools_reports_nothing_rather_than_failing()
        {
            Assert.Empty(ToolUsage.ForDocument(new JobDocument()));
            Assert.Empty(ToolUsage.WithMissingTools(new JobDocument()));
        }
    }
}
