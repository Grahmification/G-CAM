using System.Collections.Generic;

namespace GCam.Core.Strategies
{
    /// <summary>
    /// The strategy-specific half of an operation: the four fifths of the parameters that
    /// mean nothing outside one strategy.
    /// </summary>
    /// <remarks>
    /// Measured across the four strategies in the HSMWorks export, there are 231 distinct
    /// parameters and only 47 common to all four. The base operation carries the 47; a
    /// subclass of this carries the rest - see
    /// docs/decisions/0007-typed-strategy-settings.md for why these are typed classes
    /// rather than a named parameter bag.
    ///
    /// Groups that several strategies share but not all - linking, multiple depths,
    /// lead-in/out - are *composed* as properties rather than inherited here. Drilling has
    /// no lead-in, and a base that handed it one would be lying about what a drill
    /// operation is.
    /// </remarks>
    public abstract class StrategySettings
    {
        private static readonly string[] NoProblems = new string[0];

        /// <summary>Which strategy these settings belong to. Fixed by the subclass.</summary>
        public abstract StrategyId Strategy { get; }

        /// <summary>
        /// True when this strategy machines what earlier operations left behind, so that
        /// editing an operation above it invalidates this one too.
        /// </summary>
        public virtual bool DependsOnPrecedingStock => false;

        /// <summary>Deep copy. Nothing may be shared with the original.</summary>
        public abstract StrategySettings Clone();

        /// <summary>
        /// Problems with these settings alone. Empty when they are usable.
        /// </summary>
        /// <remarks>
        /// Self-consistency only - a stepdown that is zero while multiple depths are
        /// switched on, a negative lead radius, an empty selection. Rules that span
        /// objects, like a stepdown deeper than the tool's flute length or deeper than
        /// the cut, need the tool and the resolved heights; neither is reachable from
        /// here, and both are checked when an operation is generated.
        /// </remarks>
        public virtual IReadOnlyList<string> Validate() => NoProblems;
    }
}
