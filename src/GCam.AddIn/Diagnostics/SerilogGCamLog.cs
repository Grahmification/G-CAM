using System;
using GCam.Core.Diagnostics;
using Serilog;

namespace GCam.AddIn.Diagnostics
{
    /// <summary>
    /// Adapts Serilog to Core's <see cref="IGCamLog"/>, so nothing below GCam.AddIn
    /// has to know which logging library is in use.
    /// </summary>
    internal sealed class SerilogGCamLog : IGCamLog
    {
        private readonly ILogger _logger;

        public SerilogGCamLog(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void Debug(string message, params object[] args) => _logger.Debug(message, args);

        public void Info(string message, params object[] args) => _logger.Information(message, args);

        public void Warn(string message, params object[] args) => _logger.Warning(message, args);

        public void Error(Exception ex, string message, params object[] args) => _logger.Error(ex, message, args);
    }
}
