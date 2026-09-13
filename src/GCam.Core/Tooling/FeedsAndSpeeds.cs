using System;

namespace GCam.Core.Tooling
{
    /// <summary>
    /// Conversions between the numbers a machinist types and the numbers a machine is
    /// given. Every one of them works in both directions.
    /// </summary>
    /// <remarks>
    /// A handbook gives surface speed and chip load; a control wants rpm and mm/min.
    /// Which end someone enters depends on what they are reading, so both ends are
    /// editable and the partner value follows.
    ///
    /// **Only the machine-side value is ever stored** - spindle rpm and feed in mm/min,
    /// on <see cref="CuttingData"/>. Surface speed and chip load are recomputed for
    /// display every time they are shown. Storing the derived half instead would leave a
    /// saved surface speed describing a cutter diameter that has since been edited.
    /// HSMWorks gets the same property by storing these as expressions; G-CAM gets it by
    /// not storing them at all.
    ///
    /// Everything here returns zero when an input is missing rather than throwing. This
    /// is display arithmetic, filling boxes on a page while someone types - a half-filled
    /// form is a normal state, not an error. Validation gates live on the model.
    ///
    /// Units: diameters and chip loads in millimetres, feeds in mm/min, surface speed in
    /// **metres per minute**, which is what the metric handbooks and HSMWorks both use.
    /// </remarks>
    public static class FeedsAndSpeeds
    {
        /// <summary>
        /// Surface speed at the cutter's edge, m/min. <c>v = pi * d * n</c>.
        /// </summary>
        public static double SurfaceSpeed(double diameterMillimetres, double spindleRpm)
        {
            if (diameterMillimetres <= 0 || spindleRpm <= 0)
            {
                return 0;
            }

            return Math.PI * diameterMillimetres * spindleRpm / Units.MillimetresPerMetre;
        }

        /// <summary>
        /// The spindle speed that produces a surface speed, rpm. The inverse of
        /// <see cref="SurfaceSpeed"/>.
        /// </summary>
        public static double SpindleRpm(double diameterMillimetres, double surfaceSpeedMetresPerMinute)
        {
            if (diameterMillimetres <= 0 || surfaceSpeedMetresPerMinute <= 0)
            {
                return 0;
            }

            return surfaceSpeedMetresPerMinute * Units.MillimetresPerMetre
                   / (Math.PI * diameterMillimetres);
        }

        /// <summary>
        /// Chip load - how much each tooth takes per revolution, mm.
        /// </summary>
        public static double FeedPerTooth(double feedMillimetresPerMinute, double spindleRpm, int teeth)
        {
            if (teeth <= 0 || spindleRpm <= 0)
            {
                return 0;
            }

            return feedMillimetresPerMinute / (spindleRpm * teeth);
        }

        /// <summary>
        /// The feed that produces a chip load, mm/min. The inverse of
        /// <see cref="FeedPerTooth"/>.
        /// </summary>
        public static double FeedFromFeedPerTooth(
            double feedPerToothMillimetres, double spindleRpm, int teeth)
        {
            if (teeth <= 0 || spindleRpm <= 0)
            {
                return 0;
            }

            return feedPerToothMillimetres * spindleRpm * teeth;
        }

        /// <summary>
        /// Feed per spindle revolution, mm. What G95 and tapping are expressed in.
        /// </summary>
        public static double FeedPerRevolution(double feedMillimetresPerMinute, double spindleRpm)
        {
            if (spindleRpm <= 0)
            {
                return 0;
            }

            return feedMillimetresPerMinute / spindleRpm;
        }

        /// <summary>
        /// The feed that produces a feed per revolution, mm/min. The inverse of
        /// <see cref="FeedPerRevolution"/>.
        /// </summary>
        public static double FeedFromFeedPerRevolution(
            double feedPerRevolutionMillimetres, double spindleRpm)
        {
            if (spindleRpm <= 0)
            {
                return 0;
            }

            return feedPerRevolutionMillimetres * spindleRpm;
        }
    }
}
