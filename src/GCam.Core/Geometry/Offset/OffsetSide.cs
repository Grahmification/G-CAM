namespace GCam.Core.Geometry.Offset
{
    /// <summary>
    /// Which side of an open path an offset runs along, looking along the path.
    /// </summary>
    /// <remarks>
    /// A closed contour needs no such thing: it has an inside, so which way round it runs
    /// already says which side the cutter lands on, and
    /// <see cref="IContourOffsetter.Offset"/> works that way. An open path has no inside,
    /// so the side has to be said out loud.
    ///
    /// Left is the direction a quarter turn counter-clockwise from travel, seen from +Z -
    /// the same convention as G41/G42 cutter compensation, which is what this eventually
    /// posts as.
    /// </remarks>
    public enum OffsetSide
    {
        Left = 0,
        Right = 1,
    }
}
