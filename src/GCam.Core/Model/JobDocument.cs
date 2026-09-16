using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GCam.Core.Diagnostics;
using GCam.Core.Tooling;

namespace GCam.Core.Model
{
    /// <summary>
    /// Every job belonging to one part, and which of them is the default.
    /// </summary>
    /// <remarks>
    /// One of these per open document. It owns the rules the tree and the property page
    /// both need - unique names, what the default is, what happens to it when a job is
    /// deleted - so that neither has to know them and both can be tested without either.
    ///
    /// Saved inside the SOLIDWORKS part and reloaded when it opens - see
    /// docs/solidworks-api/third-party-storage.md. An edit has to mark the document
    /// dirty, or SOLIDWORKS never offers the save that would write it.
    /// </remarks>
    public sealed class JobDocument
    {
        private const string DefaultNameStem = "Job";

        private readonly List<Job> _jobs = new List<Job>();
        private readonly List<Tool> _tools = new List<Tool>();

        public IReadOnlyList<Job> Jobs => _jobs;

        /// <summary>
        /// The tools copied into this part, shared by every job and operation in it.
        /// </summary>
        /// <remarks>
        /// One list per part rather than per job, because that is what the machine looks
        /// like: a tool is in the carousel regardless of which setup is running. An
        /// operation refers to one of these by <see cref="Operation.ToolId"/> and never
        /// holds its own copy, so two operations cannot disagree about the shape of one
        /// physical cutter. See docs/decisions/0008-document-tool-list.md.
        ///
        /// What each side owns: the tool here carries identity, number, cutter geometry
        /// and holder; the operation carries its own spindle speed, feeds and coolant.
        /// </remarks>
        public IReadOnlyList<Tool> Tools => _tools;

        /// <summary>
        /// True when there is nothing here worth writing into the part.
        /// </summary>
        /// <remarks>
        /// **Jobs only.** A part with no jobs is a part G-CAM has not been used on, and
        /// writing our storage node into it would put a few hundred bytes of empty XML
        /// into every part anyone opens and saves with the add-in loaded - including every
        /// part that has nothing to do with CAM.
        ///
        /// **Tools are deliberately not counted, and that has a cost.** A tool is checked
        /// into the part the moment it is picked, before any operation commits, so a part
        /// can hold tools and no jobs - and this says such a part is not worth storing, so
        /// those tools are dropped on the next save. That is a narrow window in practice:
        /// once a part has been saved with a job in it the storage exists, and
        /// <c>JobStorageHook</c> keeps writing from then on whatever the document holds, so
        /// the tools survive. The exposed case is picking a tool, abandoning the job, and
        /// saving before anything else was ever stored.
        ///
        /// Used only to decide whether to write. Nothing else should treat a tool-only
        /// document as empty.
        /// </remarks>
        public bool HasNothingToStore => _jobs.Count == 0;

        public Tool FindTool(string toolId)
        {
            if (string.IsNullOrEmpty(toolId))
            {
                return null;
            }

            return _tools.FirstOrDefault(t => string.Equals(t.Id, toolId, StringComparison.Ordinal));
        }

        /// <summary>
        /// Puts a tool checked out of a library into this part, or returns the copy that
        /// is already here.
        /// </summary>
        /// <remarks>
        /// Idempotent by <see cref="Tool.Id"/>, which is what stops a second operation
        /// using the same library tool from making a second copy of it. The rule lives
        /// here rather than in the picker, so it holds however the tool arrives.
        ///
        /// The tool keeps the number it had in the library. Renumbering silently would be
        /// wrong - the number is the machine's, not ours - so a clash is reported by
        /// <see cref="ValidateTools"/> instead.
        /// </remarks>
        public Tool AddTool(Tool tool)
        {
            if (tool == null)
            {
                throw new ArgumentNullException(nameof(tool));
            }

            Tool existing = FindTool(tool.Id);
            if (existing != null)
            {
                return existing;
            }

            _tools.Add(tool);
            return tool;
        }

        /// <summary>
        /// Takes a tool out of the part.
        /// </summary>
        /// <remarks>
        /// An unused tool stays in the list until someone removes it deliberately -
        /// pruning on save would silently drop a tool that had been customised while the
        /// operation using it was being rebuilt.
        /// </remarks>
        /// <exception cref="GCamUserException">
        /// Operations are still using it. Naming them is the point: "in use" without
        /// saying where sends someone hunting through every job.
        /// </exception>
        public bool RemoveTool(Tool tool)
        {
            if (tool == null)
            {
                return false;
            }

            IReadOnlyList<ToolUse> uses = ToolUsage.UsesOf(this, tool.Id);
            if (uses.Count > 0)
            {
                throw new GCamUserException(
                    $"'{tool.DisplayName}' is used by {ToolUsage.Describe(uses)}. " +
                    "Change those operations to another tool first.");
            }

            return _tools.Remove(tool);
        }

        /// <summary>
        /// Problems with the part's tooling as a set. Empty when it is usable.
        /// </summary>
        /// <remarks>
        /// Two tools sharing a number is the one that matters: the machine has a single
        /// pocket 4, so a part claiming two different cutters live there cannot be set up
        /// as written. Not fatal - it is caught before posting, and a number is easy to
        /// change - so it is reported rather than refused.
        /// </remarks>
        public IReadOnlyList<string> ValidateTools()
        {
            var problems = new List<string>();

            IEnumerable<IGrouping<int, Tool>> clashes = _tools
                .Where(t => t.Number > 0)
                .GroupBy(t => t.Number)
                .Where(g => g.Count() > 1);

            foreach (IGrouping<int, Tool> clash in clashes)
            {
                problems.Add(
                    $"Tool number {clash.Key} is used by more than one tool: " +
                    string.Join(", ", clash.Select(t => "'" + t.DisplayName + "'")) + ".");
            }

            return problems;
        }

        /// <summary>
        /// The job that New Operation targets when no job is selected. Null only when
        /// there are no jobs at all.
        /// </summary>
        public Job DefaultJob { get; private set; }

        public bool IsDefault(Job job) => job != null && ReferenceEquals(job, DefaultJob);

        public Job FindById(string jobId)
        {
            if (string.IsNullOrEmpty(jobId))
            {
                return null;
            }

            return _jobs.FirstOrDefault(j => string.Equals(j.Id, jobId, StringComparison.Ordinal));
        }

        /// <summary>
        /// Adds a job, giving it a unique name if it has none or clashes, and making it
        /// the default if it is the first.
        /// </summary>
        public Job Add(Job job)
        {
            if (job == null)
            {
                throw new ArgumentNullException(nameof(job));
            }

            job.Name = string.IsNullOrWhiteSpace(job.Name)
                ? NextDefaultName()
                : MakeUnique(job.Name.Trim(), job);

            _jobs.Add(job);

            if (DefaultJob == null)
            {
                DefaultJob = job;
            }

            return job;
        }

        /// <summary>
        /// Creates an empty job with the next free name and adds it.
        /// </summary>
        public Job AddNew() => Add(new Job());

        /// <summary>
        /// Removes a job and its operations, moving the default if it was the one
        /// removed.
        /// </summary>
        public bool Remove(Job job)
        {
            if (job == null || !_jobs.Remove(job))
            {
                return false;
            }

            // The default has to land somewhere, or New Operation has no target and the
            // tree shows no default at all.
            if (ReferenceEquals(DefaultJob, job))
            {
                DefaultJob = _jobs.FirstOrDefault();
            }

            return true;
        }

        /// <summary>
        /// Copies a job and everything in it, placing the copy directly after the
        /// original. The copy is never the default - duplicating is not a statement
        /// about which job you are working on.
        /// </summary>
        public Job Duplicate(Job job)
        {
            if (job == null)
            {
                throw new ArgumentNullException(nameof(job));
            }

            int index = _jobs.IndexOf(job);
            if (index < 0)
            {
                throw new ArgumentException("That job is not in this document.", nameof(job));
            }

            Job copy = job.CloneAsNew();
            copy.Name = MakeUnique(job.Name, copy);

            _jobs.Insert(index + 1, copy);
            return copy;
        }

        /// <summary>
        /// Renames a job.
        /// </summary>
        /// <exception cref="GCamUserException">
        /// The name is blank, or another job already has it.
        /// </exception>
        public void Rename(Job job, string newName)
        {
            if (job == null)
            {
                throw new ArgumentNullException(nameof(job));
            }

            string trimmed = (newName ?? string.Empty).Trim();

            if (trimmed.Length == 0)
            {
                throw new GCamUserException("A job needs a name.");
            }

            if (string.Equals(trimmed, job.Name, StringComparison.Ordinal))
            {
                return;
            }

            if (IsNameTaken(trimmed, job))
            {
                throw new GCamUserException($"There is already a job called '{trimmed}'.");
            }

            job.Name = trimmed;
        }

        public void MakeDefault(Job job)
        {
            if (job == null)
            {
                throw new ArgumentNullException(nameof(job));
            }

            if (!_jobs.Contains(job))
            {
                throw new ArgumentException("That job is not in this document.", nameof(job));
            }

            DefaultJob = job;
        }

        /// <summary>
        /// The lowest "Job n" not already taken.
        /// </summary>
        /// <remarks>
        /// Fills gaps rather than climbing forever, the same way
        /// <see cref="Tooling.ToolLibrary.NextToolNumber"/> does: delete Job 2 of three
        /// and the next one you make is Job 2, not Job 4.
        /// </remarks>
        public string NextDefaultName()
        {
            int n = 1;
            while (IsNameTaken(FormatDefaultName(n), null))
            {
                n++;
            }

            return FormatDefaultName(n);
        }

        private static string FormatDefaultName(int n)
        {
            return DefaultNameStem + " " + n.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// <paramref name="wanted"/> if it is free, otherwise it with " (2)", " (3)" and
        /// so on appended until it is.
        /// </summary>
        private string MakeUnique(string wanted, Job exclude)
        {
            string baseName = string.IsNullOrWhiteSpace(wanted) ? DefaultNameStem : wanted.Trim();

            if (!IsNameTaken(baseName, exclude))
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
            while (IsNameTaken(candidate, exclude));

            return candidate;
        }

        private bool IsNameTaken(string name, Job exclude)
        {
            return _jobs.Any(j => !ReferenceEquals(j, exclude)
                                  && string.Equals(j.Name, name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
