using System.Collections.Generic;

namespace GCam.Core.Model
{
    /// <summary>
    /// Which coordinate system an operation's toolpath is expressed in.
    /// </summary>
    /// <remarks>
    /// Inherits the job's unless it is told not to. HSMWorks carries a full per-operation
    /// override - 11 of the 47 parameters common to every strategy in their export are
    /// this - and G-CAM models it because it is the groundwork for 3+2.
    ///
    /// **What it cannot do yet is post.** A 3-axis machine cannot reorient between
    /// operations, so an override whose Z axis is not parallel to the job's describes a
    /// setup nobody can cut on three axes. That check needs both resolved transforms, so
    /// it belongs where the transforms are known - see <see cref="Validate"/> - and it
    /// lands with the extraction work rather than here.
    /// </remarks>
    public sealed class OperationFrame
    {
        /// <summary>
        /// True - the default - means the job's coordinate system and work offset govern.
        /// </summary>
        public bool InheritFromJob { get; set; } = true;

        /// <summary>
        /// The coordinate system feature this operation uses, when it does not inherit.
        /// Null means the part origin.
        /// </summary>
        /// <remarks>
        /// A name rather than a persistent reference, matching
        /// <see cref="Job.CoordinateSystem"/>: both are
        /// persistent reference ids when persistence lands. Unlike the geometry an
        /// operation selects, which uses <see cref="GeometryRef"/> from the start, a
        /// coordinate system is a feature and is resolved by the same code that already
        /// resolves the job's.
        /// </remarks>
        public GeometryRef CoordinateSystem { get; set; }

        /// <summary>What the coordinate system box shows.</summary>
        public string DisplayName =>
            InheritFromJob
                ? "From job"
                : CoordinateSystem == null || CoordinateSystem.IsEmpty
                    ? "Part origin"
                    : CoordinateSystem.ToString();

        /// <summary>
        /// Problems visible without resolving anything.
        /// </summary>
        /// <remarks>
        /// Deliberately thin. The question worth asking about an override - is its Z
        /// parallel to the job's, so a 3-axis post can emit it - needs the two resolved
        /// transforms, which Core cannot obtain. That check runs when the operation is
        /// generated, and refuses at post time rather than quietly producing a part nobody
        /// can cut.
        /// </remarks>
        public IReadOnlyList<string> Validate() => new string[0];

        public OperationFrame Clone()
        {
            var copy = (OperationFrame)MemberwiseClone();
            copy.CoordinateSystem = CoordinateSystem?.Clone();
            return copy;
        }

        public override string ToString() => DisplayName;
    }
}
