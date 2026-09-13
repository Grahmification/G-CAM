using System;
using System.Globalization;

namespace GCam.Core.Rendering
{
    /// <summary>
    /// A colour with transparency, each channel from 0 to 1.
    /// </summary>
    /// <remarks>
    /// Floats from 0 to 1 rather than bytes because that is what OpenGL takes, and this
    /// type exists to be handed to a renderer. Core has no opinion about how it is drawn;
    /// it only needs a way to say what colour something is without referencing a UI
    /// framework.
    ///
    /// <see cref="Alpha"/> below 1 marks a batch as transparent, which is what tells the
    /// renderer to draw it after the opaque ones and to leave the depth buffer alone.
    /// </remarks>
    public struct RenderColour : IEquatable<RenderColour>
    {
        public RenderColour(double red, double green, double blue, double alpha = 1.0)
        {
            Red = Clamp(red);
            Green = Clamp(green);
            Blue = Clamp(blue);
            Alpha = Clamp(alpha);
        }

        public double Red { get; }

        public double Green { get; }

        public double Blue { get; }

        public double Alpha { get; }

        /// <summary>True when this colour has to be blended rather than simply drawn.</summary>
        public bool IsTransparent => Alpha < 1.0 - Precision.Epsilon;

        public RenderColour WithAlpha(double alpha) => new RenderColour(Red, Green, Blue, alpha);

        private static double Clamp(double value) => value < 0 ? 0 : value > 1 ? 1 : value;

        public bool Equals(RenderColour other)
        {
            return Math.Abs(Red - other.Red) < Precision.Epsilon
                   && Math.Abs(Green - other.Green) < Precision.Epsilon
                   && Math.Abs(Blue - other.Blue) < Precision.Epsilon
                   && Math.Abs(Alpha - other.Alpha) < Precision.Epsilon;
        }

        public override bool Equals(object obj) => obj is RenderColour other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Red.GetHashCode();
                hash = (hash * 397) ^ Green.GetHashCode();
                hash = (hash * 397) ^ Blue.GetHashCode();
                hash = (hash * 397) ^ Alpha.GetHashCode();
                return hash;
            }
        }

        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "rgba({0:0.##}, {1:0.##}, {2:0.##}, {3:0.##})", Red, Green, Blue, Alpha);
        }
    }
}
