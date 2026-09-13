using GCam.Core.Model;

namespace GCam.Core.Strategies.Shared
{
    /// <summary>
    /// One picked entity plus how far the selection is meant to run from it.
    /// </summary>
    /// <remarks>
    /// **Intent, not a resolved list.** What is stored is the entity someone picked and
    /// the modifiers they picked it with; the chain is worked out again each time the
    /// operation is generated. The alternative - letting SOLIDWORKS expand the selection
    /// at pick time and storing the forty edges that come back - was rejected:
    ///
    /// - HSMWorks does it this way. `chainingTolerance` is a parameter on contouring,
    ///   facing and adaptive clearing in their own templates, and a tolerance for chaining
    ///   only exists if the chaining happens in the CAM engine rather than in the CAD
    ///   selection.
    /// - One stored reference survives a model edit that would break forty.
    /// - The property page can show what was chosen - "this edge, tangentially" - instead
    ///   of a list of forty edges nobody picked individually.
    ///
    /// The cost is real and worth stating: a model edit can silently change how far the
    /// chain runs, by making two edges tangent that were not. That is covered by
    /// staleness - a SOLIDWORKS rebuild marks every operation stale, so the path is
    /// regenerated and seen before it can post without a warning.
    ///
    /// Nothing here is honoured yet. The flags are stored and round-tripped; the
    /// propagation itself lands with the contour strategy and the geometry extraction it
    /// needs. The shape exists now because persistence will freeze it into saved parts.
    /// </remarks>
    public sealed class ContourSelection
    {
        public ContourSelection()
        {
        }

        public ContourSelection(GeometryRef entity)
        {
            Entity = entity;
        }

        /// <summary>The entity that was actually picked. The chain starts here.</summary>
        public GeometryRef Entity { get; set; }

        /// <summary>
        /// Follow tangentially continuous edges away from the picked one.
        /// </summary>
        /// <remarks>
        /// On by default, because picking one edge of a filleted pocket and getting only
        /// that edge is never what anybody meant.
        /// </remarks>
        public bool PropagateTangent { get; set; } = true;

        /// <summary>
        /// Take the same profile at every Z it occurs at, rather than only the one picked.
        /// </summary>
        public bool PropagateAlongZ { get; set; }

        /// <summary>
        /// Follow the chain the other way round, which is what puts the cutter on the
        /// other side of it.
        /// </summary>
        /// <remarks>
        /// One flag rather than an inside/outside setting, because the side is a
        /// consequence of the direction the chain runs in and the climb/conventional
        /// choice - the same thing HSMWorks' little arrow toggles.
        /// </remarks>
        public bool Reversed { get; set; }

        /// <summary>True when nothing has been picked.</summary>
        public bool IsEmpty => Entity == null || Entity.IsEmpty;

        public ContourSelection Clone()
        {
            return new ContourSelection
            {
                Entity = Entity?.Clone(),
                PropagateTangent = PropagateTangent,
                PropagateAlongZ = PropagateAlongZ,
                Reversed = Reversed,
            };
        }

        public override string ToString() => Entity?.ToString() ?? "Nothing selected";
    }
}
