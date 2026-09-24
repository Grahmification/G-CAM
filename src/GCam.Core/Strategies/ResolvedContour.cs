using System;
using GCam.Core.Geometry.Primitives;
using GCam.Core.Model.Heights;

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
        public ResolvedContour(Polyline path, bool reversed = false, ResolvedHeights heights = null)
        {
            Path = path ?? throw new ArgumentNullException(nameof(path));
            Reversed = reversed;
            Heights = heights;
        }

        /// <summary>Millimetres, in the operation's frame. Open or closed.</summary>
        public Polyline Path { get; }

        /// <summary>
        /// The Z this contour lies at, which is what a height measured from the contour is
        /// measured from.
        /// </summary>
        /// <remarks>
        /// Read off the first point because extraction for 2D work flattens every chain
        /// onto one Z, so any point would give the same answer. A contour built by hand
        /// that is not flat gets the Z it starts at.
        /// </remarks>
        public double Level => Path.Count == 0 ? 0 : Path.Points[0].Z;

        /// <summary>
        /// The user asked for this contour the other way round, which puts the cutter on
        /// its other side - outside instead of inside for a closed profile, and the other
        /// hand of an open one.
        /// </summary>
        public bool Reversed { get; }

        /// <summary>
        /// This contour's own heights, when the operation's cutting heights are measured
        /// from the contour; null when they are the operation's and
        /// <see cref="GenerationContext.Heights"/> is the answer.
        /// </summary>
        /// <remarks>
        /// **Here rather than in a list beside the contours**, although it mixes geometry
        /// with heights on one type. A parallel list can fall out of step with the one it
        /// shadows - the failure <see cref="Geometry.Chaining.ChainWithSources"/> exists to
        /// avoid - and a contour dropped or extended would have to remember to drop or
        /// carry its heights with it. On the contour, it cannot be forgotten.
        /// </remarks>
        public ResolvedHeights Heights { get; }

        /// <summary>The same contour, with heights resolved for it.</summary>
        public ResolvedContour WithHeights(ResolvedHeights heights) =>
            new ResolvedContour(Path, Reversed, heights);

        public override string ToString() =>
            Reversed ? Path + " (reversed)" : Path.ToString();
    }
}
