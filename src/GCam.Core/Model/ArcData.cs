using GCam.Core.Geometry.Primitives;

namespace GCam.Core.Model
{
    /// <summary>
    /// The plane an arc turns in, in the order G-code names them.
    /// </summary>
    /// <remarks>
    /// Explicit numbers matching G17/G18/G19 so a post can map them without a table, and
    /// because they are written into the document.
    /// </remarks>
    public enum ArcPlane
    {
        /// <summary>G17. Every arc a 3-axis contour makes, helical ramps included.</summary>
        XY = 17,

        /// <summary>G18.</summary>
        ZX = 18,

        /// <summary>G19.</summary>
        YZ = 19,
    }

    /// <summary>
    /// What turns a straight move into an arc: where the centre is and which way round.
    /// </summary>
    /// <remarks>
    /// Arcs are kept as arcs rather than tessellated into short segments, because a post
    /// emits G2/G3 and a machine runs an arc better than it runs a thousand chords.
    /// Whatever needs points - drawing, simulation - tessellates its own copy to whatever
    /// tolerance it needs.
    ///
    /// The start point is the previous move's end, so it is not stored here. Immutable for
    /// the same reason <see cref="Move"/> is.
    /// </remarks>
    public sealed class ArcData
    {
        public ArcData(Vec3 centre, bool clockwise, ArcPlane plane = ArcPlane.XY)
        {
            Centre = centre;
            Clockwise = clockwise;
            Plane = plane;
        }

        /// <summary>Millimetres, in the operation's frame.</summary>
        public Vec3 Centre { get; }

        /// <summary>Clockwise seen from the positive side of the plane's normal. G2.</summary>
        public bool Clockwise { get; }

        public ArcPlane Plane { get; }

        /// <summary>The plane's normal, which the turn direction is measured about.</summary>
        public Vec3 Normal
        {
            get
            {
                switch (Plane)
                {
                    case ArcPlane.ZX: return new Vec3(0, 1, 0);
                    case ArcPlane.YZ: return new Vec3(1, 0, 0);
                    default: return new Vec3(0, 0, 1);
                }
            }
        }

        public override string ToString() =>
            $"{Plane} {(Clockwise ? "CW" : "CCW")} about {Centre}";
    }
}
