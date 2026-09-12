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
        /// INCLUDED angle of the point, in degrees - 118 or 135 for a typical drill,
        /// 90 for a 45-degree chamfer mill. Not used by end mills.
        /// </summary>
        /// <remarks>
        /// Always the included angle here, whatever the source format called it. HSM
        /// libraries store a half-angle for chamfer mills and an included angle for
        /// drills under the same attribute name; the importer normalises both to this.
        /// </remarks>
        public double TipAngle { get; set; }

        /// <summary>
        /// Secondary point angle in degrees, where a tool has one - a spot drill with a
        /// 90 degree primary point may have a 60 degree secondary relief. Carried for
        /// fidelity; the cutter profile uses <see cref="TipAngle"/>.
        /// </summary>
        public double SecondTipAngle { get; set; }

        /// <summary>
        /// Diameter of the flat at the very tip of a chamfer mill. Zero for a true point.
        /// </summary>
        public double TipDiameter { get; set; }

        /// <summary>Length of cut - how deep the flutes reach.</summary>
        public double FluteLength { get; set; }

        /// <summary>
        /// Distance from the tip to where the shank begins. Usually equals
        /// <see cref="FluteLength"/>, but differs on a necked or reduced-shank tool -
        /// and it is the shoulder, not the flutes, that collides with a workpiece.
        /// </summary>
        public double ShoulderLength { get; set; }

        /// <summary>
        /// Length of the tool body below the holder. Needed to work out how far the
        /// tool sticks out, which is what holder collision checking will use.
        /// </summary>
        public double BodyLength { get; set; }

        /// <summary>Thread pitch in mm, for taps. Zero for everything else.</summary>
        public double ThreadPitch { get; set; }

        /// <summary>
        /// Included angle of the thread form in degrees - 60 for metric and UN threads.
        /// Used by thread mills and taps.
        /// </summary>
        public double ThreadProfileAngle { get; set; }

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
                    if (Math.Abs(CornerRadius) > Precision.Epsilon)
                    {
                        problems.Add("A flat end mill must have a corner radius of zero.");
                    }

                    break;

                case ToolType.BallEndMill:
                    if (Diameter > 0 && Math.Abs(CornerRadius - radius) > Precision.Epsilon)
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
                    else if (Diameter > 0 && CornerRadius > radius + Precision.Epsilon)
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
                case ToolType.SpotDrill:
                    if (TipAngle <= 0 || TipAngle >= 180)
                    {
                        problems.Add($"{type} tip angle must be between 0 and 180 degrees.");
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

                case ToolType.Tap:
                    if (ThreadPitch <= 0)
                    {
                        problems.Add("A tap must have a thread pitch greater than zero.");
                    }

                    break;
            }

            return problems;
        }

        public ToolGeometry Clone()
        {
            return (ToolGeometry)MemberwiseClone();
        }

        private static string Format(string template, double value)
        {
            return string.Format(
                CultureInfo.CurrentCulture, template, value.ToString("0.###", CultureInfo.CurrentCulture));
        }
    }
}
