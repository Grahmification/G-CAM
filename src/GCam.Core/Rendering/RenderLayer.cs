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
        internal RenderLayer(string name, IEnumerable<RenderBatch> batches)
        {
            Name = name;
            Batches = Sanitise(batches);
            Visible = true;
        }

        public string Name { get; }

        public IReadOnlyList<RenderBatch> Batches { get; private set; }

        public bool Visible { get; internal set; }

        internal void Replace(IEnumerable<RenderBatch> batches) => Batches = Sanitise(batches);

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
            $"{Name}: {Batches.Count} batch(es){(Visible ? string.Empty : ", hidden")}";
    }
}
