using System;

namespace GCam.Core.Diagnostics
{
    /// <summary>
    /// Discards everything. The default for tests, and the fallback if logging
    /// fails to initialise - so nothing has to null-check a log.
    /// </summary>
    public sealed class NullLog : IGCamLog
    {
        public static readonly NullLog Instance = new NullLog();

        public void Debug(string message, params object[] args) { }

        public void Info(string message, params object[] args) { }

        public void Warn(string message, params object[] args) { }

        public void Error(Exception ex, string message, params object[] args) { }
    }
}
