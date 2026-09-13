using System.Linq;
using GCam.Core.Diagnostics;
using GCam.Core.Model;
using Xunit;

namespace GCam.Core.Tests.Model
{
    public class JobDocumentTests
    {
        private static Job SampleJob(string name = null)
        {
            return new Job { Name = name };
        }

        [Fact]
        public void The_first_job_added_becomes_the_default()
        {
            var document = new JobDocument();

            Job job = document.AddNew();

            Assert.True(document.IsDefault(job));
        }

        [Fact]
        public void Later_jobs_do_not_steal_the_default()
        {
            var document = new JobDocument();
            Job first = document.AddNew();

            document.AddNew();

            Assert.True(document.IsDefault(first));
        }

        [Fact]
        public void An_unnamed_job_is_named_Job_1_then_Job_2()
        {
            var document = new JobDocument();

            Assert.Equal("Job 1", document.AddNew().Name);
            Assert.Equal("Job 2", document.AddNew().Name);
        }

        [Fact]
        public void Default_names_fill_gaps_rather_than_climbing_forever()
        {
            // Delete the middle of three and the next job takes the freed name, the way
            // tool numbers do.
            var document = new JobDocument();
            document.AddNew();
            Job second = document.AddNew();
            document.AddNew();

            document.Remove(second);

            Assert.Equal("Job 2", document.AddNew().Name);
        }

        [Fact]
        public void A_clashing_name_is_made_unique_rather_than_refused()
        {
            var document = new JobDocument();
            document.Add(SampleJob("Roughing"));

            Job second = document.Add(SampleJob("Roughing"));

            Assert.Equal("Roughing (2)", second.Name);
        }

        [Fact]
        public void Name_clashes_ignore_case()
        {
            var document = new JobDocument();
            document.Add(SampleJob("Roughing"));

            Job second = document.Add(SampleJob("ROUGHING"));

            Assert.NotEqual("ROUGHING", second.Name);
        }

        [Fact]
        public void Removing_the_default_moves_it_to_another_job()
        {
            var document = new JobDocument();
            Job first = document.AddNew();
            Job second = document.AddNew();

            document.Remove(first);

            Assert.True(document.IsDefault(second));
        }

        [Fact]
        public void Removing_the_last_job_leaves_no_default()
        {
            var document = new JobDocument();
            Job only = document.AddNew();

            document.Remove(only);

            Assert.Null(document.DefaultJob);
        }

        [Fact]
        public void Removing_a_job_that_was_never_added_changes_nothing()
        {
            var document = new JobDocument();
            document.AddNew();

            Assert.False(document.Remove(SampleJob("Stranger")));
            Assert.Single(document.Jobs);
        }

        [Fact]
        public void A_duplicate_lands_directly_after_its_original()
        {
            var document = new JobDocument();
            Job first = document.AddNew();
            document.AddNew();

            Job copy = document.Duplicate(first);

            Assert.Equal(1, document.Jobs.ToList().IndexOf(copy));
        }

        [Fact]
        public void A_duplicate_gets_a_new_id_and_a_unique_name()
        {
            var document = new JobDocument();
            Job first = document.Add(SampleJob("Roughing"));

            Job copy = document.Duplicate(first);

            Assert.NotEqual(first.Id, copy.Id);
            Assert.Equal("Roughing (2)", copy.Name);
        }

        [Fact]
        public void Duplicating_does_not_change_which_job_is_default()
        {
            // Copying a job says nothing about which one you are working on.
            var document = new JobDocument();
            Job first = document.AddNew();

            Job copy = document.Duplicate(first);

            Assert.True(document.IsDefault(first));
            Assert.False(document.IsDefault(copy));
        }

        [Fact]
        public void A_duplicated_job_carries_copies_of_its_operations_not_the_originals()
        {
            var document = new JobDocument();
            Job first = document.AddNew();
            first.Operations.Add(new Operation { Name = "Contour1" });

            Job copy = document.Duplicate(first);

            Assert.Equal("Contour1", Assert.Single(copy.Operations).Name);
            Assert.False(ReferenceEquals(first.Operations[0], copy.Operations[0]));
            Assert.NotEqual(first.Operations[0].Id, copy.Operations[0].Id);
        }

        [Fact]
        public void Renaming_to_a_name_another_job_already_has_is_refused()
        {
            var document = new JobDocument();
            document.Add(SampleJob("Roughing"));
            Job second = document.Add(SampleJob("Finishing"));

            GCamUserException ex = Assert.Throws<GCamUserException>(
                () => document.Rename(second, "Roughing"));

            Assert.Contains("already a job called", ex.Message);
            Assert.Equal("Finishing", second.Name);
        }

        [Fact]
        public void Renaming_a_job_to_its_own_name_is_allowed()
        {
            // Committing an unchanged in-place edit must not look like a clash.
            var document = new JobDocument();
            Job job = document.Add(SampleJob("Roughing"));

            document.Rename(job, "Roughing");

            Assert.Equal("Roughing", job.Name);
        }

        [Fact]
        public void Renaming_trims_surrounding_whitespace()
        {
            var document = new JobDocument();
            Job job = document.AddNew();

            document.Rename(job, "  Roughing  ");

            Assert.Equal("Roughing", job.Name);
        }

        [Fact]
        public void A_blank_name_is_refused()
        {
            var document = new JobDocument();
            Job job = document.Add(SampleJob("Roughing"));

            Assert.Throws<GCamUserException>(() => document.Rename(job, "   "));
            Assert.Equal("Roughing", job.Name);
        }

        [Fact]
        public void Making_a_job_default_moves_the_flag_off_the_previous_one()
        {
            var document = new JobDocument();
            Job first = document.AddNew();
            Job second = document.AddNew();

            document.MakeDefault(second);

            Assert.True(document.IsDefault(second));
            Assert.False(document.IsDefault(first));
        }

        [Fact]
        public void FindById_matches_a_job_that_is_present()
        {
            var document = new JobDocument();
            Job job = document.AddNew();

            Assert.Same(job, document.FindById(job.Id));
        }
    }
}
