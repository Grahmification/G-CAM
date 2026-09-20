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
    /// The walk the two propagation flags describe is
    /// <see cref="GCam.Core.Geometry.EdgePropagation"/>.
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
        /// Follow tangentially continuous edges on from the picked one, forwards only.
        /// </summary>
        /// <remarks>
        /// On by default, because picking one edge of a filleted pocket and getting only
        /// that edge is never what anybody meant.
        /// </remarks>
        public bool PropagateTangent { get; set; } = true;

        /// <summary>
        /// Follow joining edges that lie at the picked edge's own height, both ways.
        /// </summary>
        /// <remarks>
        /// Also opens the backward end to tangential propagation - see
        /// <see cref="GCam.Core.Geometry.EdgePropagation"/>.
        /// </remarks>
        public bool PropagateAlongZ { get; set; } = true;

        /// <summary>
        /// Walk the chain the other way round, which turns its arrow round and puts the
        /// cutter on its other side.
        /// </summary>
        /// <remarks>
        /// The direction of travel is this flag's alone; which side of the line the cutter
        /// runs on follows from it and the climb/conventional choice together. It is also
        /// what decides which way propagation runs, since that follows the arrow.
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
