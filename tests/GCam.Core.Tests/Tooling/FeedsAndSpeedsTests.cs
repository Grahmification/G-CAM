using GCam.Core.Tooling;
using Xunit;

namespace GCam.Core.Tests.Tooling
{
    public class FeedsAndSpeedsTests
    {
        // The 0.5" flat end mill from the HSMWorks export in docs/example_files:
        // 12.7mm, 7500 rpm, 1800 mm/min, 3 flutes.
        private const double Diameter = 12.7;
        private const double Rpm = 7500;
        private const double Feed = 1800;
        private const int Teeth = 3;

        [Fact]
        public void Surface_speed_is_metres_per_minute_not_millimetres()
        {
            // pi * 12.7 * 7500 = 299236.7 mm/min = 299.2367 m/min. Off by 1000 here is
            // the classic CAM units bug, and it looks entirely plausible on screen.
            double v = FeedsAndSpeeds.SurfaceSpeed(Diameter, Rpm);

            Assert.Equal(299.2367, v, 4);
        }

        [Fact]
        public void Spindle_rpm_inverts_surface_speed()
        {
            double rpm = FeedsAndSpeeds.SpindleRpm(Diameter, 299.2367);

            Assert.Equal(Rpm, rpm, 2);
        }

        [Fact]
        public void Rpm_survives_a_round_trip_through_surface_speed()
        {
            // The property the editable-both-ends boxes depend on: typing in one box and
            // reading the other back must not drift.
            double v = FeedsAndSpeeds.SurfaceSpeed(Diameter, Rpm);
            double back = FeedsAndSpeeds.SpindleRpm(Diameter, v);

            Assert.Equal(Rpm, back, 9);
        }

        [Fact]
        public void Surface_speed_survives_a_round_trip_through_rpm()
        {
            double rpm = FeedsAndSpeeds.SpindleRpm(Diameter, 200);
            double back = FeedsAndSpeeds.SurfaceSpeed(Diameter, rpm);

            Assert.Equal(200, back, 9);
        }

        [Fact]
        public void Feed_per_tooth_divides_by_rpm_and_tooth_count()
        {
            double fz = FeedsAndSpeeds.FeedPerTooth(Feed, Rpm, Teeth);

            Assert.Equal(0.08, fz, 9);
        }

        [Fact]
        public void Feed_inverts_feed_per_tooth()
        {
            double feed = FeedsAndSpeeds.FeedFromFeedPerTooth(0.08, Rpm, Teeth);

            Assert.Equal(Feed, feed, 9);
        }

        [Fact]
        public void Feed_survives_a_round_trip_through_chip_load()
        {
            double fz = FeedsAndSpeeds.FeedPerTooth(Feed, Rpm, Teeth);
            double back = FeedsAndSpeeds.FeedFromFeedPerTooth(fz, Rpm, Teeth);

            Assert.Equal(Feed, back, 9);
        }

        [Fact]
        public void Feed_per_revolution_divides_by_rpm_alone()
        {
            double fn = FeedsAndSpeeds.FeedPerRevolution(600, Rpm);

            Assert.Equal(0.08, fn, 9);
        }

        [Fact]
        public void Feed_survives_a_round_trip_through_feed_per_revolution()
        {
            double fn = FeedsAndSpeeds.FeedPerRevolution(Feed, Rpm);
            double back = FeedsAndSpeeds.FeedFromFeedPerRevolution(fn, Rpm);

            Assert.Equal(Feed, back, 9);
        }

        [Theory]
        [InlineData(0, 7500)]
        [InlineData(-12.7, 7500)]
        [InlineData(12.7, 0)]
        [InlineData(12.7, -7500)]
        public void Surface_speed_is_zero_when_an_input_is_missing(double diameter, double rpm)
        {
            // A half-filled form is a normal state while someone types, not an error.
            Assert.Equal(0, FeedsAndSpeeds.SurfaceSpeed(diameter, rpm));
            Assert.Equal(0, FeedsAndSpeeds.SpindleRpm(diameter, rpm));
        }

        [Theory]
        [InlineData(7500, 0)]
        [InlineData(7500, -3)]
        [InlineData(0, 3)]
        [InlineData(-7500, 3)]
        public void Chip_load_is_zero_when_an_input_is_missing(double rpm, int teeth)
        {
            Assert.Equal(0, FeedsAndSpeeds.FeedPerTooth(Feed, rpm, teeth));
            Assert.Equal(0, FeedsAndSpeeds.FeedFromFeedPerTooth(0.08, rpm, teeth));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-7500)]
        public void Feed_per_revolution_is_zero_without_a_spindle_speed(double rpm)
        {
            Assert.Equal(0, FeedsAndSpeeds.FeedPerRevolution(Feed, rpm));
            Assert.Equal(0, FeedsAndSpeeds.FeedFromFeedPerRevolution(0.08, rpm));
        }

        [Fact]
        public void CuttingData_feed_per_tooth_agrees_with_the_shared_arithmetic()
        {
            // CuttingData.FeedPerTooth delegates here rather than keeping its own copy of
            // the formula. This is the test that stops the two drifting apart again.
            var cutting = new CuttingData { SpindleRpm = Rpm, CuttingFeed = Feed };

            Assert.Equal(
                FeedsAndSpeeds.FeedPerTooth(Feed, Rpm, Teeth),
                cutting.FeedPerTooth(Teeth),
                9);
        }

        [Fact]
        public void CuttingData_feed_per_tooth_is_still_zero_without_flutes_or_speed()
        {
            var cutting = new CuttingData { SpindleRpm = Rpm, CuttingFeed = Feed };

            Assert.Equal(0, cutting.FeedPerTooth(0));
            Assert.Equal(0, new CuttingData { CuttingFeed = Feed }.FeedPerTooth(Teeth));
        }
    }
}
