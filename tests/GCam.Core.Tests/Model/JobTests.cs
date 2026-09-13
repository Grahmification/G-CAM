using System.Linq;
using GCam.Core.Model;
using GCam.Core.Strategies.Contour2d;
using Xunit;

namespace GCam.Core.Tests.Model
{
    public class JobTests
    {
        private static Job SampleJob()
        {
            var job = new Job { Name = "Roughing" };
            job.ModelBodyNames.Add("Boss-Extrude1");
            job.CoordinateSystemName = "Coordinate System1";
            job.Stock.SideOffset = 2;
            job.Operations.Add(new Operation(new Contour2dSettings()) { Name = "Contour1" });
            job.Extra["hsm.origin"] = "imported";
            return job;
        }

        [Fact]
        public void A_new_job_defaults_to_G54()
        {
            Assert.Equal("G54", WorkOffsets.Name(new Job().WorkOffset));
        }

        [Fact]
        public void A_job_with_no_bodies_chosen_machines_the_whole_part()
        {
            Assert.True(new Job().MachinesWholePart);
        }

        [Fact]
        public void Choosing_a_body_stops_it_machining_the_whole_part()
        {
            var job = new Job();
            job.ModelBodyNames.Add("Boss-Extrude1");

            Assert.False(job.MachinesWholePart);
        }

        [Fact]
        public void No_coordinate_system_displays_as_the_part_origin()
        {
            Assert.Equal("Part origin", new Job().CoordinateSystemDisplayName);
        }

        [Fact]
        public void Clone_keeps_the_id_and_CloneAsNew_replaces_it()
        {
            Job job = SampleJob();

            Assert.Equal(job.Id, job.Clone().Id);
            Assert.NotEqual(job.Id, job.CloneAsNew().Id);
        }

        [Fact]
        public void Clone_copies_the_stock_rather_than_sharing_it()
        {
            Job job = SampleJob();

            Job copy = job.Clone();
            copy.Stock.SideOffset = 99;

            Assert.Equal(2, job.Stock.SideOffset, 6);
        }

        [Fact]
        public void Clone_copies_the_body_list_rather_than_sharing_it()
        {
            Job job = SampleJob();

            Job copy = job.Clone();
            copy.ModelBodyNames.Add("Boss-Extrude2");

            Assert.Single(job.ModelBodyNames);
        }

        [Fact]
        public void Clone_copies_the_operations_rather_than_sharing_them()
        {
            Job job = SampleJob();

            Job copy = job.Clone();
            copy.Operations[0].Name = "Renamed";

            Assert.Equal("Contour1", job.Operations[0].Name);
        }

        [Fact]
        public void Clone_copies_the_extra_bag_rather_than_sharing_it()
        {
            Job job = SampleJob();

            Job copy = job.Clone();
            copy.Extra["hsm.origin"] = "changed";

            Assert.Equal("imported", job.Extra["hsm.origin"]);
        }

        [Fact]
        public void CloneAsNew_gives_the_operations_fresh_ids_too()
        {
            // Two jobs whose operations share ids would make "which operation made this
            // toolpath" unanswerable as soon as either was edited.
            Job job = SampleJob();

            Job copy = job.CloneAsNew();

            Assert.NotEqual(job.Operations[0].Id, copy.Operations[0].Id);
        }

        [Fact]
        public void A_job_with_no_name_is_invalid()
        {
            var job = new Job();

            Assert.Contains("needs a name", string.Join(" ", job.Validate()));
        }

        [Fact]
        public void A_work_offset_outside_G54_to_G59_is_invalid()
        {
            var job = new Job { Name = "Roughing", WorkOffset = 9 };

            Assert.Contains("G54 to G59", string.Join(" ", job.Validate()));
        }

        [Fact]
        public void Job_validation_reports_the_stocks_problems_as_well()
        {
            var job = new Job { Name = "Roughing" };
            job.Stock.SideOffset = -1;

            Assert.Contains("Side offset", string.Join(" ", job.Validate()));
        }

        [Fact]
        public void A_complete_job_validates_clean()
        {
            Assert.Empty(SampleJob().Validate());
        }

        [Fact]
        public void Work_offset_numbers_map_onto_the_G_codes_in_order()
        {
            Assert.Equal("G54", WorkOffsets.Name(1));
            Assert.Equal("G59", WorkOffsets.Name(6));
            Assert.Equal(6, WorkOffsets.Names.Count);
            Assert.Equal("G54", WorkOffsets.Names.First());
        }

        [Fact]
        public void Offsets_beyond_the_six_standard_ones_are_not_standard()
        {
            Assert.False(WorkOffsets.IsStandard(0));
            Assert.False(WorkOffsets.IsStandard(7));
            Assert.True(WorkOffsets.IsStandard(1));
        }
    }
}
