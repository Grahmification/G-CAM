using GCam.Core;
using Xunit;

namespace GCam.Core.Tests
{
    public class UnitsTests
    {
        [Fact]
        public void Inch_conversion_is_exact()
        {
            Assert.Equal(25.4, Units.InchesToMillimetres(1), 10);
            Assert.Equal(12.7, Units.InchesToMillimetres(0.5), 10);
            Assert.Equal(1.0, Units.MillimetresToInches(25.4), 10);
        }

        [Fact]
        public void Metre_conversion_matches_the_SolidWorks_boundary()
        {
            // SOLIDWORKS reports lengths in metres; a 10mm feature arrives as 0.01.
            Assert.Equal(10.0, Units.MetresToMillimetres(0.01), 10);
            Assert.Equal(0.01, Units.MillimetresToMetres(10.0), 10);
        }

        [Theory]
        [InlineData(1.0)]
        [InlineData(6.35)]
        [InlineData(0.0001)]
        [InlineData(1234.5678)]
        public void Conversions_round_trip(double value)
        {
            Assert.Equal(value, Units.MillimetresToInches(Units.InchesToMillimetres(value)), 10);
            Assert.Equal(value, Units.MillimetresToMetres(Units.MetresToMillimetres(value)), 10);
            Assert.Equal(value, Units.RadiansToDegrees(Units.DegreesToRadians(value)), 10);
        }

        [Fact]
        public void Angle_conversion_hits_the_familiar_values()
        {
            Assert.Equal(System.Math.PI, Units.DegreesToRadians(180), 10);
            Assert.Equal(System.Math.PI / 4, Units.DegreesToRadians(45), 10);
            Assert.Equal(90, Units.RadiansToDegrees(System.Math.PI / 2), 10);
        }
    }
}
