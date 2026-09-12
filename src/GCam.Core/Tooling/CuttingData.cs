namespace GCam.Core.Tooling
{
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

        /// <summary>Feed rate for cutting moves, mm/min.</summary>
        public double CuttingFeed { get; set; }

        /// <summary>
        /// Feed rate for straight-down entry, mm/min. Usually a fraction of the cutting
        /// feed, since a cutter's centre has no cutting speed.
        /// </summary>
        public double PlungeFeed { get; set; }

        /// <summary>Default radial engagement, mm.</summary>
        public double Stepover { get; set; }

        /// <summary>Default axial depth of cut, mm.</summary>
        public double Stepdown { get; set; }

        public CoolantMode Coolant { get; set; } = CoolantMode.Flood;

        /// <summary>
        /// Feed per tooth, derived. Zero when the inputs are missing rather than
        /// throwing - this is display arithmetic, not a validation gate.
        /// </summary>
        public double FeedPerTooth(int fluteCount)
        {
            if (fluteCount <= 0 || SpindleRpm <= 0)
            {
                return 0;
            }

            return CuttingFeed / (SpindleRpm * fluteCount);
        }

        public CuttingData Clone()
        {
            return (CuttingData)MemberwiseClone();
        }
    }
}
