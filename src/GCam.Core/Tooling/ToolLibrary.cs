using System;
using System.Collections.Generic;
using System.Linq;

namespace GCam.Core.Tooling
{
    /// <summary>
    /// In-memory tool library. What a reader produces and an editor mutates.
    /// </summary>
    public sealed class ToolLibrary : IToolLibrary
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("D");

        public string Name { get; set; }

        /// <summary>
        /// Where this library was loaded from, if anywhere. Not part of the file -
        /// it is set by the reader so the UI can show and re-save the path.
        /// </summary>
        public string SourcePath { get; set; }

        public List<Tool> Tools { get; } = new List<Tool>();

        public List<Holder> Holders { get; } = new List<Holder>();

        IReadOnlyList<Tool> IToolLibrary.Tools => Tools;

        IReadOnlyList<Holder> IToolLibrary.Holders => Holders;

        public Tool FindById(string toolId)
        {
            if (string.IsNullOrEmpty(toolId))
            {
                return null;
            }

            return Tools.FirstOrDefault(t => string.Equals(t.Id, toolId, StringComparison.Ordinal));
        }

        public Tool FindByNumber(int number)
        {
            return Tools.FirstOrDefault(t => t.Number == number);
        }

        public Holder FindHolderById(string holderId)
        {
            if (string.IsNullOrEmpty(holderId))
            {
                return null;
            }

            return Holders.FirstOrDefault(h => string.Equals(h.Id, holderId, StringComparison.Ordinal));
        }

        /// <summary>
        /// Takes a copy of a tool for embedding in a job, stamped with this library's id.
        /// </summary>
        /// <exception cref="ArgumentException">No tool with that id is in this library.</exception>
        public Tool CheckOut(string toolId)
        {
            Tool tool = FindById(toolId);
            if (tool == null)
            {
                throw new ArgumentException($"No tool with id '{toolId}' in library '{Name}'.", nameof(toolId));
            }

            return tool.CloneForJob(Id);
        }

        /// <summary>
        /// The lowest tool number not already used, starting at 1.
        /// </summary>
        /// <remarks>
        /// Fills gaps rather than always appending: a shop that has retired T7 wants the
        /// next tool to take it, not to climb forever.
        /// </remarks>
        public int NextToolNumber()
        {
            var used = new HashSet<int>(Tools.Select(t => t.Number));
            int candidate = 1;
            while (used.Contains(candidate))
            {
                candidate++;
            }

            return candidate;
        }

        /// <summary>
        /// Problems across the whole library, each prefixed with the tool it came from.
        /// </summary>
        public IReadOnlyList<string> Validate()
        {
            var problems = new List<string>();

            foreach (Tool tool in Tools)
            {
                problems.AddRange(tool.Validate().Select(p => $"{tool}: {p}"));
            }

            foreach (IGrouping<string, Tool> duplicate in Tools
                         .Where(t => !string.IsNullOrEmpty(t.Id))
                         .GroupBy(t => t.Id, StringComparer.Ordinal)
                         .Where(g => g.Count() > 1))
            {
                problems.Add($"Duplicate tool id '{duplicate.Key}' used by {duplicate.Count()} tools.");
            }

            return problems;
        }
    }
}
