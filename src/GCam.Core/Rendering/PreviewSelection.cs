using System.Collections.Generic;
using System.Linq;
using GCam.Core.Model;

namespace GCam.Core.Rendering
{
    /// <summary>
    /// What the 3D view should be showing: which jobs' stock, whose origin, and which
    /// operations' toolpaths.
    /// </summary>
    /// <remarks>
    /// <b>The rule this exists to hold is which of those three follows which selection.</b>
    /// A job shows its stock; an operation shows its toolpath and nothing else of its
    /// job's; and either one shows the coordinate system it is measured in, because an
    /// origin with no picture of what it belongs to is the half a user is usually checking.
    /// Selecting a job therefore does not litter the view with every toolpath it holds,
    /// and selecting an operation does not hide the origin its numbers are in.
    ///
    /// Grouped by job rather than kept as two flat lists because that is the shape the
    /// renderer needs: resolving a job's coordinate system and measuring the model along
    /// its axes is the expensive half, and everything drawn for that job wants exactly it.
    ///
    /// Built through <see cref="Builder"/>, which is what turns a tree selection - jobs and
    /// operations in whatever order they were clicked - into that grouping.
    ///
    /// <b>This says what is selected, not what can be drawn.</b> An operation with no
    /// toolpath, or a suppressed one, still belongs here; whether it produces anything on
    /// screen is the renderer's call, so that the two questions stay separable.
    /// </remarks>
    public sealed class PreviewSelection
    {
        /// <summary>Nothing selected: the 3D view shows none of G-CAM's own drawing.</summary>
        public static readonly PreviewSelection Empty = new PreviewSelection(new PreviewedJob[0]);

        private PreviewSelection(IReadOnlyList<PreviewedJob> jobs)
        {
            Jobs = jobs;
        }

        /// <summary>The jobs with something to draw, in the order they were added.</summary>
        public IReadOnlyList<PreviewedJob> Jobs { get; }

        public bool IsEmpty => Jobs.Count == 0;

        /// <summary>
        /// One job, showing its stock and its origin and none of its toolpaths.
        /// </summary>
        /// <remarks>
        /// What the Job property page previews as it is edited, and what selecting a job in
        /// the tree amounts to.
        /// </remarks>
        public static PreviewSelection ForJob(Job job)
        {
            var builder = new Builder();
            builder.AddJob(job);
            return builder.Build();
        }

        /// <summary>
        /// Collects a selection of jobs and operations into the grouping the renderer
        /// wants.
        /// </summary>
        /// <remarks>
        /// Order is preserved and repeats are folded in: a job that is selected in its own
        /// right and also has a selected operation is one entry showing both, whichever way
        /// round they were clicked.
        /// </remarks>
        public sealed class Builder
        {
            private readonly List<Entry> _entries = new List<Entry>();

            /// <summary>Adds a job selected in its own right - stock and origin.</summary>
            public Builder AddJob(Job job)
            {
                if (job != null)
                {
                    For(job).ShowStock = true;
                }

                return this;
            }

            /// <summary>
            /// Adds a selected operation - its toolpath, and its job's origin.
            /// </summary>
            /// <remarks>
            /// The job is required: a toolpath is computed in the job's frame, so there is
            /// nowhere to draw one without it.
            /// </remarks>
            public Builder AddOperation(Job job, Operation operation)
            {
                if (job == null || operation == null)
                {
                    return this;
                }

                Entry entry = For(job);

                if (!entry.Operations.Contains(operation))
                {
                    entry.Operations.Add(operation);
                }

                return this;
            }

            public PreviewSelection Build()
            {
                if (_entries.Count == 0)
                {
                    return Empty;
                }

                return new PreviewSelection(
                    _entries.Select(e => new PreviewedJob(e.Job, e.ShowStock, e.Operations))
                        .ToArray());
            }

            private Entry For(Job job)
            {
                Entry existing = _entries.FirstOrDefault(e => ReferenceEquals(e.Job, job));

                if (existing != null)
                {
                    return existing;
                }

                var entry = new Entry(job);
                _entries.Add(entry);

                return entry;
            }

            private sealed class Entry
            {
                public Entry(Job job)
                {
                    Job = job;
                }

                public Job Job { get; }

                public bool ShowStock { get; set; }

                public List<Operation> Operations { get; } = new List<Operation>();
            }
        }
    }

    /// <summary>One job's share of a <see cref="PreviewSelection"/>.</summary>
    /// <remarks>
    /// The origin is not a flag: a job is only in the selection at all because something
    /// belonging to it is selected, and that is exactly when its coordinate system is worth
    /// seeing.
    /// </remarks>
    public sealed class PreviewedJob
    {
        internal PreviewedJob(Job job, bool showStock, IReadOnlyList<Operation> operations)
        {
            Job = job;
            ShowStock = showStock;
            Operations = operations;
        }

        public Job Job { get; }

        /// <summary>True when the job itself is selected, rather than only an operation.</summary>
        public bool ShowStock { get; }

        /// <summary>The selected operations of this job, in selection order.</summary>
        public IReadOnlyList<Operation> Operations { get; }
    }
}
