using System.Collections.Generic;
using System.Linq;
using GCam.Core.Model;
using GCam.Core.Model.Heights;
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
    public sealed class Contour2dSettings : StrategySettings, IContourSelectionOwner
    {
        /// <summary>
        /// Parameter names as they appear in files. Changing one silently drops whatever
        /// was stored under the old name, so they are spelled out rather than derived from
        /// the property names.
        /// </summary>
        private static class Names
        {
            public const string Direction = "direction";
            public const string StockToLeaveEnabled = "doStockToLeave";
            public const string StockToLeave = "stockToLeave";
            public const string VerticalStockToLeave = "verticalStockToLeave";
            public const string MultipleDepths = "doMultipleDepths";
            public const string MaximumStepdown = "maximumStepdown";
            public const string EvenStepdowns = "useEvenStepdowns";
            public const string TangentialExtension = "tangentialExtensionDistance";
            public const string LeadOutMatchesLeadIn = "exitSameAsEntry";
            public const string LeadInPrefix = "entry_";
            public const string LeadOutPrefix = "exit_";
        }

        public override StrategyId Strategy => StrategyId.Contour2d;

        /// <summary>
        /// The edges or faces whose boundary the cutter follows, each with the modifiers
        /// it was picked with.
        /// </summary>
        /// <remarks>
        /// Typed as part of the strategy rather than on the base operation, because there
        /// is no meaningful selection every strategy shares - drilling takes cylindrical
        /// faces, facing takes an optional boundary. See docs/design/operations.md.
        ///
        /// <see cref="ContourSelection"/> rather than a bare <see cref="GeometryRef"/>
        /// because tangent propagation is stored as intent and re-evaluated, not baked
        /// into a list of edges at pick time.
        /// </remarks>
        public List<ContourSelection> Contours { get; set; } = new List<ContourSelection>();

        public CutDirection Direction { get; set; } = CutDirection.Climb;

        /// <summary>
        /// Whether stock to leave is applied at all. Off, both amounts are ignored rather
        /// than zeroed, so turning it back on restores what was typed.
        /// </summary>
        /// <remarks>
        /// Defaults on, and old files that predate the parameter read as on: their stored
        /// amounts were being applied, and loading a part must not quietly change what it
        /// cuts. An operation that wants none of it leaves the amounts at zero, which is
        /// where they start.
        /// </remarks>
        public bool StockToLeaveEnabled { get; set; } = true;

        /// <summary>Material left on the wall for a later pass, mm.</summary>
        public double StockToLeave { get; set; }

        /// <summary>Material left on the floor, mm.</summary>
        public double VerticalStockToLeave { get; set; }

        /// <summary>
        /// The wall and floor amounts actually cut to, which are zero when stock to leave
        /// is off. Read these, not the raw properties.
        /// </summary>
        public double EffectiveStockToLeave => StockToLeaveEnabled ? StockToLeave : 0;

        public double EffectiveVerticalStockToLeave =>
            StockToLeaveEnabled ? VerticalStockToLeave : 0;

        /// <summary>
        /// How far each open contour is run on past its own ends before the cutter is
        /// offset from it, mm. Negative shortens it.
        /// </summary>
        /// <remarks>
        /// One distance for both ends. HSMWorks carries a second,
        /// `tangentialExtensionDistanceEnd`, whose default is an expression reading this
        /// one - so a single box is its default state and the pair is what asymmetry
        /// needs. Closed contours ignore it; see
        /// <see cref="GCam.Core.Geometry.TangentialExtension"/>, which also says why the
        /// Passes tab's fragment extension is a different parameter.
        /// </remarks>
        public double TangentialExtensionDistance { get; set; }

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
                StockToLeaveEnabled = StockToLeaveEnabled,
                StockToLeave = StockToLeave,
                VerticalStockToLeave = VerticalStockToLeave,
                TangentialExtensionDistance = TangentialExtensionDistance,
                MultipleDepths = MultipleDepths.Clone(),
                LeadIn = LeadIn.Clone(),
                LeadOut = LeadOut.Clone(),
                LeadOutMatchesLeadIn = LeadOutMatchesLeadIn,
            };
        }

        public override void WriteParameters(ParameterBag bag)
        {
            bag.SetEnum(Names.Direction, Direction);
            bag.Set(Names.StockToLeaveEnabled, StockToLeaveEnabled);
            bag.Set(Names.StockToLeave, StockToLeave);
            bag.Set(Names.VerticalStockToLeave, VerticalStockToLeave);
            bag.Set(Names.TangentialExtension, TangentialExtensionDistance);

            bag.Set(Names.MultipleDepths, MultipleDepths.Enabled);
            bag.Set(Names.MaximumStepdown, MultipleDepths.MaximumStepdown);
            bag.Set(Names.EvenStepdowns, MultipleDepths.UseEvenStepdowns);

            bag.Set(Names.LeadOutMatchesLeadIn, LeadOutMatchesLeadIn);
            WriteLead(bag, Names.LeadInPrefix, LeadIn);
            WriteLead(bag, Names.LeadOutPrefix, LeadOut);
        }

        public override void ReadParameters(ParameterBag bag)
        {
            Direction = bag.GetEnum(Names.Direction, Direction);
            StockToLeaveEnabled = bag.GetBool(Names.StockToLeaveEnabled, StockToLeaveEnabled);
            StockToLeave = bag.GetDouble(Names.StockToLeave, StockToLeave);
            VerticalStockToLeave = bag.GetDouble(Names.VerticalStockToLeave, VerticalStockToLeave);
            TangentialExtensionDistance =
                bag.GetDouble(Names.TangentialExtension, TangentialExtensionDistance);

            MultipleDepths.Enabled = bag.GetBool(Names.MultipleDepths, MultipleDepths.Enabled);
            MultipleDepths.MaximumStepdown =
                bag.GetDouble(Names.MaximumStepdown, MultipleDepths.MaximumStepdown);
            MultipleDepths.UseEvenStepdowns =
                bag.GetBool(Names.EvenStepdowns, MultipleDepths.UseEvenStepdowns);

            LeadOutMatchesLeadIn = bag.GetBool(Names.LeadOutMatchesLeadIn, LeadOutMatchesLeadIn);
            ReadLead(bag, Names.LeadInPrefix, LeadIn);
            ReadLead(bag, Names.LeadOutPrefix, LeadOut);
        }

        private static void WriteLead(ParameterBag bag, string prefix, LeadSettings lead)
        {
            bag.Set(prefix + "enabled", lead.Enabled);
            bag.Set(prefix + "radius", lead.Radius);
            bag.Set(prefix + "distance", lead.Distance);
            bag.Set(prefix + "sweep", lead.Sweep);
            bag.Set(prefix + "verticalRadius", lead.VerticalRadius);
            bag.Set(prefix + "perpendicular", lead.Perpendicular);
        }

        private static void ReadLead(ParameterBag bag, string prefix, LeadSettings lead)
        {
            lead.Enabled = bag.GetBool(prefix + "enabled", lead.Enabled);
            lead.Radius = bag.GetDouble(prefix + "radius", lead.Radius);
            lead.Distance = bag.GetDouble(prefix + "distance", lead.Distance);
            lead.Sweep = bag.GetDouble(prefix + "sweep", lead.Sweep);
            lead.VerticalRadius = bag.GetDouble(prefix + "verticalRadius", lead.VerticalRadius);
            lead.Perpendicular = bag.GetBool(prefix + "perpendicular", lead.Perpendicular);
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

            // Stock to leave is deliberately unbounded in sign. Negative cuts past the
            // profile rather than short of it, which is how a cutter running undersize is
            // taken out - and radial stock more negative than the cutter's radius carries
            // it across to the other side of the contour, which is occasionally what is
            // wanted and never something to guess at on the user's behalf.
            problems.AddRange(MultipleDepths.Validate());
            problems.AddRange(LeadIn.Validate("Lead-in"));

            if (!LeadOutMatchesLeadIn)
            {
                problems.AddRange(LeadOut.Validate("Lead-out"));
            }

            return problems;
        }

        /// <summary>
        /// Cutting down to the contour itself rather than to the bottom of the model.
        /// </summary>
        /// <remarks>
        /// The edge picked is the edge to be cut, so the profile's own Z is where the cut
        /// should stop - and on a part with several levels, each chain stops at its own.
        /// The model bottom took every profile straight through the part.
        /// </remarks>
        public override OperationHeights DefaultHeights()
        {
            OperationHeights heights = base.DefaultHeights();
            heights.Bottom = new HeightSetting(HeightMode.FromContour);
            return heights;
        }
    }
}
