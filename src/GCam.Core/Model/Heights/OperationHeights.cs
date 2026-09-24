using System.Collections.Generic;

namespace GCam.Core.Model.Heights
{
    /// <summary>
    /// The five heights every operation has, and the rule that they have to be in order.
    /// </summary>
    /// <remarks>
    /// Clearance at the top, bottom at the bottom:
    ///
    ///     clearance  >=  retract  >=  feed  >=  top  >  bottom
    ///
    /// The rule is checked against *resolved* values rather than modes, because two
    /// different modes can land on the same plane and only the numbers say whether they
    /// did. A height pair the wrong way round is how a tool gets driven through a clamp.
    /// </remarks>
    public sealed class OperationHeights
    {
        /// <summary>
        /// Defaults are sensible starting values, not HSMWorks' exact numbers: 10mm of
        /// clearance over the stock, retract at 5, feed at 2, cutting from the top of the
        /// stock to the bottom of the model.
        /// </summary>
        public HeightSetting Clearance { get; set; } = new HeightSetting(HeightMode.FromStockTop, 10);

        public HeightSetting Retract { get; set; } = new HeightSetting(HeightMode.FromStockTop, 5);

        public HeightSetting Feed { get; set; } = new HeightSetting(HeightMode.FromStockTop, 2);

        public HeightSetting Top { get; set; } = new HeightSetting(HeightMode.FromStockTop);

        public HeightSetting Bottom { get; set; } = new HeightSetting(HeightMode.FromModelBottom);

        /// <summary>
        /// True when the cutting heights move with each contour, so they have to be
        /// resolved once per contour rather than once for the operation.
        /// </summary>
        public bool IsContourRelative => Top.IsContourRelative || Bottom.IsContourRelative;

        /// <summary>
        /// Resolves all five. False when any of them cannot be resolved, which is a
        /// selection that has gone missing.
        /// </summary>
        /// <remarks>
        /// Says nothing about whether the result is in order - that is
        /// <see cref="Validate"/>'s job. Resolution answers "what Z is this", validation
        /// answers "is this usable", and a strategy needs both answers separately.
        /// </remarks>
        public bool TryResolve(HeightContext context, out ResolvedHeights resolved)
        {
            resolved = null;

            if (!Clearance.TryResolve(context, out double clearance)
                || !Retract.TryResolve(context, out double retract)
                || !Feed.TryResolve(context, out double feed)
                || !Top.TryResolve(context, out double top)
                || !Bottom.TryResolve(context, out double bottom))
            {
                return false;
            }

            resolved = new ResolvedHeights(clearance, retract, feed, top, bottom);
            return true;
        }

        /// <summary>
        /// Problems a user can act on. Empty when the heights are usable.
        /// </summary>
        public IReadOnlyList<string> Validate(HeightContext context)
        {
            var problems = new List<string>();

            // Only the cutting heights may follow a contour. The other three are crossed
            // on the way from one contour to the next, so they have to be one plane for
            // all of them; the page never offers it, and a file that says so is refused.
            RequireNotContourRelative(problems, Clearance, "Clearance height");
            RequireNotContourRelative(problems, Retract, "Retract height");
            RequireNotContourRelative(problems, Feed, "Feed height");

            if (problems.Count > 0)
            {
                return problems;
            }

            CheckResolves(problems, Clearance, "Clearance height", context);
            CheckResolves(problems, Retract, "Retract height", context);
            CheckResolves(problems, Feed, "Feed height", context);
            CheckResolves(problems, Top, "Top height", context);
            CheckResolves(problems, Bottom, "Bottom height", context);

            if (problems.Count > 0 || !TryResolve(context, out ResolvedHeights z))
            {
                // Ordering cannot be judged on numbers that do not exist.
                return problems;
            }

            RequireAtOrAbove(problems, z.Clearance, "Clearance height", z.Retract, "retract height");
            RequireAtOrAbove(problems, z.Retract, "Retract height", z.Feed, "feed height");
            RequireAtOrAbove(problems, z.Feed, "Feed height", z.Top, "top height");

            if (z.DepthOfCut <= Precision.Epsilon)
            {
                problems.Add(
                    $"Top height ({Format(z.Top)}) must be above bottom height " +
                    $"({Format(z.Bottom)}); there is nothing to cut.");
            }

            return problems;
        }

        public OperationHeights Clone()
        {
            return new OperationHeights
            {
                Clearance = Clearance.Clone(),
                Retract = Retract.Clone(),
                Feed = Feed.Clone(),
                Top = Top.Clone(),
                Bottom = Bottom.Clone(),
            };
        }

        private static void RequireNotContourRelative(
            ICollection<string> problems, HeightSetting height, string label)
        {
            if (height.IsContourRelative)
            {
                problems.Add(
                    $"{label} cannot be measured from the contour; only the top and bottom heights can.");
            }
        }

        private static void CheckResolves(
            ICollection<string> problems, HeightSetting height, string label, HeightContext context)
        {
            string failure = height.DescribeFailure(context);

            if (failure != null)
            {
                problems.Add($"{label} cannot be worked out: {failure}.");
            }
        }

        private static void RequireAtOrAbove(
            ICollection<string> problems, double upper, string upperLabel, double lower, string lowerLabel)
        {
            // Equal is allowed. Retracting to exactly the feed height is a legitimate way
            // to keep a tool down between passes.
            if (upper < lower - Precision.Epsilon)
            {
                problems.Add(
                    $"{upperLabel} ({Format(upper)}) is below the {lowerLabel} ({Format(lower)}).");
            }
        }

        private static string Format(double millimetres) => millimetres.ToString("0.###") + "mm";
    }
}
