using System;
using System.Collections.Generic;
using System.Linq;

namespace GCam.Core.Rendering
{
    /// <summary>
    /// A named group of batches that is shown or hidden as a unit - the stock box, a
    /// toolpath, the tool, the simulated material.
    /// </summary>
    /// <remarks>
    /// Naming is what lets one producer replace its own drawing without knowing what
    /// else is on screen. The stock preview owns a layer, sets it whenever the job
    /// changes and removes it when nothing is selected; a toolpath renderer will do the
    /// same beside it, and neither has to coordinate with the other.
    ///
    /// Mutate through <see cref="RenderScene"/> rather than here, so the scene's version
    /// moves and the view repaints.
    /// </remarks>
    public sealed class RenderLayer
    {
        internal RenderLayer(string name, IEnumerable<RenderBatch> batches, IEnumerable<ScreenArrow> arrows)
        {
            Name = name;
            Visible = true;
            Replace(batches, arrows);
        }

        public string Name { get; }

        public IReadOnlyList<RenderBatch> Batches { get; private set; }

        /// <summary>
        /// Arrows that keep their size on screen, drawn over the batches.
        /// </summary>
        /// <remarks>
        /// Apart from <see cref="Batches"/> because they are not fixed geometry: a
        /// renderer has to rebuild these every frame, and keeping them in the same list
        /// would cost the cache that the rest of the scene exists to keep. See
        /// <see cref="ScreenArrow"/>.
        /// </remarks>
        public IReadOnlyList<ScreenArrow> Arrows { get; private set; }

        public bool Visible { get; internal set; }

        /// <summary>True when this layer has nothing to put on screen.</summary>
        public bool IsEmpty => Batches.Count == 0 && Arrows.Count == 0;

        internal void Replace(IEnumerable<RenderBatch> batches, IEnumerable<ScreenArrow> arrows)
        {
            Batches = Sanitise(batches);
            Arrows = arrows?.Where(a => a != null).ToArray() ?? new ScreenArrow[0];
        }

        /// <summary>
        /// Empty batches are dropped on the way in rather than skipped on the way out.
        /// A batch with two vertices and no way to make a triangle is a caller's mistake
        /// the renderer should never have to think about once per frame.
        /// </summary>
        private static IReadOnlyList<RenderBatch> Sanitise(IEnumerable<RenderBatch> batches)
        {
            if (batches == null)
            {
                return new RenderBatch[0];
            }

            return batches.Where(b => b != null && !b.IsEmpty).ToArray();
        }

        public override string ToString() =>
            $"{Name}: {Batches.Count} batch(es), {Arrows.Count} arrow(s)" +
            (Visible ? string.Empty : ", hidden");
    }
}
