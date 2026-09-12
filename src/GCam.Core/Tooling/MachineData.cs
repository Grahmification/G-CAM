namespace GCam.Core.Tooling
{
    /// <summary>
    /// How the machine control refers to this tool.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="ToolGeometry"/> because none of it describes the cutter -
    /// it describes the machine's bookkeeping, and posting is the only consumer.
    ///
    /// The two offsets are the reason this exists. Controls keep tool geometry in
    /// numbered registers, and posted code refers to them rather than to dimensions:
    /// G43 H&lt;length offset&gt; applies tool length compensation, G41/G42 D&lt;diameter
    /// offset&gt; applies cutter compensation. They usually match the tool number, but
    /// not always, and guessing produces a crash rather than a wrong part.
    /// </remarks>
    public sealed class MachineData
    {
        /// <summary>Register holding the cutter diameter - the D word.</summary>
        public int DiameterOffset { get; set; }

        /// <summary>Register holding the tool length - the H word.</summary>
        public int LengthOffset { get; set; }

        /// <summary>Turret index on a machine that has more than one. Zero for a mill.</summary>
        public int Turret { get; set; }

        /// <summary>Whether the machine should run a tool break check after use.</summary>
        public bool BreakControl { get; set; }

        /// <summary>
        /// Whether the operator changes this tool by hand. True on machines without an
        /// automatic changer; posting emits a stop and a prompt.
        /// </summary>
        public bool ManualToolChange { get; set; }

        public MachineData Clone()
        {
            return (MachineData)MemberwiseClone();
        }
    }
}
