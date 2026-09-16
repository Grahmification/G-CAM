namespace GCam.Core.Model
{
    /// <summary>
    /// What the job tree marks an operation's icon with.
    /// </summary>
    /// <remarks>
    /// Three values rather than one per <see cref="OperationState"/>, because the badge
    /// answers a narrower question than the state does: <i>can I believe this toolpath?</i>
    /// Five of the six states answer it with yes, no, or "read the message", and an icon
    /// that tried to distinguish all six would be decoded rather than glanced at.
    ///
    /// The state itself is still what the tooltip says, so nothing is lost - see
    /// <see cref="OperationStatus"/>.
    /// </remarks>
    public enum OperationBadge
    {
        /// <summary>Nothing drawn. The toolpath matches its inputs as far as anything knows.</summary>
        None = 0,

        /// <summary>
        /// There is a usable toolpath, but something about it is worth reading.
        /// </summary>
        Warning = 1,

        /// <summary>
        /// No toolpath that can be trusted: never generated, out of date, failed, or
        /// still running.
        /// </summary>
        Error = 2,
    }
}
