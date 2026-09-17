using System.Linq;
using GCam.Core.Model;
using GCam.Core.Strategies.Contour2d;
using Xunit;

namespace GCam.Core.Tests.Model
{
    /// <summary>
    /// Moving operations and jobs about - what a drag in the job tree asks for.
    /// </summary>
    /// <remarks>
    /// A destination is "before this row", which is what a drop is. The case worth reading
    /// twice is moving an operation <i>down</i> inside its own job: taking it out shifts
    /// everything after it, so the row it lands in front of is not the row at the index the
    /// cursor was over.
    /// </remarks>
    public class OperationReorderingTests
    {
        private static Operation Add(Job job, string name)
        {
            var operation = new Operation(new Contour2dSettings()) { Name = name };
            job.Operations.Add(operation);
            return operation;
        }

        private static string Names(Job job) => string.Join(",", job.Operations.Select(o => o.Name));

        private static string JobNames(JobDocument document) =>
            string.Join(",", document.Jobs.Select(j => j.Name));

        private static JobDocument OneJob(out Job job)
        {
            var document = new JobDocument();
            job = document.AddNew();
            job.Name = "Job1";

            Add(job, "A");
            Add(job, "B");
            Add(job, "C");

            return document;
        }

        [Fact]
        public void An_operation_moves_down_to_in_front_of_the_row_dropped_on()
        {
            JobDocument document = OneJob(out Job job);
            Operation a = job.Operations[0];
            Operation c = job.Operations[2];

            Assert.True(document.MoveOperation(a, job, before: c));

            Assert.Equal("B,A,C", Names(job));
        }

        [Fact]
        public void An_operation_moves_up_to_in_front_of_the_row_dropped_on()
        {
            JobDocument document = OneJob(out Job job);
            Operation a = job.Operations[0];
            Operation c = job.Operations[2];

            Assert.True(document.MoveOperation(c, job, before: a));

            Assert.Equal("C,A,B", Names(job));
        }

        [Fact]
        public void An_operation_moves_to_the_end_when_there_is_nothing_to_go_before()
        {
            JobDocument document = OneJob(out Job job);

            Assert.True(document.MoveOperation(job.Operations[0], job, before: null));

            Assert.Equal("B,C,A", Names(job));
        }

        [Fact]
        public void Dropping_an_operation_in_front_of_itself_changes_nothing()
        {
            JobDocument document = OneJob(out Job job);
            Operation b = job.Operations[1];

            Assert.False(document.MoveOperation(b, job, before: b));
            Assert.Equal("A,B,C", Names(job));
        }

        /// <summary>
        /// The other way of asking for where it already is: the row below it. Both have to
        /// answer false, or a drop that moves nothing still marks toolpaths stale and the
        /// document dirty.
        /// </summary>
        [Fact]
        public void Dropping_an_operation_in_front_of_the_row_it_already_precedes_changes_nothing()
        {
            JobDocument document = OneJob(out Job job);
            Operation a = job.Operations[0];
            Operation b = job.Operations[1];

            Assert.False(document.MoveOperation(a, job, before: b));
            Assert.Equal("A,B,C", Names(job));
        }

        [Fact]
        public void Dropping_the_last_operation_at_the_end_changes_nothing()
        {
            JobDocument document = OneJob(out Job job);

            Assert.False(document.MoveOperation(job.Operations[2], job, before: null));
        }

        [Fact]
        public void An_operation_moves_to_another_job()
        {
            var document = new JobDocument();
            Job first = document.AddNew();
            Job second = document.AddNew();
            Operation moved = Add(first, "A");
            Add(first, "B");
            Operation c = Add(second, "C");

            Assert.True(document.MoveOperation(moved, second, before: c));

            Assert.Equal("B", Names(first));
            Assert.Equal("A,C", Names(second));
        }

        /// <summary>
        /// Names are unique within a job and not across the part, so an operation arriving
        /// from another job can perfectly well collide with one already there.
        /// </summary>
        [Fact]
        public void An_operation_arriving_under_a_name_already_taken_is_renamed()
        {
            var document = new JobDocument();
            Job first = document.AddNew();
            Job second = document.AddNew();
            Operation moved = Add(first, "2D Contour1");
            Add(second, "2D Contour1");

            document.MoveOperation(moved, second, before: null);

            Assert.Equal(2, second.Operations.Count);
            Assert.NotEqual(second.Operations[0].Name, second.Operations[1].Name);
            Assert.Same(moved, second.Operations[1]);
        }

        [Fact]
        public void An_operation_keeps_its_toolpath_when_it_moves()
        {
            var document = new JobDocument();
            Job first = document.AddNew();
            Job second = document.AddNew();
            Operation moved = Add(first, "A");
            moved.Toolpath = new Toolpath();
            moved.State = OperationState.Generated;

            document.MoveOperation(moved, second, before: null);

            // Whether it can still be trusted is Staleness' call, not the model's - it is
            // still the last thing the machine cut.
            Assert.NotNull(moved.Toolpath);
        }

        [Fact]
        public void An_operation_this_document_does_not_hold_does_not_move()
        {
            JobDocument document = OneJob(out Job job);

            Assert.False(document.MoveOperation(
                new Operation(new Contour2dSettings()), job, before: null));
            Assert.Equal("A,B,C", Names(job));
        }

        [Fact]
        public void A_move_into_a_job_this_document_does_not_hold_does_nothing()
        {
            JobDocument document = OneJob(out Job job);

            Assert.False(document.MoveOperation(job.Operations[0], new Job(), before: null));
            Assert.Equal("A,B,C", Names(job));
        }

        [Fact]
        public void A_job_moves_in_front_of_the_job_dropped_on()
        {
            var document = new JobDocument();
            document.AddNew().Name = "Job1";
            document.AddNew().Name = "Job2";
            document.AddNew().Name = "Job3";

            Assert.True(document.MoveJob(document.Jobs[2], before: document.Jobs[0]));

            Assert.Equal("Job3,Job1,Job2", JobNames(document));
        }

        [Fact]
        public void A_job_moves_down_past_the_one_it_is_dropped_in_front_of()
        {
            var document = new JobDocument();
            document.AddNew().Name = "Job1";
            document.AddNew().Name = "Job2";
            document.AddNew().Name = "Job3";

            document.MoveJob(document.Jobs[0], before: document.Jobs[2]);

            Assert.Equal("Job2,Job1,Job3", JobNames(document));
        }

        /// <summary>
        /// Order is not a statement about which job you are working on, so moving one must
        /// not quietly hand the default to somebody else.
        /// </summary>
        [Fact]
        public void Moving_a_job_leaves_the_default_where_it_was()
        {
            var document = new JobDocument();
            Job first = document.AddNew();
            Job second = document.AddNew();
            document.MakeDefault(second);

            document.MoveJob(second, before: first);

            Assert.True(document.IsDefault(second));
            Assert.False(document.IsDefault(first));
        }

        [Fact]
        public void A_job_dropped_where_it_already_is_changes_nothing()
        {
            var document = new JobDocument();
            Job first = document.AddNew();
            Job second = document.AddNew();

            Assert.False(document.MoveJob(first, before: second));
            Assert.False(document.MoveJob(second, before: null));
        }
    }
}
