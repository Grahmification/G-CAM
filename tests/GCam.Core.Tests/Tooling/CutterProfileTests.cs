using System;
using System.Linq;
using GCam.Core.Tooling;
using Xunit;

namespace GCam.Core.Tests.Tooling
{
    /// <summary>
    /// The silhouette is what every geometry consumer sees, so its shape matters more
    /// than any other single thing in the tooling model.
    /// </summary>
    public class CutterProfileTests
    {
        private const double Tol = 1e-6;

        private static ToolGeometry EndMill(double diameter, double cornerRadius, double fluteLength = 30)
        {
            return new ToolGeometry
            {
                Diameter = diameter,
                CornerRadius = cornerRadius,
                FluteLength = fluteLength,
                FluteCount = 4,
            };
        }

        [Fact]
        public void Flat_end_mill_is_full_width_at_the_tip()
        {
            CutterProfile profile = CutterProfile.For(ToolType.FlatEndMill, EndMill(10, 0));

            Assert.Equal(5, profile.MaxRadius, 6);
            Assert.Equal(5, profile.RadiusAt(0), 6);
            Assert.Equal(5, profile.RadiusAt(10), 6);

            // The whole flat bottom sits at height zero.
            Assert.Equal(0, profile.HeightAt(0), 6);
            Assert.Equal(0, profile.HeightAt(5), 6);
        }

        [Fact]
        public void Ball_end_mill_is_a_point_at_the_tip_and_full_width_at_its_radius()
        {
            CutterProfile profile = CutterProfile.For(ToolType.BallEndMill, EndMill(10, 5));

            Assert.Equal(0, profile.RadiusAt(0), 3);
            Assert.Equal(5, profile.MaxRadius, 6);

            // Full width is reached exactly one radius up from the tip.
            Assert.Equal(5, profile.RadiusAt(5), 3);
        }

        [Theory]
        [InlineData(1.0)]
        [InlineData(2.5)]
        [InlineData(4.0)]
        public void Ball_end_mill_profile_lies_on_the_sphere(double height)
        {
            const double R = 5;
            CutterProfile profile = CutterProfile.For(
                ToolType.BallEndMill, EndMill(2 * R, R), chordTolerance: 0.0001);

            // r^2 + (h - R)^2 = R^2
            double expected = Math.Sqrt((R * R) - ((height - R) * (height - R)));

            Assert.Equal(expected, profile.RadiusAt(height), 3);
        }

        [Theory]
        [InlineData(0.5)]
        [InlineData(1.0)]
        [InlineData(2.5)]
        [InlineData(4.0)]
        [InlineData(4.9)]
        public void Ball_end_mill_never_models_the_cutter_as_larger_than_it_is(double height)
        {
            // The polyline is inscribed in the arc, so it is never wider than the true
            // sphere. Consumers that must be conservative inflate by ChordTolerance;
            // this test pins the direction of the error so that advice stays true.
            const double R = 5;
            CutterProfile profile = CutterProfile.For(ToolType.BallEndMill, EndMill(2 * R, R));

            double exact = Math.Sqrt((R * R) - ((height - R) * (height - R)));

            Assert.True(
                profile.RadiusAt(height) <= exact + Tol,
                $"Profile was wider than the true sphere at h={height}, which would make "
                + "gouge checking optimistic.");
        }

        [Fact]
        public void Bull_nose_has_a_flat_centre_then_a_corner_radius()
        {
            CutterProfile profile = CutterProfile.For(ToolType.BullNoseEndMill, EndMill(10, 2));

            // Flat out to (radius - cornerRadius) = 3mm, all at height zero.
            Assert.Equal(0, profile.HeightAt(3), 6);

            // Then the corner arc lifts the profile; full width at cornerRadius height.
            Assert.Equal(5, profile.RadiusAt(2), 2);
            Assert.True(profile.HeightAt(4) > 0);
        }

        [Fact]
        public void Drill_tip_height_follows_the_point_angle()
        {
            var geometry = new ToolGeometry
            {
                Diameter = 10,
                TipAngle = 118,
                FluteLength = 50,
                FluteCount = 2,
            };

            CutterProfile profile = CutterProfile.For(ToolType.Drill, geometry);

            // Half angle 59 degrees; tip height = radius / tan(59).
            double expected = 5.0 / Math.Tan(59 * Math.PI / 180.0);

            Assert.Equal(expected, profile.HeightAt(5), 3);
            Assert.Equal(0, profile.RadiusAt(0), 3);
        }

        [Fact]
        public void Chamfer_mill_keeps_its_tip_flat()
        {
            var geometry = new ToolGeometry
            {
                Diameter = 12,
                TipAngle = 90,
                TipDiameter = 2,
                FluteLength = 20,
                FluteCount = 4,
            };

            CutterProfile profile = CutterProfile.For(ToolType.ChamferMill, geometry);

            // The 2mm flat is at height zero.
            Assert.Equal(0, profile.HeightAt(1), 6);

            // 90 degrees included: rises 1mm for every 1mm of radius beyond the flat.
            Assert.Equal(5.0, profile.HeightAt(6), 3);
        }

        [Fact]
        public void Radius_never_decreases_with_height()
        {
            foreach (CutterProfile profile in AllSampleProfiles())
            {
                double previous = -1;
                foreach (ProfilePoint point in profile.Points)
                {
                    Assert.True(
                        point.Radius >= previous - Tol,
                        $"Profile radius decreased at {point}; consumers rely on it being monotonic.");
                    previous = point.Radius;
                }
            }
        }

        [Fact]
        public void Profile_runs_the_full_flute_length()
        {
            foreach (CutterProfile profile in AllSampleProfiles())
            {
                Assert.Equal(30, profile.Height, 6);
            }
        }

        [Fact]
        public void Finer_chord_tolerance_gives_more_points_and_a_closer_arc()
        {
            ToolGeometry geometry = EndMill(10, 5);

            CutterProfile coarse = CutterProfile.For(ToolType.BallEndMill, geometry, chordTolerance: 0.5);
            CutterProfile fine = CutterProfile.For(ToolType.BallEndMill, geometry, chordTolerance: 0.001);

            Assert.True(fine.Points.Count > coarse.Points.Count);

            // Compare WORST error over many heights, not one sample: a coarse polyline
            // happens to be exact wherever a vertex lands, so a single probe can make
            // it look better than a fine one.
            Assert.True(WorstSphereError(fine, 5) < WorstSphereError(coarse, 5));
        }

        private static double WorstSphereError(CutterProfile profile, double sphereRadius)
        {
            double worst = 0;
            for (int i = 0; i <= 200; i++)
            {
                double height = sphereRadius * i / 200.0;
                double exact = Math.Sqrt(
                    (sphereRadius * sphereRadius) - ((height - sphereRadius) * (height - sphereRadius)));
                worst = Math.Max(worst, Math.Abs(profile.RadiusAt(height) - exact));
            }

            return worst;
        }

        [Fact]
        public void Invalid_geometry_is_rejected_rather_than_producing_a_nonsense_profile()
        {
            // Corner radius larger than the tool radius.
            ToolGeometry bad = EndMill(10, 8);

            Assert.Throws<ArgumentException>(() => CutterProfile.For(ToolType.BullNoseEndMill, bad));
        }

        [Fact]
        public void Asking_for_a_radius_beyond_the_cutter_is_an_error_not_a_silent_zero()
        {
            CutterProfile profile = CutterProfile.For(ToolType.FlatEndMill, EndMill(10, 0));

            Assert.Throws<ArgumentOutOfRangeException>(() => profile.HeightAt(6));
            Assert.Throws<ArgumentOutOfRangeException>(() => profile.HeightAt(-1));
        }

        private static CutterProfile[] AllSampleProfiles()
        {
            return new[]
            {
                CutterProfile.For(ToolType.FlatEndMill, EndMill(10, 0)),
                CutterProfile.For(ToolType.BallEndMill, EndMill(10, 5)),
                CutterProfile.For(ToolType.BullNoseEndMill, EndMill(10, 2)),
                CutterProfile.For(
                    ToolType.Drill,
                    new ToolGeometry { Diameter = 6, TipAngle = 118, FluteLength = 30, FluteCount = 2 }),
                CutterProfile.For(
                    ToolType.ChamferMill,
                    new ToolGeometry
                    {
                        Diameter = 10, TipAngle = 90, TipDiameter = 1, FluteLength = 30, FluteCount = 4,
                    }),
            };
        }
    }
}
