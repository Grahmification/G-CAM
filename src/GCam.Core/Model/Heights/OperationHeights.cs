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
        /// Defaults are sensible starting values, not HSMWorks' exact numbers: clearance 5
        /// above the retract, retract 5 over the stock, feed 2 above wherever cutting
        /// starts, cutting from the top of the stock to the bottom of the model. A strategy
        /// may start from different ones - see <c>StrategySettings.DefaultHeights</c>.
        /// </summary>
        /// <remarks>
        /// Measured from the retract by default, so raising the retract raises the
        /// clearance with it and the two cannot cross - on the default retract that is the
        /// same 10mm over the stock the clearance always was.
        /// </remarks>
        public HeightSetting Clearance { get; set; } = new HeightSetting(HeightMode.FromRetract, 5);

        public HeightSetting Retract { get; set; } = new HeightSetting(HeightMode.FromStockTop, 5);

        /// <summary>
        /// Measured from the top by default, so moving where cutting starts moves where
        /// the plunge slows down with it - on the stock top that is the same 2mm above it
        /// the feed height always was.
        /// </summary>
        public HeightSetting Feed { get; set; } = new HeightSetting(HeightMode.FromTop, 2);

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
            context = WithDatums(context);

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
        /// Resolves one of the five. False when it cannot be resolved - including a height
        /// measured from the top or the retract when that itself cannot be.
        /// </summary>
        /// <remarks>
        /// The way to resolve a single height. <see cref="HeightSetting.TryResolve"/> on its
        /// own cannot answer for <see cref="HeightMode.FromTop"/> or
        /// <see cref="HeightMode.FromRetract"/>, because only this knows which settings
        /// those are.
        /// </remarks>
        public bool TryResolve(HeightKind kind, HeightContext context, out double z)
        {
            z = 0;
            HeightSetting height = For(kind);

            return height != null && height.TryResolve(WithDatums(context), out z);
        }

        /// <summary>The setting for one of the five.</summary>
        public HeightSetting For(HeightKind kind)
        {
            switch (kind)
            {
                case HeightKind.Clearance: return Clearance;
                case HeightKind.Retract: return Retract;
                case HeightKind.Feed: return Feed;
                case HeightKind.Top: return Top;
                case HeightKind.Bottom: return Bottom;
                default: return null;
            }
        }

        /// <summary>
        /// Problems a user can act on. Empty when the heights are usable.
        /// </summary>
        public IReadOnlyList<string> Validate(HeightContext context)
        {
            var problems = new List<string>();

            // Only the cutting heights may be measured from the contour. Clearance and
            // retract are crossed on the way from one contour to the next, so they have to
            // be one plane for all of them. Feed may follow the contour, but only by way of
            // a top that does: that is what From Top is for. The page never offers the
            // rest, and a file that says so is refused.
            const string OnlyCutting = "the contour; only the top and bottom heights can";
            Refuse(problems, Clearance, "Clearance height", HeightMode.FromContour, OnlyCutting);
            Refuse(problems, Retract, "Retract height", HeightMode.FromContour, OnlyCutting);
            Refuse(problems, Feed, "Feed height", HeightMode.FromContour, OnlyCutting);

            // Each height measured from another is one step down the stack, and only the
            // steps that have been asked for are allowed: feed from the top, clearance from
            // the retract. A height measured from itself has no answer.
            const string OnlyFeed = "the top height; only the feed height can";
            Refuse(problems, Clearance, "Clearance height", HeightMode.FromTop, OnlyFeed);
            Refuse(problems, Retract, "Retract height", HeightMode.FromTop, OnlyFeed);
            Refuse(problems, Top, "Top height", HeightMode.FromTop, OnlyFeed);
            Refuse(problems, Bottom, "Bottom height", HeightMode.FromTop, OnlyFeed);

            const string OnlyClearance = "the retract height; only the clearance height can";
            Refuse(problems, Retract, "Retract height", HeightMode.FromRetract, OnlyClearance);
            Refuse(problems, Feed, "Feed height", HeightMode.FromRetract, OnlyClearance);
            Refuse(problems, Top, "Top height", HeightMode.FromRetract, OnlyClearance);
            Refuse(problems, Bottom, "Bottom height", HeightMode.FromRetract, OnlyClearance);

            if (problems.Count > 0)
            {
                return problems;
            }

            HeightContext datums = WithDatums(context);

            // A height measured from one that cannot be worked out fails for that one's
            // reason, which is reported on its own line. Saying it twice would send
            // someone to the wrong row to fix it.
            if (Clearance.Mode != HeightMode.FromRetract || datums.Retract.HasValue)
            {
                CheckResolves(problems, Clearance, "Clearance height", datums);
            }

            CheckResolves(problems, Retract, "Retract height", datums);

            if (Feed.Mode != HeightMode.FromTop || datums.Top.HasValue)
            {
                CheckResolves(problems, Feed, "Feed height", datums);
            }

            CheckResolves(problems, Top, "Top height", datums);
            CheckResolves(problems, Bottom, "Bottom height", datums);

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

        /// <summary>
        /// The context with the top and retract heights resolved into it, so a height
        /// measured from either can be. Each is left out when it cannot be resolved.
        /// </summary>
        /// <remarks>
        /// Resolved against the plain context, never against each other: the top and the
        /// retract may not be measured from another height, which is what keeps this one
        /// step deep and free of cycles. One that tries is refused by <see cref="Validate"/>
        /// and simply does not resolve here.
        /// </remarks>
        private HeightContext WithDatums(HeightContext context)
        {
            if (context == null)
            {
                return null;
            }

            HeightContext plain = context;

            if (IsDatum(Top) && Top.TryResolve(plain, out double top))
            {
                context = context.WithTop(top);
            }

            if (IsDatum(Retract) && Retract.TryResolve(plain, out double retract))
            {
                context = context.WithRetract(retract);
            }

            return context;
        }

        /// <summary>True for a height that others can be measured from: one not measured from another.</summary>
        private static bool IsDatum(HeightSetting height) =>
            height != null && height.Mode != HeightMode.FromTop && height.Mode != HeightMode.FromRetract;

        private static void Refuse(
            ICollection<string> problems, HeightSetting height, string label, HeightMode mode, string rule)
        {
            if (height.Mode == mode)
            {
                problems.Add($"{label} cannot be measured from {rule}.");
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
