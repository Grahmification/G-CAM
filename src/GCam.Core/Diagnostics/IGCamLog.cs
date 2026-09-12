using System;

namespace GCam.Core.Diagnostics
{
    /// <summary>
    /// Logging abstraction. Declared in Core so the domain can log without Core
    /// taking a dependency on any logging library; GCam.AddIn supplies the Serilog
    /// implementation at startup.
    /// </summary>
    /// <remarks>
    /// Message templates use positional placeholders - "loaded {0} in {1}ms" - which
    /// both string.Format and Serilog understand.
    /// </remarks>
    public interface IGCamLog
    {
        void Debug(string message, params object[] args);

        void Info(string message, params object[] args);

        void Warn(string message, params object[] args);

        void Error(Exception ex, string message, params object[] args);
    }
}
