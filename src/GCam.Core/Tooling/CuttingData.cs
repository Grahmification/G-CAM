namespace GCam.Core.Tooling
{
    /// <summary>How feed rates are interpreted.</summary>
    public enum FeedMode
    {
        /// <summary>Distance per minute (G94). The normal milling mode.</summary>
        PerMinute = 0,

        /// <summary>
        /// Distance per spindle revolution (G95). What tapping needs, because the feed
        /// must track the spindle exactly or the thread is destroyed.
        /// </summary>
        PerRevolution = 1,
    }

    /// <summary>Coolant mode requested for an operation.</summary>
    public enum CoolantMode
    {
        Off = 0,
        Flood = 1,
        Mist = 2,
        ThroughTool = 3,
    }

    /// <summary>
    /// Default speeds and feeds for a tool. Feeds are mm/min, stepover and stepdown mm.
    /// </summary>
    /// <remarks>
    /// One set per tool in v1. Real shops vary these by stock material, which is why
    /// this is a separate object rather than fields on <see cref="Tool"/> - a future
    /// per-material preset list slots in without reshaping the tool.
    ///
    /// Operations copy these as their starting values and may override them; the tool
    /// supplies defaults, it does not dictate.
    /// </remarks>
    public sealed class CuttingData
    {
        /// <summary>Spindle speed in revolutions per minute.</summary>
        public double SpindleRpm { get; set; }

        /// <summary>
        /// Spindle speed while ramping into material, rpm. Often the same as
        /// <see cref="SpindleRpm"/>.
        /// </summary>
        public double RampSpindleRpm { get; set; }

        /// <summary>
        /// True for M3 (clockwise seen from above), false for M4. Wrong direction means
        /// the tool rubs instead of cutting, so this is posted explicitly rather than
        /// assumed.
        /// </summary>
        public bool SpindleClockwise { get; set; } = true;

        /// <summary>Whether feeds are per minute or per spindle revolution.</summary>
        public FeedMode FeedMode { get; set; } = FeedMode.PerMinute;

        /// <summary>Feed rate for cutting moves, mm/min.</summary>
        public double CuttingFeed { get; set; }

        /// <summary>
        /// Feed rate for straight-down entry, mm/min. Usually a fraction of the cutting
        /// feed, since a cutter's centre has no cutting speed.
        /// </summary>
        public double PlungeFeed { get; set; }

        /// <summary>Feed rate while leading into a cut, mm/min.</summary>
        public double EntryFeed { get; set; }

        /// <summary>Feed rate while leading out of a cut, mm/min.</summary>
        public double ExitFeed { get; set; }

        /// <summary>Feed rate while ramping or helixing down into material, mm/min.</summary>
        public double RampFeed { get; set; }

        /// <summary>
        /// Feed rate while withdrawing from a cut, mm/min. Often much faster than the
        /// cutting feed, which is why it is worth storing rather than deriving.
        /// </summary>
        public double RetractFeed { get; set; }

        /// <summary>
        /// Default radial engagement, mm.
        /// </summary>
        /// <remarks>
        /// G-CAM keeps stepover and stepdown on the tool as starting values. HSM stores
        /// them per operation instead, so they arrive as zero from an HSM import and
        /// have to be filled in.
        /// </remarks>
        public double Stepover { get; set; }

        /// <summary>Default axial depth of cut, mm.</summary>
        public double Stepdown { get; set; }

        public CoolantMode Coolant { get; set; } = CoolantMode.Flood;

        /// <summary>
        /// Feed per tooth, derived. Zero when the inputs are missing rather than
        /// throwing - this is display arithmetic, not a validation gate.
        /// </summary>
        /// <remarks>
        /// Derived, never stored: the flute count lives on the tool, so a chip load kept
        /// here would go stale the moment that changed. <see cref="FeedsAndSpeeds"/> owns
        /// the arithmetic and the inverse, so the page can offer both ends.
        /// </remarks>
        public double FeedPerTooth(int fluteCount) =>
            FeedsAndSpeeds.FeedPerTooth(CuttingFeed, SpindleRpm, fluteCount);

        public CuttingData Clone()
        {
            return (CuttingData)MemberwiseClone();
        }
    }
}
