using System.Collections.Generic;
using GCam.Core.Geometry.Primitives;

namespace GCam.Core.Geometry
{
    /// <summary>
    /// What <see cref="EdgePropagation"/> needs to know about a model's edges.
    /// </summary>
    /// <remarks>
    /// Edges are identified by an index the implementation hands out, so the walk itself
    /// is arithmetic over a graph and can be tested without a model - the same split
    /// <see cref="Chaining"/> makes. <c>GCam.SolidWorks</c> answers these from
    /// <c>IEdge</c> and <c>IVertex</c>.
    ///
    /// Points are millimetres in the job's frame, like everything else a strategy sees.
    /// </remarks>
    public interface IEdgeTopology
    {
        Vec3 StartOf(int edge);

        Vec3 EndOf(int edge);

        /// <summary>
        /// The other edges meeting one end of this one, in no particular order.
        /// </summary>
        IReadOnlyList<int> Joining(int edge, bool atEnd);

        /// <summary>True when the two edges run smoothly into one another.</summary>
        bool AreTangent(int edge, int other);

        /// <summary>True when the whole edge lies flat at this Z.</summary>
        bool LiesAt(int edge, double z);
    }
}
