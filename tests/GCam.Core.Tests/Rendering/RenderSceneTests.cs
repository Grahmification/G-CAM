using System;
using System.Linq;
using GCam.Core.Geometry.Primitives;
using GCam.Core.Rendering;
using Xunit;

namespace GCam.Core.Tests.Rendering
{
    public class RenderSceneTests
    {
        private static readonly RenderColour Yellow = new RenderColour(1, 1, 0);

        [Fact]
        public void A_new_scene_is_empty()
        {
            var scene = new RenderScene();

            Assert.True(scene.IsEmpty);
            Assert.Empty(scene.Layers);
        }

        [Fact]
        public void Setting_a_layer_adds_it()
        {
            var scene = new RenderScene();

            scene.Set("stock", new[] { Triangle() });

            Assert.Single(scene.Layers);
            Assert.Equal("stock", scene.Layers[0].Name);
            Assert.False(scene.IsEmpty);
        }

        [Fact]
        public void Setting_a_layer_again_replaces_its_contents_and_keeps_its_place()
        {
            // Producers re-state everything they want drawn each time, so a second Set
            // must not stack up - and a layer being refreshed must not jump in front of
            // the others.
            var scene = new RenderScene();

            scene.Set("stock", new[] { Triangle() });
            scene.Set("toolpath", new[] { Triangle() });
            scene.Set("stock", new[] { Triangle(), Triangle() });

            Assert.Equal(2, scene.Layers.Count);
            Assert.Equal("stock", scene.Layers[0].Name);
            Assert.Equal(2, scene.Layers[0].Batches.Count);
        }

        [Fact]
        public void Every_change_moves_the_version()
        {
            // A renderer caches its converted vertex data against this, so a change that
            // did not move it would be drawn from a stale cache.
            var scene = new RenderScene();

            int start = scene.Version;

            scene.Set("stock", new[] { Triangle() });
            int afterSet = scene.Version;

            scene.SetVisible("stock", false);
            int afterHide = scene.Version;

            scene.Remove("stock");
            int afterRemove = scene.Version;

            Assert.NotEqual(start, afterSet);
            Assert.NotEqual(afterSet, afterHide);
            Assert.NotEqual(afterHide, afterRemove);
        }

        [Fact]
        public void A_change_that_changes_nothing_leaves_the_version_alone()
        {
            var scene = new RenderScene();
            scene.Set("stock", new[] { Triangle() });

            int before = scene.Version;

            Assert.False(scene.Remove("nothing-by-that-name"));
            Assert.True(scene.SetVisible("stock", true));

            Assert.Equal(before, scene.Version);
        }

        [Fact]
        public void Changing_the_scene_raises_Changed()
        {
            var scene = new RenderScene();

            int raised = 0;
            scene.Changed += (s, e) => raised++;

            scene.Set("stock", new[] { Triangle() });
            scene.Remove("stock");

            Assert.Equal(2, raised);
        }

        [Fact]
        public void A_hidden_layer_keeps_its_batches_but_leaves_the_scene_empty()
        {
            var scene = new RenderScene();
            scene.Set("stock", new[] { Triangle() });

            scene.SetVisible("stock", false);

            Assert.True(scene.IsEmpty);
            Assert.Single(scene.Layers[0].Batches);
        }

        [Fact]
        public void Batches_with_nothing_drawable_in_them_are_dropped_on_the_way_in()
        {
            // Cheaper to refuse a two-vertex triangle once than to check for one on every
            // frame.
            var scene = new RenderScene();

            scene.Set("stock", new[]
            {
                new RenderBatch(PrimitiveKind.Triangles, new[] { Vec3.Zero, Vec3.Zero }, Yellow),
                Triangle(),
            });

            Assert.Single(scene.Layers[0].Batches);
        }

        [Fact]
        public void A_layer_needs_a_name()
        {
            var scene = new RenderScene();

            Assert.Throws<ArgumentException>(() => scene.Set("  ", new[] { Triangle() }));
        }

        [Fact]
        public void Clearing_takes_every_layer_away()
        {
            var scene = new RenderScene();
            scene.Set("stock", new[] { Triangle() });
            scene.Set("toolpath", new[] { Triangle() });

            scene.Clear();

            Assert.Empty(scene.Layers);
            Assert.True(scene.IsEmpty);
        }

        private static RenderBatch Triangle() => new RenderBatch(
            PrimitiveKind.Triangles,
            new[] { Vec3.Zero, new Vec3(1, 0, 0), new Vec3(0, 1, 0) },
            Yellow);
    }
}
