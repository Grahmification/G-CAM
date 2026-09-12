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

        /// <summary>Free-text note from whoever set the tool up.</summary>
        public string Comment { get; set; }

        /// <summary>Who made the tool.</summary>
        public string Manufacturer { get; set; }

        /// <summary>Manufacturer's catalogue number, for reordering.</summary>
        public string ProductId { get; set; }

        /// <summary>
        /// Cutter substrate - "carbide", "hss" and so on. Free text rather than an enum:
        /// source libraries use their own vocabularies and nothing consumes it yet.
        /// </summary>
        public string Material { get; set; }

        public ToolType Type { get; set; }

        public ToolGeometry Geometry { get; set; } = new ToolGeometry();

        public CuttingData Cutting { get; set; } = new CuttingData();

        /// <summary>How the machine control refers to this tool. Used by posting.</summary>
        public MachineData Machine { get; set; } = new MachineData();

        /// <summary>
        /// Fields from an imported library that G-CAM has no property for.
        /// </summary>
        /// <remarks>
        /// Preserved verbatim and written back out, so importing does not quietly
        /// destroy data. Keys are namespaced by source, e.g. "hsm.library-name".
        /// Promoting one to a real property later is a refactor with no data lost in
        /// the meantime.
        /// </remarks>
        public Dictionary<string, string> Extra { get; set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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

        /// <summary>
        /// How far the tool protrudes from the holder face, mm.
        /// </summary>
        /// <remarks>
        /// Drives where the holder sits in a preview, and eventually how deep a cut can
        /// go before the holder fouls the work.
        ///
        /// Taken from <see cref="ToolGeometry.BodyLength"/>. *Assumed*, from arithmetic
        /// on a real HSMWorks library: overall length minus body length gives 22.5, 14.6,
        /// 43.0 and 22.3mm across four tools - all plausible collet grips, which only
        /// works if body length is the exposed portion. Verify against a measured tool
        /// before trusting it for collision checking.
        ///
        /// Falls back to overall length, then flute length, and never reports less than
        /// the cutting length - a holder overlapping the flutes would be nonsense.
        /// </remarks>
        public double Stickout
        {
            get
            {
                ToolGeometry g = Geometry ?? new ToolGeometry();

                double exposed = g.BodyLength > 0 ? g.BodyLength
                    : g.OverallLength > 0 ? g.OverallLength
                    : g.FluteLength;

                return Math.Max(exposed, Math.Max(g.FluteLength, g.ShoulderLength));
            }
        }

        /// <summary>
        /// Short physical description of the tool - "Ø12.7 flat end mill",
        /// "Ø10 R2 bull nose end mill", "Ø2.5 118° drill".
        /// </summary>
        /// <remarks>
        /// Derived, never stored. <see cref="Name"/> is whatever the library author
        /// typed - often something like "Aluminum", which says what the tool is FOR but
        /// nothing about what it IS. This is the label you can scan a list by.
        ///
        /// Only the numbers that distinguish one tool from another of the same type
        /// appear: corner radius for a bull nose, point angle for anything conical,
        /// thread pitch for a tap. Adding the rest would make every row look alike.
        /// </remarks>
        public string DisplayName
        {
            get
            {
                ToolGeometry g = Geometry ?? new ToolGeometry();
                var parts = new List<string> { "Ø" + Format(g.Diameter) };

                switch (Type)
                {
                    case ToolType.BullNoseEndMill:
                        parts.Add("R" + Format(g.CornerRadius));
                        break;

                    case ToolType.Drill:
                    case ToolType.SpotDrill:
                    case ToolType.ChamferMill:
                        if (g.TipAngle > 0)
                        {
                            parts.Add(Format(g.TipAngle) + "°");
                        }

                        break;

                    case ToolType.Tap:
                        if (g.ThreadPitch > 0)
                        {
                            parts.Add("× " + Format(g.ThreadPitch));
                        }

                        break;
                }

                parts.Add(ToolSearch.DisplayName(Type).ToLowerInvariant());
                return string.Join(" ", parts);
            }
        }

        /// <summary>
        /// The plain shank above the cutting body, as a silhouette measured from the
        /// tool tip. Empty when none is exposed below the holder.
        /// </summary>
        /// <remarks>
        /// Runs from the top of the body - the greater of flute and shoulder length -
        /// up to <see cref="Stickout"/>, at the shank diameter. Falls back to the
        /// cutting diameter when no shank diameter is recorded, which is right for the
        /// common case of a plain straight-shank end mill.
        ///
        /// Kept separate from the cutter profile rather than appended to it because the
        /// shank does not cut: it matters for reach and collisions, never for material
        /// removal, and merging the two would quietly widen the cutter in a Z-map.
        /// </remarks>
        public IReadOnlyList<ProfilePoint> GetShankProfile()
        {
            ToolGeometry g = Geometry ?? new ToolGeometry();

            double bodyTop = Math.Max(g.FluteLength, g.ShoulderLength);
            double top = Stickout;

            if (top <= bodyTop + Precision.Epsilon)
            {
                return new List<ProfilePoint>();
            }

            double radius = (g.ShankDiameter > 0 ? g.ShankDiameter : g.Diameter) / 2.0;
            if (radius <= 0)
            {
                return new List<ProfilePoint>();
            }

            return new List<ProfilePoint>
            {
                new ProfilePoint(bodyTop, radius),
                new ProfilePoint(top, radius),
            };
        }

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

        /// <summary>
        /// A copy with a fresh identity, for duplicating a tool within a library.
        /// </summary>
        /// <remarks>
        /// A new Id matters: two tools sharing one would break re-linking an embedded
        /// job copy back to its library entry, and ToolLibrary.Validate reports it.
        /// </remarks>
        public Tool CloneAsNew()
        {
            Tool copy = Clone();
            copy.Id = Guid.NewGuid().ToString("D");
            copy.SourceLibraryId = null;
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
                Comment = Comment,
                Manufacturer = Manufacturer,
                ProductId = ProductId,
                Material = Material,
                Geometry = Geometry?.Clone(),
                Cutting = Cutting?.Clone(),
                Machine = Machine?.Clone(),
                Holder = Holder?.Clone(),
                SourceLibraryId = SourceLibraryId,
                Extra = new Dictionary<string, string>(Extra, StringComparer.OrdinalIgnoreCase),
            };
        }

        public override string ToString()
        {
            return string.IsNullOrWhiteSpace(Name)
                ? $"T{Number} {DisplayName}"
                : $"T{Number} {Name}";
        }

        private static string Format(double value)
        {
            return value.ToString("0.###", System.Globalization.CultureInfo.CurrentCulture);
        }
    }
}
