using System;
using GCam.Core.Diagnostics;
using GCam.Core.Model;
using GCam.Core.Strategies.Contour2d;
using Xunit;

namespace GCam.Core.Tests.Model
{
    /// <summary>
    /// Deleting, duplicating and renaming one operation - what the job tree's context
    /// menu asks the model for.
    /// </summary>
    /// <remarks>
    /// These rules live on <see cref="Job"/> for the same reason the job-level ones live
    /// on <see cref="JobDocument"/>: they are rules about the model, and this is the only
    /// place a headless test can reach them. There is no GCam.UI.Tests project.
    /// </remarks>
    public class OperationEditingTests
    {
        private static Operation Named(string name) =>
            new Operation(new Contour2dSettings()) { Name = name };

        private static Job JobWith(params string[] names)
        {
            var job = new Job { Name = "Roughing" };

            foreach (string name in names)
            {
                job.Operations.Add(Named(name));
            }

            return job;
        }

        // ---- Removing --------------------------------------------------------

        [Fact]
        public void Removing_an_operation_takes_it_out_of_the_job()
        {
            Job job = JobWith("Contour1", "Contour2");
            Operation first = job.Operations[0];

            Assert.True(job.RemoveOperation(first));

            Assert.Single(job.Operations);
            Assert.Equal("Contour2", job.Operations[0].Name);
        }

        [Fact]
        public void Removing_an_operation_that_is_not_in_the_job_changes_nothing()
        {
            Job job = JobWith("Contour1");

            Assert.False(job.RemoveOperation(Named("Contour1")));
            Assert.False(job.RemoveOperation(null));
            Assert.Single(job.Operations);
        }

        [Fact]
        public void A_name_freed_by_a_delete_is_offered_again()
        {
            // The gap-filling rule NextOperationName already has, seen from the other end:
            // delete the second of three and the next one you make is 2, not 4.
            Job job = JobWith("2D Contour1", "2D Contour2", "2D Contour3");

            job.RemoveOperation(job.Operations[1]);

            Assert.Equal("2D Contour2", job.NextOperationName("2D Contour"));
        }

        // ---- Duplicating -----------------------------------------------------

        [Fact]
        public void A_duplicate_sits_directly_after_the_original()
        {
            Job job = JobWith("Contour1", "Contour2");

            Operation copy = job.DuplicateOperation(job.Operations[0]);

            Assert.Equal(3, job.Operations.Count);
            Assert.Same(copy, job.Operations[1]);
        }

        [Fact]
        public void A_duplicate_gets_a_fresh_id_and_a_free_name()
        {
            Job job = JobWith("Contour1");
            Operation original = job.Operations[0];

            Operation copy = job.DuplicateOperation(original);

            Assert.NotEqual(original.Id, copy.Id);
            Assert.Equal("Contour1 (2)", copy.Name);
        }

        [Fact]
        public void Duplicating_twice_does_not_repeat_the_name()
        {
            Job job = JobWith("Contour1");

            job.DuplicateOperation(job.Operations[0]);
            Operation second = job.DuplicateOperation(job.Operations[0]);

            Assert.Equal("Contour1 (3)", second.Name);
        }

        [Fact]
        public void A_duplicate_starts_ungenerated_and_without_a_toolpath()
        {
            // A path computed for something else is worse than no path: it would draw and
            // post while claiming to belong to an operation nobody has generated.
            Job job = JobWith("Contour1");
            job.Operations[0].Toolpath = new Toolpath();
            job.Operations[0].State = OperationState.Generated;

            Operation copy = job.DuplicateOperation(job.Operations[0]);

            Assert.Null(copy.Toolpath);
            Assert.Equal(OperationState.NotGenerated, copy.State);
        }

        [Fact]
        public void Duplicating_an_operation_from_another_job_is_refused()
        {
            Job job = JobWith("Contour1");

            Assert.Throws<ArgumentException>(() => job.DuplicateOperation(Named("Elsewhere")));
            Assert.Throws<ArgumentNullException>(() => job.DuplicateOperation(null));
        }

        // ---- Renaming --------------------------------------------------------

        [Fact]
        public void An_operation_renames()
        {
            Job job = JobWith("Contour1");

            job.RenameOperation(job.Operations[0], "  Finish pass  ");

            Assert.Equal("Finish pass", job.Operations[0].Name);
        }

        [Fact]
        public void A_blank_operation_name_is_refused()
        {
            Job job = JobWith("Contour1");

            GCamUserException ex = Assert.Throws<GCamUserException>(
                () => job.RenameOperation(job.Operations[0], "   "));

            Assert.Contains("needs a name", ex.Message);
            Assert.Equal("Contour1", job.Operations[0].Name);
        }

        [Fact]
        public void A_name_another_operation_in_the_job_has_is_refused()
        {
            Job job = JobWith("Contour1", "Contour2");

            GCamUserException ex = Assert.Throws<GCamUserException>(
                () => job.RenameOperation(job.Operations[1], "contour1"));

            Assert.Contains("already an operation", ex.Message);
            Assert.Equal("Contour2", job.Operations[1].Name);
        }

        [Fact]
        public void Renaming_an_operation_to_what_it_is_already_called_is_not_a_clash()
        {
            Job job = JobWith("Contour1");

            job.RenameOperation(job.Operations[0], "Contour1");

            Assert.Equal("Contour1", job.Operations[0].Name);
        }

        [Fact]
        public void A_name_used_in_a_different_job_is_free()
        {
            // Names are unique within a job, not across the part - two jobs may each have
            // a "2D Contour1" without anybody being confused about which is meant.
            Job first = JobWith("2D Contour1");
            Job second = JobWith("Facing1");

            second.RenameOperation(second.Operations[0], "2D Contour1");

            Assert.Equal("2D Contour1", second.Operations[0].Name);
            Assert.Equal("2D Contour1", first.Operations[0].Name);
        }
    }
}
