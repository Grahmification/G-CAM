using System;
using System.Collections.Generic;
using System.Linq;

namespace GCam.Core.Tooling
{
    /// <summary>
    /// One stage of a holder's profile, measured upward from the holder's lower face.
    /// </summary>
    /// <remarks>
    /// Equal upper and lower diameters give a cylinder; differing ones give a cone,
    /// which is how collet nuts and taper shanks are described.
    /// </remarks>
    public sealed class HolderSegment
    {
        public HolderSegment()
        {
        }

        public HolderSegment(double length, double lowerDiameter, double upperDiameter)
        {
            Length = length;
            LowerDiameter = lowerDiameter;
            UpperDiameter = upperDiameter;
        }

        /// <summary>Axial length of this stage, mm.</summary>
        public double Length { get; set; }

        /// <summary>Diameter at the bottom of this stage, mm.</summary>
        public double LowerDiameter { get; set; }

        /// <summary>Diameter at the top of this stage, mm.</summary>
        public double UpperDiameter { get; set; }

        public HolderSegment Clone()
        {
            return (HolderSegment)MemberwiseClone();
        }
    }

    /// <summary>
    /// A tool holder, described as a stack of cylindrical and conical stages.
    /// </summary>
    /// <remarks>
    /// Holders are shared: a shop has a handful of them and many tools, so they live
    /// as separate entries in the library and tools reference one by id.
    ///
    /// Nothing consumes this geometry yet. It is here because holder collision checking
    /// is the reason gauge length matters, and capturing the data now avoids re-editing
    /// every tool later.
    /// </remarks>
    public sealed class Holder
    {
        public string Id { get; set; }

        public string Name { get; set; }

        /// <summary>Free-text note.</summary>
        public string Comment { get; set; }

        /// <summary>Who supplies the holder.</summary>
        public string Vendor { get; set; }

        /// <summary>Supplier's catalogue number or product page.</summary>
        public string ProductId { get; set; }

        /// <summary>Fields from an imported library with no G-CAM property. See Tool.Extra.</summary>
        public Dictionary<string, string> Extra { get; set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Stages ordered from the holder's lower face upward.</summary>
        public List<HolderSegment> Segments { get; set; } = new List<HolderSegment>();

        /// <summary>Total height of the holder, mm.</summary>
        public double Height => Segments.Sum(s => s.Length);

        /// <summary>Largest diameter anywhere on the holder, mm.</summary>
        public double MaxDiameter =>
            Segments.Count == 0 ? 0 : Segments.Max(s => Math.Max(s.LowerDiameter, s.UpperDiameter));

        /// <summary>
        /// The holder's silhouette as a polyline, measured up from its lower face.
        /// </summary>
        /// <remarks>
        /// Same shape of data as <see cref="CutterProfile"/> so that anything drawing or
        /// colliding against tool geometry can treat a holder the same way. Steps are
        /// implicit: where one segment's upper diameter differs from the next segment's
        /// lower diameter, the profile jumps vertically, which is exactly how collet
        /// nuts and taper shanks are shaped.
        ///
        /// Unlike a cutter, a holder profile is NOT monotonic - it widens and narrows -
        /// so do not reuse CutterProfile's lookup logic on it.
        /// </remarks>
        public IReadOnlyList<ProfilePoint> GetProfile()
        {
            var points = new List<ProfilePoint>();
            double height = 0;

            foreach (HolderSegment segment in Segments)
            {
                points.Add(new ProfilePoint(height, segment.LowerDiameter / 2.0));
                height += segment.Length;
                points.Add(new ProfilePoint(height, segment.UpperDiameter / 2.0));
            }

            return points;
        }

        public IReadOnlyList<string> Validate()
        {
            var problems = new List<string>();

            if (Segments.Count == 0)
            {
                problems.Add("A holder must have at least one segment.");
            }

            for (int i = 0; i < Segments.Count; i++)
            {
                HolderSegment segment = Segments[i];
                if (segment.Length <= 0)
                {
                    problems.Add($"Holder segment {i + 1} must have a length greater than zero.");
                }

                if (segment.LowerDiameter < 0 || segment.UpperDiameter < 0)
                {
                    problems.Add($"Holder segment {i + 1} cannot have a negative diameter.");
                }
            }

            return problems;
        }

        public Holder Clone()
        {
            return new Holder
            {
                Id = Id,
                Name = Name,
                Comment = Comment,
                Vendor = Vendor,
                ProductId = ProductId,
                Segments = Segments.Select(s => s.Clone()).ToList(),
                Extra = new Dictionary<string, string>(Extra, StringComparer.OrdinalIgnoreCase),
            };
        }
    }
}
