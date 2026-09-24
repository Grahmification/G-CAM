using System.Collections.Generic;
using GCam.Core.Model.Heights;

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
        /// Writes every parameter worth keeping into the bag, under stable names.
        /// </summary>
        /// <remarks>
        /// Abstract rather than virtual on purpose. A strategy that forgets to save a
        /// parameter loses it silently on the next reopen, and the loss is invisible until
        /// someone notices a toolpath came out different - so this cannot be inherited by
        /// omission. The same reasoning that makes `PmpHandlerBase` wrap all 37 callbacks.
        ///
        /// Selections are not parameters and do not go in here - see
        /// <see cref="ParameterBag"/>.
        /// </remarks>
        public abstract void WriteParameters(ParameterBag bag);

        /// <summary>
        /// Reads parameters back, leaving anything absent at this object's default.
        /// </summary>
        /// <remarks>
        /// An absent parameter is the normal shape of a file written by an older build, so
        /// every read falls back rather than failing. That is what makes a new parameter a
        /// non-breaking addition.
        /// </remarks>
        public abstract void ReadParameters(ParameterBag bag);

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

        /// <summary>
        /// The heights a new operation of this strategy starts with.
        /// </summary>
        /// <remarks>
        /// The strategy's to decide, because where a cut sensibly stops depends on what
        /// the cut is: a contour usually stops at the contour, a face at the top of the
        /// model. A fresh object every call, since the operation goes on to edit it.
        /// </remarks>
        public virtual OperationHeights DefaultHeights() => new OperationHeights();
    }
}
