using System.Collections.Generic;
using System.Linq;
using GCam.Core.Model;
using GCam.Core.Strategies.Shared;

namespace GCam.Core.Strategies.Contour2d
{
    /// <summary>
    /// 2D contouring: follow selected edges at a depth, with a cutter offset to one side.
    /// </summary>
    /// <remarks>
    /// The first strategy built, and the one the whole base model was shaped against.
    ///
    /// **This is a working subset, not all of HSMWorks' 152 contour parameters.** What is
    /// here is what a 2D contour needs to cut: what to follow, which way round, how much
    /// to leave, how deep each pass goes, and how to get in and out. Chamfering, multiple
    /// finishing passes, rest machining, corner smoothing, tabs and cutter compensation
    /// are deliberately absent - each is a feature with its own behaviour to design, and
    /// adding a property here is cheap once one is wanted. Adding all 152 before anything
    /// can cut is not.
    /// </remarks>
    public sealed class Contour2dSettings : StrategySettings
    {
        public override StrategyId Strategy => StrategyId.Contour2d;

        /// <summary>
        /// The edges or faces whose boundary the cutter follows.
        /// </summary>
        /// <remarks>
        /// Typed as part of the strategy rather than on the base operation, because there
        /// is no meaningful selection every strategy shares - drilling takes cylindrical
        /// faces, facing takes an optional boundary. See docs/design/operations.md.
        /// </remarks>
        public List<GeometryRef> Contours { get; set; } = new List<GeometryRef>();

        public CutDirection Direction { get; set; } = CutDirection.Climb;

        /// <summary>Material left on the wall for a later pass, mm.</summary>
        public double StockToLeave { get; set; }

        /// <summary>Material left on the floor, mm.</summary>
        public double VerticalStockToLeave { get; set; }

        public MultipleDepthsSettings MultipleDepths { get; set; } = new MultipleDepthsSettings();

        public LeadSettings LeadIn { get; set; } = new LeadSettings();

        /// <summary>
        /// Read through <see cref="EffectiveLeadOut"/>, which accounts for
        /// <see cref="LeadOutMatchesLeadIn"/>.
        /// </summary>
        public LeadSettings LeadOut { get; set; } = new LeadSettings();

        /// <summary>
        /// Lead out the same way as in, which is what most contours want. HSMWorks calls
        /// this <c>exit_sameAsEntry</c>.
        /// </summary>
        public bool LeadOutMatchesLeadIn { get; set; } = true;

        /// <summary>
        /// The lead-out actually used. <see cref="LeadOut"/> keeps whatever was typed into
        /// it, so switching the checkbox on and back does not lose it.
        /// </summary>
        public LeadSettings EffectiveLeadOut => LeadOutMatchesLeadIn ? LeadIn : LeadOut;

        public override StrategySettings Clone()
        {
            return new Contour2dSettings
            {
                Contours = Contours.Select(c => c.Clone()).ToList(),
                Direction = Direction,
                StockToLeave = StockToLeave,
                VerticalStockToLeave = VerticalStockToLeave,
                MultipleDepths = MultipleDepths.Clone(),
                LeadIn = LeadIn.Clone(),
                LeadOut = LeadOut.Clone(),
                LeadOutMatchesLeadIn = LeadOutMatchesLeadIn,
            };
        }

        public override IReadOnlyList<string> Validate()
        {
            var problems = new List<string>();

            if (Contours == null || Contours.Count == 0)
            {
                problems.Add("Select at least one contour to follow.");
            }
            else if (Contours.Any(c => c == null || c.IsEmpty))
            {
                problems.Add("One of the selected contours is empty.");
            }

            if (StockToLeave < 0)
            {
                problems.Add("Stock to leave cannot be negative; use a smaller contour instead.");
            }

            if (VerticalStockToLeave < 0)
            {
                problems.Add("Vertical stock to leave cannot be negative.");
            }

            problems.AddRange(MultipleDepths.Validate());
            problems.AddRange(LeadIn.Validate("Lead-in"));

            if (!LeadOutMatchesLeadIn)
            {
                problems.AddRange(LeadOut.Validate("Lead-out"));
            }

            return problems;
        }
    }
}
