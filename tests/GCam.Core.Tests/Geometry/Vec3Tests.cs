using GCam.Core.Geometry.Primitives;
using Xunit;

namespace GCam.Core.Tests.Geometry
{
    public class Vec3Tests
    {
        [Fact]
        public void Length_is_the_distance_from_the_origin()
        {
            Assert.Equal(5, new Vec3(3, 4, 0).Length, 6);
            Assert.Equal(0, Vec3.Zero.Length, 6);
        }

        [Fact]
        public void Normalised_keeps_the_direction_and_makes_the_length_one()
        {
            Vec3 unit = new Vec3(0, 0, -7).Normalised();

            Assert.True(unit.Equals(new Vec3(0, 0, -1)));
            Assert.Equal(1, unit.Length, 6);
        }

        [Fact]
        public void Normalising_nothing_gives_nothing_rather_than_NaN()
        {
            // A zero vector has no direction. Returning zero keeps a caller's bug from
            // spreading through the arithmetic that follows as a quiet NaN.
            Assert.True(Vec3.Zero.Normalised().Equals(Vec3.Zero));
        }
    }
}
