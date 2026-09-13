namespace GCam.Core.Strategies.Shared
{
    /// <summary>
    /// Which way round a cutter travels relative to its rotation.
    /// </summary>
    /// <remarks>
    /// Explicit numbers: written into the document.
    /// </remarks>
    public enum CutDirection
    {
        /// <summary>
        /// The cutter turns into the material, chip thick to thin. The default, and what
        /// carbide in a rigid machine wants.
        /// </summary>
        Climb = 0,

        /// <summary>
        /// Chip thin to thick. Kinder to a machine with backlash, and what a hard skin on
        /// a casting needs.
        /// </summary>
        Conventional = 1,
    }
}
