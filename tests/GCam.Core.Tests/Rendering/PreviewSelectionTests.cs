using System.Linq;
using GCam.Core.Model;
using GCam.Core.Rendering;
using GCam.Core.Strategies.Contour2d;
using Xunit;

namespace GCam.Core.Tests.Rendering
{
    public class PreviewSelectionTests
    {
        private static Job JobWith(string name, params string[] operations)
        {
            var job = new Job { Name = name };

            foreach (string operation in operations)
            {
                job.Operations.Add(new Operation(new Contour2dSettings()) { Name = operation });
            }

            return job;
        }

        [Fact]
        public void Nothing_selected_shows_nothing()
        {
            Assert.True(PreviewSelection.Empty.IsEmpty);
            Assert.True(new PreviewSelection.Builder().Build().IsEmpty);
        }

        /// <summary>
        /// The rule the whole type exists for: a selected job is its stock and its origin,
        /// and none of the toolpaths underneath it.
        /// </summary>
        [Fact]
        public void A_selected_job_shows_its_stock_and_not_its_operations()
        {
            Job job = JobWith("Job1", "Contour1", "Contour2");

            PreviewSelection selection = PreviewSelection.ForJob(job);

            PreviewedJob previewed = Assert.Single(selection.Jobs);
            Assert.Same(job, previewed.Job);
            Assert.True(previewed.ShowStock);
            Assert.Empty(previewed.Operations);
        }

        /// <summary>
        /// The other half of it: an operation brings its own toolpath and its job's origin,
        /// but not the stock box that would hide what it is cutting.
        /// </summary>
        [Fact]
        public void A_selected_operation_shows_its_toolpath_without_its_job_s_stock()
        {
            Job job = JobWith("Job1", "Contour1", "Contour2");

            var builder = new PreviewSelection.Builder();
            builder.AddOperation(job, job.Operations[1]);

            PreviewedJob previewed = Assert.Single(builder.Build().Jobs);

            Assert.False(previewed.ShowStock);
            Assert.Same(job.Operations[1], Assert.Single(previewed.Operations));
        }

        [Fact]
        public void A_job_and_one_of_its_operations_are_one_entry_showing_both()
        {
            Job job = JobWith("Job1", "Contour1");

            var builder = new PreviewSelection.Builder();
            builder.AddOperation(job, job.Operations[0]);
            builder.AddJob(job);

            PreviewedJob previewed = Assert.Single(builder.Build().Jobs);

            Assert.True(previewed.ShowStock);
            Assert.Single(previewed.Operations);
        }

        [Fact]
        public void Operations_from_two_jobs_stay_with_their_own_job()
        {
            Job first = JobWith("Job1", "Contour1");
            Job second = JobWith("Job2", "Contour2", "Contour3");

            var builder = new PreviewSelection.Builder();
            builder.AddOperation(first, first.Operations[0]);
            builder.AddOperation(second, second.Operations[1]);
            builder.AddJob(second);

            PreviewSelection selection = builder.Build();

            Assert.Equal(2, selection.Jobs.Count);
            Assert.Same(first, selection.Jobs[0].Job);
            Assert.False(selection.Jobs[0].ShowStock);
            Assert.True(selection.Jobs[1].ShowStock);
            Assert.Same(second.Operations[1], Assert.Single(selection.Jobs[1].Operations));
        }

        [Fact]
        public void The_same_operation_twice_is_drawn_once()
        {
            Job job = JobWith("Job1", "Contour1");

            var builder = new PreviewSelection.Builder();
            builder.AddOperation(job, job.Operations[0]);
            builder.AddOperation(job, job.Operations[0]);

            Assert.Single(builder.Build().Jobs.Single().Operations);
        }

        /// <summary>
        /// A toolpath is computed in its job's frame, so there is nowhere to draw one
        /// without the job. Nulls are dropped rather than throwing: this is built from a
        /// tree selection, and a node whose job has just gone is a race, not a bug.
        /// </summary>
        [Fact]
        public void An_operation_with_no_job_is_dropped()
        {
            var builder = new PreviewSelection.Builder();
            builder.AddOperation(null, new Operation(new Contour2dSettings()));
            builder.AddJob(null);

            Assert.True(builder.Build().IsEmpty);
        }

        /// <summary>
        /// Suppressed and ungenerated operations still belong to the selection - whether
        /// they draw anything is the renderer's call, so that the two questions stay
        /// separable.
        /// </summary>
        [Fact]
        public void A_suppressed_operation_is_still_part_of_the_selection()
        {
            Job job = JobWith("Job1", "Contour1");
            job.Operations[0].Enabled = false;

            var builder = new PreviewSelection.Builder();
            builder.AddOperation(job, job.Operations[0]);

            Assert.Single(builder.Build().Jobs.Single().Operations);
        }
    }
}
