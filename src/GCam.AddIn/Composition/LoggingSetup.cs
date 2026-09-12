using System;
using System.IO;
using GCam.Core.Diagnostics;
using Serilog;

namespace GCam.AddIn.Composition
{
    /// <summary>
    /// Creates the file log. Called once, first thing in ConnectToSW.
    /// </summary>
    internal static class LoggingSetup
    {
        /// <summary>
        /// %LOCALAPPDATA%\G-CAM\logs - per-user and always writable, unlike the
        /// add-in's own folder, which may sit under Program Files.
        /// </summary>
        public static string LogDirectory =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "G-CAM",
                "logs");

        /// <summary>
        /// Builds the logger. Never throws: if the log file cannot be opened, the
        /// add-in still loads and runs, it just loses logging.
        /// </summary>
        public static IGCamLog Create()
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);

                ILogger logger = new LoggerConfiguration()
                    .MinimumLevel.Debug()
                    .WriteTo.File(
                        Path.Combine(LogDirectory, "gcam-.log"),
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 7,
                        fileSizeLimitBytes: 10L * 1024 * 1024,
                        rollOnFileSizeLimit: true,
                        // Two SOLIDWORKS instances can run at once; without this the
                        // second one fails to open the file and logs nothing.
                        shared: true,
                        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                    .CreateLogger();

                return new Diagnostics.SerilogGCamLog(logger);
            }
            catch (Exception)
            {
                // Logging is diagnostics, not a feature. Losing it must not stop the
                // add-in loading - and there is nowhere to report this failure to.
                return NullLog.Instance;
            }
        }

        /// <summary>
        /// Records what a log reader needs to know before anything else: which build
        /// of G-CAM, which SOLIDWORKS, and a marker separating this run from the last.
        /// </summary>
        public static void WriteSessionHeader(IGCamLog log, string solidWorksVersion)
        {
            log.Info("=== G-CAM session start ===");
            log.Info("G-CAM      : {0}", typeof(LoggingSetup).Assembly.GetName().Version);
            log.Info("SOLIDWORKS : {0}", solidWorksVersion ?? "unknown");
            // Environment.ProcessId is .NET 5+; on .NET Framework it comes from Process.
            using (var process = System.Diagnostics.Process.GetCurrentProcess())
            {
                log.Info("Process    : {0} ({1}-bit)", process.Id, IntPtr.Size * 8);
            }

            log.Info("User       : {0}", Environment.UserName);
        }
    }
}
