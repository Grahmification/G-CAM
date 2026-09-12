namespace GCam.Core.Tooling
{
    /// <summary>
    /// The cutter shapes G-CAM understands.
    /// </summary>
    /// <remarks>
    /// The type drives which <see cref="ToolGeometry"/> parameters are meaningful and
    /// which silhouette <see cref="CutterProfile"/> builds. It is also what posting
    /// needs in order to emit a drilling cycle rather than a milling move, which is
    /// why the profile alone is not enough.
    ///
    /// These names are written into the library XML, so renaming one is a file format
    /// change. Add new members at the end.
    /// </remarks>
    public enum ToolType
    {
        /// <summary>Square end. Corner radius is zero.</summary>
        FlatEndMill = 0,

        /// <summary>Hemispherical end. Corner radius equals half the diameter.</summary>
        BallEndMill = 1,

        /// <summary>Square end with a corner radius between zero and half the diameter.</summary>
        BullNoseEndMill = 2,

        /// <summary>Conical point described by an included tip angle, on a cylindrical body.</summary>
        Drill = 3,

        /// <summary>
        /// Conical cutter for chamfering and spotting. May have a small flat at the tip.
        /// </summary>
        ChamferMill = 4,
    }
}
