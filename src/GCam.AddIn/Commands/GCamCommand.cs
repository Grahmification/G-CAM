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
    /// </remarks>
    public enum GCamCommand
    {
        NewJob = 0,
        NewOperation = 1,
        ToolLibrary = 2,
        PostProcess = 3,
        Simulate = 4,
    }
}
