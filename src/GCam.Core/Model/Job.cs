using System;
using System.Collections.Generic;
using System.Linq;

namespace GCam.Core.Model
{
    /// <summary>
    /// A job: what is being machined, out of what, and in which coordinate system.
    /// Operations live inside it.
    /// </summary>
    /// <remarks>
    /// Equivalent to an HSMWorks Setup. There is no separate Setup level in G-CAM - see
    /// docs/decisions/0004-jobs-own-operations-directly.md.
    ///
    /// The machine is deliberately absent. Only the work offset is modelled, because it
    /// is the one machine-side value that changes per job.
    /// </remarks>
    public sealed class Job
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("D");

        public string Name { get; set; }

        /// <summary>
        /// The solid bodies this job machines. Empty means every solid body in the part.
        /// </summary>
        /// <remarks>
        /// Names, not persistent references, and that is a temporary choice tied to jobs
        /// not being saved yet. Once a job survives a reopen these must become persistent
        /// reference ids from IModelDocExtension::GetPersistReference3 - renaming a body
        /// must not silently change what a proven job cuts. Same for
        /// <see cref="CoordinateSystemName"/>.
        /// </remarks>
        public List<string> ModelBodyNames { get; set; } = new List<string>();

        /// <summary>
        /// The coordinate system feature defining this job's origin and orientation.
        /// Null means the part origin.
        /// </summary>
        public string CoordinateSystemName { get; set; }

        public Stock Stock { get; set; } = new Stock();

        /// <summary>Work offset number - see <see cref="WorkOffsets"/>. 1 is G54.</summary>
        public int WorkOffset { get; set; } = WorkOffsets.First;

        public List<Operation> Operations { get; set; } = new List<Operation>();

        /// <summary>
        /// Fields G-CAM has no property for, preserved rather than dropped. Same
        /// contract as <see cref="Tooling.Tool.Extra"/>.
        /// </summary>
        public Dictionary<string, string> Extra { get; set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>True when the job machines every solid body rather than a chosen set.</summary>
        public bool MachinesWholePart => ModelBodyNames == null || ModelBodyNames.Count == 0;

        /// <summary>
        /// What the coordinate system box shows, including the part-origin default.
        /// </summary>
        public string CoordinateSystemDisplayName =>
            string.IsNullOrWhiteSpace(CoordinateSystemName) ? "Part origin" : CoordinateSystemName;

        /// <summary>Deep copy, keeping the id.</summary>
        public Job Clone()
        {
            return new Job
            {
                Id = Id,
                Name = Name,
                ModelBodyNames = new List<string>(ModelBodyNames ?? new List<string>()),
                CoordinateSystemName = CoordinateSystemName,
                Stock = Stock?.Clone() ?? new Stock(),
                WorkOffset = WorkOffset,
                Operations = (Operations ?? new List<Operation>()).Select(o => o.Clone()).ToList(),
                Extra = new Dictionary<string, string>(
                    Extra ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase),
            };
        }

        /// <summary>
        /// Deep copy with fresh ids throughout, for a duplicate that stands on its own.
        /// </summary>
        /// <remarks>
        /// The operations get new ids too. A duplicated job that shared operation ids
        /// with its original would make "which operation produced this toolpath"
        /// unanswerable the moment either was edited.
        /// </remarks>
        public Job CloneAsNew()
        {
            Job copy = Clone();
            copy.Id = Guid.NewGuid().ToString("D");
            copy.Operations = copy.Operations.Select(o => o.CloneAsNew()).ToList();
            return copy;
        }

        /// <summary>
        /// Problems a user can act on. Empty when the job is usable.
        /// </summary>
        public IReadOnlyList<string> Validate()
        {
            var problems = new List<string>();

            if (string.IsNullOrWhiteSpace(Name))
            {
                problems.Add("A job needs a name.");
            }

            if (!WorkOffsets.IsStandard(WorkOffset))
            {
                problems.Add(
                    $"Work offset {WorkOffset} is not one of G54 to G59.");
            }

            problems.AddRange(Stock?.Validate() ?? new List<string>());

            return problems;
        }

        public override string ToString() => Name ?? "Job";
    }
}
