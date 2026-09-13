using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GCam.Core.Diagnostics;

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
    /// Nothing here is persisted yet. A document's jobs live for as long as it is open
    /// and are lost when it closes.
    /// </remarks>
    public sealed class JobDocument
    {
        private const string DefaultNameStem = "Job";

        private readonly List<Job> _jobs = new List<Job>();

        public IReadOnlyList<Job> Jobs => _jobs;

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
