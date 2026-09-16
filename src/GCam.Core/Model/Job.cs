using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GCam.Core.Diagnostics;

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
        /// Persistent references, because a job outlives the session that made it. A body
        /// renamed between sessions must not silently change what a proven job cuts, and a
        /// stored name cannot tell the difference between a rename and a different body
        /// that has taken the old name.
        ///
        /// <see cref="GeometryRef.DisplayName"/> still carries the name, for the UI and as
        /// the fallback that lets a part saved before this change keep working.
        /// </remarks>
        public List<GeometryRef> ModelBodies { get; set; } = new List<GeometryRef>();

        /// <summary>
        /// The coordinate system feature defining this job's origin and orientation.
        /// Null means the part origin.
        /// </summary>
        public GeometryRef CoordinateSystem { get; set; }

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
        public bool MachinesWholePart => ModelBodies == null || ModelBodies.Count == 0;

        /// <summary>
        /// What the coordinate system box shows, including the part-origin default.
        /// </summary>
        public string CoordinateSystemDisplayName =>
            CoordinateSystem == null || CoordinateSystem.IsEmpty
                ? "Part origin"
                : CoordinateSystem.ToString();

        /// <summary>
        /// The lowest "<paramref name="stem"/> n" this job does not already use.
        /// </summary>
        /// <remarks>
        /// Here rather than in the caller for the same reason job naming is in
        /// <see cref="JobDocument"/>: it is a rule about the model, and a headless test can
        /// reach it. Fills gaps rather than climbing forever, so deleting the second of
        /// three operations and making a new one gives you 2 back, not 4.
        /// </remarks>
        public string NextOperationName(string stem)
        {
            stem = string.IsNullOrWhiteSpace(stem) ? "Operation" : stem.Trim();

            int n = 1;
            while (Operations.Any(o => string.Equals(
                       o?.Name, stem + n, StringComparison.OrdinalIgnoreCase)))
            {
                n++;
            }

            return stem + n;
        }

        /// <summary>Takes an operation out of this job.</summary>
        /// <remarks>
        /// The operation's toolpath goes with it. Marking whatever machined what it left
        /// behind is <see cref="Generation.Staleness.OperationOrderChanged"/>'s job and
        /// the caller's to ask for - Model cannot reference Generation, and the rule is
        /// the same one reordering needs.
        /// </remarks>
        public bool RemoveOperation(Operation operation)
        {
            return operation != null && Operations.Remove(operation);
        }

        /// <summary>
        /// Copies an operation, placing the copy directly after the original.
        /// </summary>
        /// <remarks>
        /// The same shape as <see cref="JobDocument.Duplicate"/>: a fresh id, a unique
        /// name, and a position that says what it came from. The copy starts ungenerated
        /// because <see cref="Operation.CloneAsNew"/> drops the toolpath - a path computed
        /// for something else is worse than no path at all.
        /// </remarks>
        public Operation DuplicateOperation(Operation operation)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            int index = Operations.IndexOf(operation);
            if (index < 0)
            {
                throw new ArgumentException("That operation is not in this job.", nameof(operation));
            }

            Operation copy = operation.CloneAsNew();
            copy.Name = MakeOperationNameUnique(operation.Name, copy);

            Operations.Insert(index + 1, copy);
            return copy;
        }

        /// <summary>
        /// Renames an operation.
        /// </summary>
        /// <remarks>
        /// Names are unique within a job rather than across the part, because that is the
        /// scope <see cref="NextOperationName"/> already numbers in and the scope an
        /// operation is read in - two jobs may each have a "2D Contour1" without anyone
        /// being confused.
        /// </remarks>
        /// <exception cref="GCamUserException">
        /// The name is blank, or another operation in this job already has it.
        /// </exception>
        public void RenameOperation(Operation operation, string newName)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            string trimmed = (newName ?? string.Empty).Trim();

            if (trimmed.Length == 0)
            {
                throw new GCamUserException("An operation needs a name.");
            }

            if (string.Equals(trimmed, operation.Name, StringComparison.Ordinal))
            {
                return;
            }

            if (IsOperationNameTaken(trimmed, operation))
            {
                throw new GCamUserException(
                    $"There is already an operation called '{trimmed}' in this job.");
            }

            operation.Name = trimmed;
        }

        /// <summary>
        /// <paramref name="wanted"/> if it is free in this job, otherwise it with " (2)",
        /// " (3)" and so on appended until it is.
        /// </summary>
        private string MakeOperationNameUnique(string wanted, Operation exclude)
        {
            string baseName = string.IsNullOrWhiteSpace(wanted)
                ? NextOperationName("Operation")
                : wanted.Trim();

            if (!IsOperationNameTaken(baseName, exclude))
            {
                return baseName;
            }

            int suffix = 2;
            string candidate;
            do
            {
                candidate = baseName + " (" + suffix.ToString(CultureInfo.InvariantCulture) + ")";
                suffix++;
            }
            while (IsOperationNameTaken(candidate, exclude));

            return candidate;
        }

        private bool IsOperationNameTaken(string name, Operation exclude)
        {
            return Operations.Any(o => !ReferenceEquals(o, exclude)
                                       && string.Equals(o?.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Deep copy, keeping the id.</summary>
        public Job Clone()
        {
            return new Job
            {
                Id = Id,
                Name = Name,
                ModelBodies = (ModelBodies ?? new List<GeometryRef>())
                    .Select(b => b?.Clone()).Where(b => b != null).ToList(),
                CoordinateSystem = CoordinateSystem?.Clone(),
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
