using System.Collections.Generic;

namespace GCam.Core.Strategies.Shared
{
    /// <summary>
    /// A strategy whose geometry is a list of contours.
    /// </summary>
    /// <remarks>
    /// Selections are typed per strategy, which is right for the strategy code and awkward
    /// for anything that has to store them. This is the seam: a serialiser asks whether
    /// settings own contours, rather than knowing about `Contour2dSettings` by name.
    /// Facing and adaptive clearing will implement it too; drilling will have its own,
    /// because a hole is not a contour.
    ///
    /// Deliberately not a general "IHaveGeometry" with an untyped list. That would let a
    /// drill operation store a flat face and defer the complaint to generation time, which
    /// is the thing typing the selections was for.
    /// </remarks>
    public interface IContourSelectionOwner
    {
        /// <summary>The picked entities and the modifiers they were picked with.</summary>
        List<ContourSelection> Contours { get; }
    }
}
