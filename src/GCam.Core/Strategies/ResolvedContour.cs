using System;
using GCam.Core.Geometry.Primitives;

namespace GCam.Core.Strategies
{
    /// <summary>
    /// One resolved contour: the tessellated curve, and the intent it was picked with.
    /// </summary>
    /// <remarks>
    /// **The flag has to travel with the geometry, and it cannot be applied before the
    /// strategy sees it.** Reversing a contour is how the user chooses which side of it the
    /// cutter runs on. For an open path that could be done by handing over an
    /// already-reversed <see cref="Polyline"/> - but for a closed one it could not, because
    /// <c>Contour2dStrategy</c> forces the orientation from the climb/conventional setting
    /// as the first thing it does, and that would quietly undo the reversal. So the
    /// strategy is told, and applies it last.
    ///
    /// Only the modifier that is honoured lives here. <c>ContourSelection</c> also stores
    /// tangent and Z propagation, which act during extraction, before this exists.
    /// </remarks>
    public sealed class ResolvedContour
    {
        public ResolvedContour(Polyline path, bool reversed = false)
        {
            Path = path ?? throw new ArgumentNullException(nameof(path));
            Reversed = reversed;
        }

        /// <summary>Millimetres, in the operation's frame. Open or closed.</summary>
        public Polyline Path { get; }

        /// <summary>
        /// The user asked for this contour the other way round, which puts the cutter on
        /// its other side - outside instead of inside for a closed profile, and the other
        /// hand of an open one.
        /// </summary>
        public bool Reversed { get; }

        public override string ToString() =>
            Reversed ? Path + " (reversed)" : Path.ToString();
    }
}
