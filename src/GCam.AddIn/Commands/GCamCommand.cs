namespace GCam.AddIn.Commands
{
    /// <summary>
    /// Command IDs for the G-CAM CommandGroup.
    /// </summary>
    /// <remarks>
    /// The integer values are the image index of each button inside the icon strip
    /// (Resources/icons/buttons*.png), so the order here must match the order of
    /// BUTTONS in tools/make-placeholder-icons.py. Adding a button means adding it
    /// in both places and regenerating the strips.
    ///
    /// It is also the order the buttons appear in, on the toolbar and on the ribbon tab -
    /// <c>AddCommandTab</c> reads the values in numeric order. So inserting one at the
    /// front, as Generate is, renumbers every button after it and shifts the whole
    /// strip along with them. Bump <c>CommandGroupId</c> when that happens.
    /// </remarks>
    public enum GCamCommand
    {
        GenerateSelected = 0,
        NewJob = 1,
        NewOperation = 2,
        ToolLibrary = 3,
        PostProcess = 4,
        Simulate = 5,
    }
}
