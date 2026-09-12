using System;
using System.Collections.Generic;
using System.Linq;

namespace GCam.Core.Tooling
{
    /// <summary>
    /// A cutting tool: what it is, what shape it is, and how fast to run it.
    /// </summary>
    /// <remarks>
    /// Tools live in a library, but a job keeps its own <see cref="Clone"/> of the tool
    /// it uses, written into the SOLIDWORKS document. That makes a part self-contained:
    /// it opens and posts identically on a machine with a different library, or none.
    /// <see cref="SourceLibraryId"/> records where the copy came from so that
    /// re-linking to the library stays possible - as an explicit action, never silent
    /// drift underneath a job that has already been proven on a machine.
    /// </remarks>
    public sealed class Tool
    {
        /// <summary>
        /// Stable identity, unique within a library. Preserved by <see cref="Clone"/>,
        /// so an embedded copy can be matched back to its library entry.
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString("D");

        /// <summary>Tool number in the machine's carousel. Emitted as T&lt;n&gt; when posting.</summary>
        public int Number { get; set; }

        /// <summary>Human-readable description, e.g. "10mm 4-flute carbide bull nose".</summary>
        public string Name { get; set; }

        public ToolType Type { get; set; }

        public ToolGeometry Geometry { get; set; } = new ToolGeometry();

        public CuttingData Cutting { get; set; } = new CuttingData();

        /// <summary>
        /// The holder this tool is assembled into. Null when unassigned; nothing consumes
        /// it until holder collision checking exists.
        /// </summary>
        public Holder Holder { get; set; }

        /// <summary>
        /// Id of the library this tool was copied from, or null for a tool that still
        /// lives in a library. Set when embedding a copy into a job.
        /// </summary>
        public string SourceLibraryId { get; set; }

        /// <summary>Silhouette used by all geometry and simulation code.</summary>
        /// <exception cref="ArgumentException">The geometry is not valid for the type.</exception>
        public CutterProfile GetProfile(double chordTolerance = CutterProfile.DefaultChordTolerance)
        {
            return CutterProfile.For(Type, Geometry, chordTolerance);
        }

        /// <summary>
        /// Problems with this tool, in language suitable for showing the user.
        /// Empty when valid.
        /// </summary>
        public IReadOnlyList<string> Validate()
        {
            var problems = new List<string>();

            if (string.IsNullOrWhiteSpace(Id))
            {
                problems.Add("Tool id is missing.");
            }

            if (Number < 0)
            {
                problems.Add("Tool number cannot be negative.");
            }

            problems.AddRange(Geometry?.Validate(Type) ?? new[] { "Tool geometry is missing." });

            if (Holder != null)
            {
                problems.AddRange(Holder.Validate());
            }

            return problems;
        }

        /// <summary>
        /// A deep copy, for embedding into a job. Keeps <see cref="Id"/> and stamps
        /// <see cref="SourceLibraryId"/> so the copy can be traced back.
        /// </summary>
        public Tool CloneForJob(string sourceLibraryId)
        {
            Tool copy = Clone();
            copy.SourceLibraryId = sourceLibraryId;
            return copy;
        }

        public Tool Clone()
        {
            return new Tool
            {
                Id = Id,
                Number = Number,
                Name = Name,
                Type = Type,
                Geometry = Geometry?.Clone(),
                Cutting = Cutting?.Clone(),
                Holder = Holder?.Clone(),
                SourceLibraryId = SourceLibraryId,
            };
        }

        public override string ToString()
        {
            string label = string.IsNullOrWhiteSpace(Name)
                ? $"{Type} Ø{Geometry?.Diameter:0.###}"
                : Name;
            return $"T{Number} {label}";
        }
    }
}
