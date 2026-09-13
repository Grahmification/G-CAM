using System;
using System.Collections.Generic;
using System.Linq;
using GCam.Core.Tooling;

namespace GCam.Core.Model
{
    /// <summary>
    /// One operation's use of a tool, with the job it sits in.
    /// </summary>
    /// <remarks>
    /// The job comes along because the tool list is per part and spans jobs, so
    /// "2D Contour1" on its own does not say which one.
    /// </remarks>
    public sealed class ToolUse
    {
        public ToolUse(Job job, Operation operation)
        {
            Job = job;
            Operation = operation;
        }

        public Job Job { get; }

        public Operation Operation { get; }

        public override string ToString() =>
            (Job?.Name ?? "?") + " > " + (Operation?.Name ?? "?");
    }

    /// <summary>
    /// One tool in the part, and everything using it.
    /// </summary>
    /// <remarks>
    /// What the tool library browser shows beside the libraries: the tools copied into the
    /// open part, each with the operations cutting with it underneath. Computed here
    /// rather than assembled in the viewmodel, for the same reason
    /// <see cref="Tooling.ToolSearch"/> is in Core - it is a rule about the model, and a
    /// headless test can reach it.
    ///
    /// Lives in Model rather than Tooling because it reads <see cref="JobDocument"/>, and
    /// Model already depends on Tooling. Putting it the other way round would make the two
    /// namespaces depend on each other.
    /// </remarks>
    public sealed class ToolUsage
    {
        private static readonly ToolUse[] NoUses = new ToolUse[0];

        public ToolUsage(Tool tool, IReadOnlyList<ToolUse> uses)
        {
            Tool = tool;
            Uses = uses ?? NoUses;
        }

        public Tool Tool { get; }

        public IReadOnlyList<ToolUse> Uses { get; }

        /// <summary>
        /// True when nothing cuts with it. Shown as such rather than removed - see
        /// <see cref="JobDocument.RemoveTool"/>.
        /// </summary>
        public bool IsUnused => Uses.Count == 0;

        /// <summary>
        /// Every tool in the part with its operations, ordered by tool number.
        /// </summary>
        /// <remarks>
        /// Tool number order because that is the order someone loading the carousel works
        /// in. Unnumbered tools sort last rather than first, where a zero would put them.
        /// </remarks>
        public static IReadOnlyList<ToolUsage> ForDocument(JobDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            return document.Tools
                .Select(t => new ToolUsage(t, UsesOf(document, t.Id)))
                .OrderBy(u => u.Tool.Number > 0 ? u.Tool.Number : int.MaxValue)
                .ThenBy(u => u.Tool.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        /// <summary>Every operation in the part cutting with one tool.</summary>
        public static IReadOnlyList<ToolUse> UsesOf(JobDocument document, string toolId)
        {
            if (document == null || string.IsNullOrEmpty(toolId))
            {
                return NoUses;
            }

            return EveryOperation(document)
                .Where(u => string.Equals(u.Operation.ToolId, toolId, StringComparison.Ordinal))
                .ToList();
        }

        /// <summary>
        /// Operations pointing at a tool that is not in the part.
        /// </summary>
        /// <remarks>
        /// Different from an operation with no tool chosen yet, which is an ordinary
        /// unfinished state that <see cref="Operation.Validate"/> reports. This is a
        /// reference that has gone stale - a tool removed behind an operation's back, or a
        /// document written by something that had one we do not.
        /// </remarks>
        public static IReadOnlyList<ToolUse> WithMissingTools(JobDocument document)
        {
            if (document == null)
            {
                return NoUses;
            }

            return EveryOperation(document)
                .Where(u => !string.IsNullOrEmpty(u.Operation.ToolId)
                            && document.FindTool(u.Operation.ToolId) == null)
                .ToList();
        }

        /// <summary>"2 operations" or the single one by name, for a message.</summary>
        public static string Describe(IReadOnlyList<ToolUse> uses)
        {
            if (uses == null || uses.Count == 0)
            {
                return "nothing";
            }

            return uses.Count == 1
                ? "'" + uses[0] + "'"
                : uses.Count + " operations";
        }

        private static IEnumerable<ToolUse> EveryOperation(JobDocument document)
        {
            return document.Jobs.SelectMany(
                job => (job.Operations ?? new List<Operation>())
                    .Where(o => o != null)
                    .Select(o => new ToolUse(job, o)));
        }

        public override string ToString() =>
            (Tool?.DisplayName ?? "?") + " (" + Uses.Count + ")";
    }
}
