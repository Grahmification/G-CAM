using System;

namespace GCam.Core.Diagnostics
{
    /// <summary>
    /// Shows nothing. Used by tests, and during the earliest part of add-in startup
    /// before a real presenter exists - failures still reach the log.
    /// </summary>
    public sealed class NullErrorPresenter : IErrorPresenter
    {
        public static readonly NullErrorPresenter Instance = new NullErrorPresenter();

        public void ShowError(Exception ex, string context) { }

        public void ShowUserError(string message) { }
    }
}
