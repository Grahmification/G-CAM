using System;
using System.Collections.Generic;
using System.Linq;

namespace GCam.Core.Tooling
{
    /// <summary>A point on the cutter silhouette: how wide the tool is at a given height.</summary>
    public struct ProfilePoint
    {
        public ProfilePoint(double height, double radius)
        {
            Height = height;
            Radius = radius;
        }

        /// <summary>Height above the tool tip, mm.</summary>
        public double Height { get; }

        /// <summary>Distance from the tool axis, mm.</summary>
        public double Radius { get; }

        public override string ToString() => $"(h={Height:0.###}, r={Radius:0.###})";
    }

    /// <summary>
    /// The cutter's silhouette as a polyline, measured from the tip upward.
    /// </summary>
    /// <remarks>
    /// This is the single representation every piece of geometry code uses. The Z-map
    /// sweep, gouge checking, offsetting and rendering all care only about how wide the
    /// tool is at a given height - not whether it is a ball nose or a chamfer mill.
    /// Keeping that knowledge here means none of them grow a switch on
    /// <see cref="ToolType"/>.
    ///
    /// Arcs are tessellated to a chord tolerance at construction, so consumers work
    /// with straight segments only.
    ///
    /// Radius is non-decreasing with height for every supported tool, and the rest of
    /// this class relies on that.
    /// </remarks>
    public sealed class CutterProfile
    {
        /// <summary>
        /// Default chord tolerance for tessellating corner arcs, mm. Finer than any
        /// sensible machining tolerance, and cheap - a 90 degree arc needs about
        /// 30 segments at this setting.
        /// </summary>
        public const double DefaultChordTolerance = 0.005;

        private readonly ProfilePoint[] _points;

        private CutterProfile(ProfilePoint[] points, double chordTolerance)
        {
            _points = points;
            ChordTolerance = chordTolerance;
        }

        public IReadOnlyList<ProfilePoint> Points => _points;

        /// <summary>
        /// The chord tolerance this profile was built to, mm.
        /// </summary>
        /// <remarks>
        /// Two things to know before relying on it:
        ///
        /// The polyline is INSCRIBED in the true arc, so the modelled cutter is very
        /// slightly SMALLER than the real one. That is the unsafe direction for gouge
        /// checking and collision detection - code that must be conservative should
        /// inflate the profile by this value rather than assume it is exact.
        ///
        /// The tolerance bounds deviation measured PERPENDICULAR to the arc. Error
        /// measured radially at a fixed height is larger wherever the profile is steep,
        /// by a factor of 1/cos of the profile's slope. Near the tip of a ball nose that
        /// factor approaches infinity, though the absolute error stays small.
        /// </remarks>
        public double ChordTolerance { get; }

        /// <summary>Widest radius of the cutter, mm.</summary>
        public double MaxRadius => _points[_points.Length - 1].Radius;

        /// <summary>Height of the profile - the flute length, mm.</summary>
        public double Height => _points[_points.Length - 1].Height;

        /// <summary>
        /// Builds the silhouette for a tool.
        /// </summary>
        /// <exception cref="ArgumentException">The geometry is not valid for the type.</exception>
        public static CutterProfile For(
            ToolType type, ToolGeometry geometry, double chordTolerance = DefaultChordTolerance)
        {
            if (geometry == null)
            {
                throw new ArgumentNullException(nameof(geometry));
            }

            IReadOnlyList<string> problems = geometry.Validate(type);
            if (problems.Count > 0)
            {
                throw new ArgumentException(
                    "Cannot build a cutter profile: " + string.Join(" ", problems), nameof(geometry));
            }

            if (chordTolerance <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chordTolerance), "Chord tolerance must be positive.");
            }

            double radius = geometry.Diameter / 2.0;
            double fluteLength = geometry.FluteLength;
            var points = new List<ProfilePoint>();

            switch (type)
            {
                case ToolType.FlatEndMill:
                case ToolType.BallEndMill:
                case ToolType.BullNoseEndMill:
                    // One generator covers all three: a flat end mill is corner radius 0
                    // and a ball nose is corner radius = radius. The arc below degenerates
                    // to nothing in the first case and to the whole end in the second.
                    AppendRoundedEnd(points, radius, geometry.CornerRadius, chordTolerance);
                    break;

                case ToolType.Drill:
                    // Cone from a true point out to full diameter.
                    points.Add(new ProfilePoint(0, 0));
                    points.Add(new ProfilePoint(ConeHeight(radius, 0, geometry.TipAngle), radius));
                    break;

                case ToolType.ChamferMill:
                    // As a drill, but usually with a small flat at the tip.
                    double tipRadius = geometry.TipDiameter / 2.0;
                    points.Add(new ProfilePoint(0, 0));
                    if (tipRadius > ToolGeometry.Tolerance)
                    {
                        points.Add(new ProfilePoint(0, tipRadius));
                    }

                    points.Add(new ProfilePoint(ConeHeight(radius, tipRadius, geometry.TipAngle), radius));
                    break;

                default:
                    throw new ArgumentException("Unsupported tool type: " + type, nameof(type));
            }

            // Run the full diameter up to the top of the flutes. A tool whose end
            // geometry is already taller than its flute length is degenerate, but the
            // profile should still be monotonic rather than doubling back.
            double endHeight = points[points.Count - 1].Height;
            if (fluteLength > endHeight)
            {
                points.Add(new ProfilePoint(fluteLength, radius));
            }

            return new CutterProfile(points.ToArray(), chordTolerance);
        }

        /// <summary>
        /// How wide the cutter is at <paramref name="height"/> above the tip.
        /// Above the flutes, reports the full radius.
        /// </summary>
        public double RadiusAt(double height)
        {
            if (height <= 0)
            {
                // At the tip the profile may be vertical (a flat end); report the widest.
                return WidestAt(0);
            }

            if (height >= Height)
            {
                return MaxRadius;
            }

            double best = 0;
            for (int i = 1; i < _points.Length; i++)
            {
                ProfilePoint a = _points[i - 1];
                ProfilePoint b = _points[i];

                if (height < a.Height || height > b.Height)
                {
                    continue;
                }

                double span = b.Height - a.Height;
                double candidate = span <= ToolGeometry.Tolerance
                    ? Math.Max(a.Radius, b.Radius)
                    : a.Radius + ((b.Radius - a.Radius) * ((height - a.Height) / span));

                best = Math.Max(best, candidate);
            }

            return best;
        }

        /// <summary>
        /// The lowest point of the cutter at a given distance from its axis - how far
        /// the tool dips at that radial offset. This is the function a Z-map sweep needs.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="radius"/> is negative or wider than the cutter.
        /// </exception>
        public double HeightAt(double radius)
        {
            if (radius < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(radius), "Radius cannot be negative.");
            }

            if (radius > MaxRadius + ToolGeometry.Tolerance)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(radius),
                    $"Radius {radius:0.###} is outside the cutter, whose maximum radius is {MaxRadius:0.###}.");
            }

            double best = double.MaxValue;
            for (int i = 1; i < _points.Length; i++)
            {
                ProfilePoint a = _points[i - 1];
                ProfilePoint b = _points[i];

                if (radius < a.Radius || radius > b.Radius)
                {
                    continue;
                }

                double span = b.Radius - a.Radius;
                double candidate = span <= ToolGeometry.Tolerance
                    ? Math.Min(a.Height, b.Height)
                    : a.Height + ((b.Height - a.Height) * ((radius - a.Radius) / span));

                best = Math.Min(best, candidate);
            }

            return best == double.MaxValue ? 0 : best;
        }

        private double WidestAt(double height)
        {
            return _points.Where(p => Math.Abs(p.Height - height) <= ToolGeometry.Tolerance)
                          .Select(p => p.Radius)
                          .DefaultIfEmpty(0)
                          .Max();
        }

        /// <summary>
        /// Flat bottom out to (radius - cornerRadius), then a quarter arc up to full radius.
        /// </summary>
        private static void AppendRoundedEnd(
            List<ProfilePoint> points, double radius, double cornerRadius, double chordTolerance)
        {
            double flat = radius - cornerRadius;

            points.Add(new ProfilePoint(0, 0));

            if (flat > ToolGeometry.Tolerance)
            {
                points.Add(new ProfilePoint(0, flat));
            }

            if (cornerRadius <= ToolGeometry.Tolerance)
            {
                return;
            }

            // Arc centred at (height = cornerRadius, radius = flat), swept from pointing
            // straight down to pointing straight out.
            int steps = ArcSteps(cornerRadius, Math.PI / 2, chordTolerance);
            for (int i = 1; i <= steps; i++)
            {
                double angle = (Math.PI / 2) * i / steps;
                points.Add(new ProfilePoint(
                    cornerRadius - (cornerRadius * Math.Cos(angle)),
                    flat + (cornerRadius * Math.Sin(angle))));
            }
        }

        /// <summary>
        /// Segments needed so no chord deviates from the arc by more than the tolerance.
        /// </summary>
        private static int ArcSteps(double arcRadius, double sweepRadians, double chordTolerance)
        {
            if (chordTolerance >= arcRadius)
            {
                return 1;
            }

            double maxStep = 2 * Math.Acos(1 - (chordTolerance / arcRadius));
            int steps = (int)Math.Ceiling(sweepRadians / maxStep);
            return Math.Min(Math.Max(steps, 1), 720);
        }

        /// <summary>Height of a cone rising from <paramref name="lowerRadius"/> to
        /// <paramref name="upperRadius"/> at the given included angle.</summary>
        private static double ConeHeight(double upperRadius, double lowerRadius, double includedAngleDegrees)
        {
            double halfAngle = includedAngleDegrees * Math.PI / 360.0;
            return (upperRadius - lowerRadius) / Math.Tan(halfAngle);
        }
    }
}
