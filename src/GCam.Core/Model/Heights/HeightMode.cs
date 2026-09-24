namespace GCam.Core.Model.Heights
{
    /// <summary>
    /// What a height is measured from, before its offset is applied.
    /// </summary>
    /// <remarks>
    /// HSMWorks' model, with the list trimmed to what 3-axis work uses. Every mode here
    /// costs a resolver and a test, so the rest are deliberately absent until something
    /// asks for them - see docs/design/operations.md.
    ///
    /// Explicit numbers: these are written into the document, so a member added later
    /// must not renumber the existing ones.
    /// </remarks>
    public enum HeightMode
    {
        /// <summary>The top of the stock box. The usual datum for clearance and retract.</summary>
        FromStockTop = 0,

        /// <summary>The bottom of the stock box.</summary>
        FromStockBottom = 1,

        /// <summary>The highest point of the model.</summary>
        FromModelTop = 2,

        /// <summary>The lowest point of the model. The usual datum for a through cut.</summary>
        FromModelBottom = 3,

        /// <summary>
        /// Z zero in the operation's own frame - the job origin unless the operation
        /// overrides it. The offset is then simply the height.
        /// </summary>
        FromJobOrigin = 4,

        /// <summary>
        /// A picked face, edge or vertex. The only mode that needs
        /// <see cref="HeightSetting.Reference"/>.
        /// </summary>
        FromSelection = 5,

        /// <summary>
        /// The Z of the contour being cut, so one operation cuts each of its chains at a
        /// height of its own. For Top and Bottom only: clearance, retract and feed are
        /// crossed between contours, and have to be one plane for all of them.
        /// </summary>
        FromContour = 6,
    }
}
