using System.Collections.Generic;

namespace GCam.Core.Strategies.Shared
{
    /// <summary>
    /// How the cutter arrives at, or leaves, a cut.
    /// </summary>
    /// <remarks>
    /// Shared by contouring and adaptive clearing; absent from facing and drilling, which
    /// is exactly why it is composed rather than inherited. HSMWorks spells these
    /// <c>doLeadIn</c>, <c>entry_radius</c>, <c>entry_distance</c>, <c>entry_sweep</c> and
    /// <c>entry_verticalRadius</c>, with the same set again for the exit.
    ///
    /// Leading in matters because plunging straight down onto a finished wall leaves a
    /// mark where the cutter deflects and recovers.
    /// </remarks>
    public sealed class LeadSettings
    {
        public bool Enabled { get; set; } = true;

        /// <summary>Radius of the arc the cutter swings through, mm.</summary>
        public double Radius { get; set; } = 2.0;

        /// <summary>Straight run before the arc, mm.</summary>
        public double Distance { get; set; } = 2.0;

        /// <summary>How far round the arc goes, degrees.</summary>
        public double Sweep { get; set; } = 90.0;

        /// <summary>Radius of the vertical arc easing the tool down, mm. Zero for none.</summary>
        public double VerticalRadius { get; set; }

        /// <summary>Approach square to the wall rather than tangentially.</summary>
        public bool Perpendicular { get; set; }

        public IReadOnlyList<string> Validate(string label)
        {
            var problems = new List<string>();

            if (!Enabled)
            {
                return problems;
            }

            // Zero is legitimate for all of these - it means "no arc" or "no run-up", and
            // a lead with neither is a plain plunge, which is sometimes what is wanted.
            RequireNotNegative(problems, Radius, $"{label} radius");
            RequireNotNegative(problems, Distance, $"{label} distance");
            RequireNotNegative(problems, VerticalRadius, $"{label} vertical radius");

            if (Sweep < 0 || Sweep > 360)
            {
                problems.Add($"{label} sweep must be between 0 and 360 degrees.");
            }

            return problems;
        }

        public LeadSettings Clone() => (LeadSettings)MemberwiseClone();

        private static void RequireNotNegative(ICollection<string> problems, double value, string label)
        {
            if (value < 0)
            {
                problems.Add($"{label} cannot be negative.");
            }
        }
    }
}
