namespace GCam.Core.Model
{
    /// <summary>
    /// What a move is for. Decides how it is drawn, how it is posted, and what it costs.
    /// </summary>
    /// <remarks>
    /// The distinction is worth carrying all the way through to the screen: the mistakes
    /// that show up by eye are a rapid through the stock and a bad lead-in, and neither is
    /// visible when a whole operation is drawn in one colour.
    ///
    /// Explicit numbers: written into the document.
    /// </remarks>
    public enum MoveKind
    {
        /// <summary>Not cutting. G0, at whatever the machine does.</summary>
        Rapid = 0,

        /// <summary>Easing into or out of a cut, so the entry mark is off the wall.</summary>
        Lead = 1,

        /// <summary>Getting from the end of one pass to the start of the next.</summary>
        Link = 2,

        /// <summary>Removing material. What the operation exists to do.</summary>
        Cutting = 3,

        /// <summary>Straight down into material, at the plunge feed.</summary>
        Plunge = 4,

        /// <summary>Withdrawing from a cut, usually faster than cutting.</summary>
        Retract = 5,

        /// <summary>
        /// A canned cycle - G81, G83 - kept whole rather than expanded into moves.
        /// </summary>
        /// <remarks>
        /// Reserved. Nothing produces one until drilling is built, and the cycle's own
        /// parameters land with it: inventing peck and dwell semantics before the strategy
        /// that uses them would be guessing. Reserved now so the stored numbering does not
        /// have to shift later.
        /// </remarks>
        Cycle = 6,
    }
}
