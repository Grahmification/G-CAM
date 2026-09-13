namespace GCam.Core.Model
{
    /// <summary>
    /// Where an operation stands: whether it has a toolpath, and whether that toolpath
    /// can be believed.
    /// </summary>
    /// <remarks>
    /// Shown as an icon in the job tree with the message in the tooltip. Generation
    /// failures are state rather than dialogs - generating a whole job would otherwise
    /// stop dead on the first failure, which is exactly when failures come in groups.
    ///
    /// Explicit numbers: written into the document.
    /// </remarks>
    public enum OperationState
    {
        /// <summary>Never generated, or generation was cancelled before it finished.</summary>
        NotGenerated = 0,

        /// <summary>In the queue or running.</summary>
        Generating = 1,

        /// <summary>Has a toolpath that matches its inputs as far as anything knows.</summary>
        Generated = 2,

        /// <summary>
        /// Has a toolpath, but something it was made from has changed since. Drawn dimmed;
        /// posting one warns.
        /// </summary>
        Stale = 3,

        /// <summary>
        /// Generated, with something worth reading - a selection that was dropped, a
        /// reference that moved.
        /// </summary>
        Warning = 4,

        /// <summary>
        /// Generation could not proceed. Any previous toolpath is kept, because a failed
        /// retry must never lose a path that was already proven on a machine.
        /// </summary>
        Failed = 5,
    }
}
