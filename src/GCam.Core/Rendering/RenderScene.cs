using System;
using System.Collections.Generic;
using System.Linq;

namespace GCam.Core.Rendering
{
    /// <summary>
    /// Everything G-CAM wants drawn over one document's 3D view, as named layers.
    /// </summary>
    /// <remarks>
    /// The whole of Core's rendering story. Producers - the stock preview today,
    /// toolpaths and the simulated tool later - call <see cref="Set"/> with their own
    /// layer name and the batches to draw; a renderer reads the scene and puts it on
    /// screen. Neither side knows anything about the other, which is what keeps Core free
    /// of SOLIDWORKS while still deciding what appears in its window.
    ///
    /// <b>Changes are detected by <see cref="Version"/>, not by watching the contents.</b>
    /// A renderer converts the vertices into whatever its graphics API wants and caches
    /// the result; comparing one integer per frame tells it whether the cache is stale.
    /// That is also why <see cref="RenderBatch"/> is immutable - a batch that could be
    /// edited in place would let the cache drift without the version moving.
    ///
    /// <b>Not thread-safe, and deliberately so.</b> A scene belongs to a document, and
    /// everything touching a document runs on the SOLIDWORKS main thread. Toolpath
    /// calculation happening off-thread builds its batches off-thread and marshals the
    /// one call to <see cref="Set"/> back, the same as every other SOLIDWORKS-side call.
    /// </remarks>
    public sealed class RenderScene
    {
        private readonly List<RenderLayer> _layers = new List<RenderLayer>();

        /// <summary>
        /// Bumped by every change. A renderer caches against this and rebuilds when it
        /// moves.
        /// </summary>
        public int Version { get; private set; }

        /// <summary>Layers in the order they were first added, which is drawing order.</summary>
        public IReadOnlyList<RenderLayer> Layers => _layers;

        /// <summary>True when there is nothing visible to draw.</summary>
        public bool IsEmpty => !_layers.Any(l => l.Visible && l.Batches.Count > 0);

        /// <summary>Raised after any change, so a view can ask for a repaint.</summary>
        public event EventHandler Changed;

        /// <summary>
        /// Replaces the named layer's contents, adding the layer if it is new.
        /// </summary>
        /// <remarks>
        /// Replace rather than append: a producer re-states everything it wants drawn
        /// each time. Incremental updates would need the producer to track what it had
        /// already sent, and the batches are rebuilt from the model anyway.
        ///
        /// An existing layer keeps its position, so a layer being refreshed does not
        /// jump in front of or behind the others.
        /// </remarks>
        public void Set(string name, IEnumerable<RenderBatch> batches)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A render layer needs a name.", nameof(name));
            }

            RenderLayer existing = Find(name);

            if (existing == null)
            {
                _layers.Add(new RenderLayer(name, batches));
            }
            else
            {
                existing.Replace(batches);
            }

            Touch();
        }

        /// <summary>Takes a layer away. False when there was no such layer.</summary>
        public bool Remove(string name)
        {
            RenderLayer layer = Find(name);

            if (layer == null)
            {
                return false;
            }

            _layers.Remove(layer);
            Touch();
            return true;
        }

        /// <summary>
        /// Shows or hides a layer without discarding it. False when there is no such
        /// layer.
        /// </summary>
        public bool SetVisible(string name, bool visible)
        {
            RenderLayer layer = Find(name);

            if (layer == null || layer.Visible == visible)
            {
                return layer != null;
            }

            layer.Visible = visible;
            Touch();
            return true;
        }

        public void Clear()
        {
            if (_layers.Count == 0)
            {
                return;
            }

            _layers.Clear();
            Touch();
        }

        private RenderLayer Find(string name) =>
            _layers.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.Ordinal));

        private void Touch()
        {
            unchecked
            {
                Version++;
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
