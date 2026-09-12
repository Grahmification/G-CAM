using System;

namespace GCam.Core
{
    /// <summary>
    /// Unit conversions. G-CAM works internally in millimetres, degrees and mm/min.
    /// </summary>
    /// <remarks>
    /// Everything inside Core is millimetres. Conversion happens at the edges - when
    /// reading a library that declares inches, or when extracting geometry from
    /// SOLIDWORKS, which works in metres. Nothing in between should scale anything.
    ///
    /// These live here rather than in the readers that happen to need them because a
    /// physical constant duplicated across files is a constant that eventually
    /// disagrees with itself.
    ///
    /// Deliberately plain doubles rather than a unit-carrying Length type. A struct
    /// that made millimetres-versus-metres a compile error would be stronger, but it
    /// would touch every number in the domain model and every arithmetic expression in
    /// the geometry kernel. Revisit if unit bugs actually show up; the named constants
    /// below are the cheap 80% of that benefit.
    /// </remarks>
    public static class Units
    {
        /// <summary>Millimetres in an inch, exactly. Tool libraries may declare either.</summary>
        public const double MillimetresPerInch = 25.4;

        /// <summary>
        /// Millimetres in a metre.
        /// </summary>
        /// <remarks>
        /// The SOLIDWORKS API works exclusively in metres, so every length crossing the
        /// extraction boundary is scaled by this. Getting it wrong produces a toolpath
        /// out by a factor of 1000, which is the classic CAM integration bug - use the
        /// named constant so the intent is greppable rather than a bare 1000.
        /// </remarks>
        public const double MillimetresPerMetre = 1000.0;

        public static double InchesToMillimetres(double inches) => inches * MillimetresPerInch;

        public static double MillimetresToInches(double millimetres) => millimetres / MillimetresPerInch;

        /// <summary>Converts a SOLIDWORKS length into G-CAM's millimetres.</summary>
        public static double MetresToMillimetres(double metres) => metres * MillimetresPerMetre;

        /// <summary>Converts a G-CAM length into the metres the SOLIDWORKS API expects.</summary>
        public static double MillimetresToMetres(double millimetres) => millimetres / MillimetresPerMetre;

        public static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

        public static double RadiansToDegrees(double radians) => radians * 180.0 / Math.PI;
    }
}
