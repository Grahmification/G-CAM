namespace GCam.Core.Model.Heights
{
    /// <summary>
    /// Which of an operation's five heights is meant.
    /// </summary>
    /// <remarks>
    /// <see cref="OperationHeights"/> names them as properties, which is right for a thing
    /// that always has all five. This exists for the cases that have to point at one of
    /// them from outside - the Heights tab saying which plane is being edited - where a
    /// property reference would be a reference to the wrong operation's copy.
    /// </remarks>
    public enum HeightKind
    {
        Clearance = 0,
        Retract = 1,
        Feed = 2,
        Top = 3,
        Bottom = 4,
    }
}
