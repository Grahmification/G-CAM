using System;

namespace GCam.Core.Diagnostics
{
    /// <summary>
    /// An expected failure with a message written for the user, not for a developer.
    /// </summary>
    /// <remarks>
    /// Throw this when the user has asked for something that cannot be done and the
    /// reason is understandable to them - a tool too large for a pocket, a face that
    /// is not planar, a missing tool library. It is presented as a plain message with
    /// no stack trace, and logged at Info rather than Error.
    ///
    /// Do NOT use it for defects. A null reference or an index out of range is a bug,
    /// and dressing it up as a user error hides it.
    /// </remarks>
    [Serializable]
    public class GCamUserException : Exception
    {
        public GCamUserException(string message)
            : base(message)
        {
        }

        public GCamUserException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}
