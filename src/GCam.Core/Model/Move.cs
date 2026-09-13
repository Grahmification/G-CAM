using GCam.Core.Geometry.Primitives;

namespace GCam.Core.Model
{
    /// <summary>
    /// One move of the tool: where it ends up, how fast, and what for.
    /// </summary>
    /// <remarks>
    /// **A move stores its destination, not its start.** The tool is wherever the previous
    /// move left it, which is how G-code and every CAM canonical form work - and it means
    /// a move cannot disagree with the one before it about where the tool is.
    ///
    /// Millimetres, in the operation's frame. Feeds in mm/min.
    ///
    /// Immutable, for the reason <see cref="Rendering.RenderBatch"/> gives: a toolpath is
    /// cached and drawn, and something that can be edited underneath a cache has to be
    /// watched. Build a new one instead.
    /// </remarks>
    public sealed class Move
    {
        public Move(MoveKind kind, Vec3 end, double feed = 0, ArcData arc = null)
        {
            Kind = kind;
            End = end;
            Feed = feed;
            Arc = arc;
        }

        public MoveKind Kind { get; }

        /// <summary>Where the tool ends up, mm, in the operation's frame.</summary>
        public Vec3 End { get; }

        /// <summary>mm/min. Zero for a rapid, which runs at whatever the machine does.</summary>
        public double Feed { get; }

        /// <summary>Null for a straight move.</summary>
        public ArcData Arc { get; }

        public bool IsArc => Arc != null;

        /// <summary>True for everything except a rapid - anything that is in the material.</summary>
        public bool IsFeedMove => Kind != MoveKind.Rapid;

        public static Move Rapid(Vec3 end) => new Move(MoveKind.Rapid, end);

        public static Move Cut(Vec3 end, double feed) => new Move(MoveKind.Cutting, end, feed);

        public static Move Plunge(Vec3 end, double feed) => new Move(MoveKind.Plunge, end, feed);

        public static Move Retract(Vec3 end, double feed) => new Move(MoveKind.Retract, end, feed);

        public static Move Lead(Vec3 end, double feed, ArcData arc = null) =>
            new Move(MoveKind.Lead, end, feed, arc);

        public static Move Link(Vec3 end, double feed) => new Move(MoveKind.Link, end, feed);

        /// <summary>A cutting move that turns about a centre rather than running straight.</summary>
        public static Move CutArc(Vec3 end, double feed, Vec3 centre, bool clockwise,
            ArcPlane plane = ArcPlane.XY) =>
            new Move(MoveKind.Cutting, end, feed, new ArcData(centre, clockwise, plane));

        public override string ToString() =>
            $"{Kind} to {End}" + (IsArc ? " (arc)" : string.Empty);
    }
}
