using System.Collections.Generic;
using GCam.Core.Strategies;
using Xunit;

namespace GCam.Core.Tests.Strategies
{
    public class StrategyIdTests
    {
        [Fact]
        public void The_well_known_ids_match_the_names_HSMWorks_uses()
        {
            // These are matched against the strategy attribute in an .hsmworks-template,
            // so they are not free to be tidied up.
            Assert.Equal("contour2d", StrategyId.Contour2d.Value);
            Assert.Equal("face", StrategyId.Face.Value);
            Assert.Equal("adaptive2d", StrategyId.Adaptive2d.Value);
            Assert.Equal("drill", StrategyId.Drill.Value);
        }

        [Fact]
        public void Ids_compare_by_value_not_by_reference()
        {
            Assert.Equal(StrategyId.Contour2d, new StrategyId("contour2d"));
            Assert.True(StrategyId.Contour2d == new StrategyId("contour2d"));
            Assert.True(StrategyId.Contour2d != StrategyId.Drill);
        }

        [Theory]
        [InlineData("CONTOUR2D")]
        [InlineData("Contour2D")]
        [InlineData("  contour2d  ")]
        public void Case_and_surrounding_space_do_not_change_identity(string written)
        {
            // Nothing guarantees another tool's casing, and a stray space in a file should
            // not produce an unknown strategy.
            Assert.Equal(StrategyId.Contour2d, new StrategyId(written));
        }

        [Fact]
        public void Equal_ids_hash_together_so_they_work_as_dictionary_keys()
        {
            var map = new Dictionary<StrategyId, string>
            {
                [StrategyId.Contour2d] = "found",
            };

            Assert.Equal("found", map[new StrategyId("CONTOUR2D")]);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void An_id_with_no_name_is_empty_rather_than_null(string written)
        {
            var id = new StrategyId(written);

            Assert.True(id.IsEmpty);
            Assert.Equal(string.Empty, id.Value);
            Assert.Equal(string.Empty, id.ToString());
        }

        [Fact]
        public void The_default_id_is_empty_and_does_not_throw()
        {
            // default(StrategyId) turns up whenever one is a field on a freshly
            // constructed object, so it has to behave rather than null-reference.
            StrategyId id = default;

            Assert.True(id.IsEmpty);
            Assert.Equal(string.Empty, id.Value);
            Assert.NotEqual(StrategyId.Contour2d, id);
        }
    }
}
