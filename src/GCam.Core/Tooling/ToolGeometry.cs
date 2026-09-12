using System;
using System.Collections.Generic;
using System.Globalization;

namespace GCam.Core.Tooling
{
    /// <summary>
    /// Cutter dimensions. All lengths are millimetres, all angles degrees.
    /// </summary>
    /// <remarks>
    /// Which properties matter depends on <see cref="ToolType"/> - see
    /// <see cref="Validate"/>, which is the authoritative statement of the rules.
    /// Unused properties are left at zero rather than made nullable, because the
    /// library format is flat XML and zero is the natural "not applicable".
    /// </remarks>
    public sealed class ToolGeometry
    {
        /// <summary>Cutting diameter at the widest point of the flutes.</summary>
        public double Diameter { get; set; }

        /// <summary>
        /// Corner radius. Zero for a flat end mill, half the diameter for a ball nose,
        /// anything between for a bull nose. Not used by conical tools.
        /// </summary>
        public double CornerRadius { get; set; }

        /// <summary>
        /// Included angle of the point, in degrees - 118 or 135 for a typical drill,
        /// 90 for a chamfer mill. Not used by end mills.
        /// </summary>
        public double TipAngle { get; set; }

        /// <summary>
        /// Diameter of the flat at the very tip of a chamfer mill. Zero for a true point.
        /// </summary>
        public double TipDiameter { get; set; }

        /// <summary>Length of cut - how deep the flutes reach.</summary>
        public double FluteLength { get; set; }

        /// <summary>Number of flutes. Used for feed-per-tooth arithmetic.</summary>
        public int FluteCount { get; set; }

        /// <summary>Shank diameter. Informational until holder collision checking exists.</summary>
        public double ShankDiameter { get; set; }

        /// <summary>Overall tool length, tip to the end of the shank.</summary>
        public double OverallLength { get; set; }

        /// <summary>
        /// Checks the parameters against the rules for <paramref name="type"/>.
        /// </summary>
        /// <returns>
        /// Problems in language suitable for showing the user. Empty when valid.
        /// </returns>
        public IReadOnlyList<string> Validate(ToolType type)
        {
            var problems = new List<string>();

            if (Diameter <= 0)
            {
                problems.Add("Diameter must be greater than zero.");
            }

            if (FluteLength <= 0)
            {
                problems.Add("Flute length must be greater than zero.");
            }

            if (FluteCount < 0)
            {
                problems.Add("Flute count cannot be negative.");
            }

            double radius = Diameter / 2.0;

            switch (type)
            {
                case ToolType.FlatEndMill:
                    if (Math.Abs(CornerRadius) > Tolerance)
                    {
                        problems.Add("A flat end mill must have a corner radius of zero.");
                    }

                    break;

                case ToolType.BallEndMill:
                    if (Diameter > 0 && Math.Abs(CornerRadius - radius) > Tolerance)
                    {
                        problems.Add(Format(
                            "A ball end mill must have a corner radius of half its diameter ({0}).", radius));
                    }

                    break;

                case ToolType.BullNoseEndMill:
                    if (CornerRadius <= 0)
                    {
                        problems.Add("A bull nose end mill must have a corner radius greater than zero.");
                    }
                    else if (Diameter > 0 && CornerRadius > radius + Tolerance)
                    {
                        problems.Add(Format(
                            "Corner radius cannot exceed half the diameter ({0}).", radius));
                    }

                    break;

                case ToolType.Drill:
                    if (TipAngle <= 0 || TipAngle >= 180)
                    {
                        problems.Add("Drill tip angle must be between 0 and 180 degrees.");
                    }

                    break;

                case ToolType.ChamferMill:
                    if (TipAngle <= 0 || TipAngle >= 180)
                    {
                        problems.Add("Chamfer mill tip angle must be between 0 and 180 degrees.");
                    }

                    if (TipDiameter < 0)
                    {
                        problems.Add("Tip diameter cannot be negative.");
                    }
                    else if (Diameter > 0 && TipDiameter >= Diameter)
                    {
                        problems.Add("Tip diameter must be smaller than the cutting diameter.");
                    }

                    break;
            }

            return problems;
        }

        public ToolGeometry Clone()
        {
            return (ToolGeometry)MemberwiseClone();
        }

        internal const double Tolerance = 1e-9;

        private static string Format(string template, double value)
        {
            return string.Format(
                CultureInfo.CurrentCulture, template, value.ToString("0.###", CultureInfo.CurrentCulture));
        }
    }
}
