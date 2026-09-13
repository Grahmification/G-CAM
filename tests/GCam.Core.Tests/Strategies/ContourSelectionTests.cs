using GCam.Core.Model;
using GCam.Core.Strategies.Shared;
using Xunit;

namespace GCam.Core.Tests.Strategies
{
    public class ContourSelectionTests
    {
        private static GeometryRef Edge() => new GeometryRef
        {
            PersistentId = "edge-1",
            Kind = GeometryRefKind.Edge,
            DisplayName = "Edge1",
        };

        [Fact]
        public void Tangent_propagation_is_on_by_default()
        {
            // Picking one edge of a filleted pocket and getting only that edge is never
            // what anybody meant.
            var selection = new ContourSelection(Edge());

            Assert.True(selection.PropagateTangent);
            Assert.False(selection.PropagateAlongZ);
            Assert.False(selection.Reversed);
        }

        [Fact]
        public void A_selection_with_no_entity_is_empty()
        {
            Assert.True(new ContourSelection().IsEmpty);
            Assert.True(new ContourSelection(new GeometryRef()).IsEmpty);
            Assert.False(new ContourSelection(Edge()).IsEmpty);
        }

        [Fact]
        public void It_describes_itself_by_what_was_picked()
        {
            Assert.Equal("Edge1", new ContourSelection(Edge()).ToString());
            Assert.Equal("Nothing selected", new ContourSelection().ToString());
        }

        [Fact]
        public void Cloning_copies_the_entity_and_the_modifiers_rather_than_sharing_them()
        {
            var original = new ContourSelection(Edge())
            {
                PropagateTangent = false,
                PropagateAlongZ = true,
                Reversed = true,
            };

            ContourSelection copy = original.Clone();
            copy.Entity.DisplayName = "Changed";
            copy.PropagateAlongZ = false;
            copy.Reversed = false;

            Assert.Equal("Edge1", original.Entity.DisplayName);
            Assert.True(original.PropagateAlongZ);
            Assert.True(original.Reversed);
            Assert.False(original.PropagateTangent);
        }

        [Fact]
        public void Cloning_an_empty_selection_does_not_invent_an_entity()
        {
            Assert.Null(new ContourSelection().Clone().Entity);
        }

        [Fact]
        public void The_modifiers_are_stored_intent_and_change_nothing_yet()
        {
            // Deliberately recorded: the flags round-trip, and no propagation happens.
            // Honouring them lands with the contour strategy and the geometry extraction
            // it needs. This test is here so that staying true is not an accident.
            var selection = new ContourSelection(Edge())
            {
                PropagateTangent = true,
                PropagateAlongZ = true,
            };

            ContourSelection copy = selection.Clone();

            Assert.Equal("edge-1", copy.Entity.PersistentId);
            Assert.True(copy.PropagateTangent);
            Assert.True(copy.PropagateAlongZ);
        }
    }
}
