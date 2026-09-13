using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using GCam.AddIn.Composition;
using GCam.Core.Diagnostics;
using GCam.Core.Settings;
using GCam.SolidWorks.Hosting;
using GCam.UI.Diagnostics;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;

// SolidWorks.Interop.sldworks declares its own Environment type, which collides with
// System.Environment. Alias rather than fully qualifying every use.
using SysEnv = System.Environment;

namespace GCam.AddIn
{
    /// <summary>
    /// Add-in lifetime: everything SOLIDWORKS creates on connect is torn down on
    /// disconnect, in reverse order.
    /// </summary>
    public partial class GCamAddin : ISwAddin
    {
        private SldWorks _swApp;
        private int _addinID = -1;
        private ICommandManager _iCmdMgr;
        private JobTreeTabs _jobTreeTabs;

        private IGCamLog _log = NullLog.Instance;
        private ErrorHandler _errors;
        private IGCamSettings _settings;

        /// <summary>
        /// Installs the dependency resolver before anything else can run.
        /// </summary>
        /// <remarks>
        /// This has to happen in a type initialiser rather than at the top of
        /// ConnectToSW. The JIT resolves every type a method mentions when that method
        /// is first entered, so by the time a line inside ConnectToSW could execute, a
        /// missing dependency has already thrown. The static constructor runs when COM
        /// creates the instance, which is early enough.
        ///
        /// See docs/solidworks-api/addin-dependencies.md.
        /// </remarks>
        static GCamAddin()
        {
            AssemblyResolver.Install();
        }

        /// <summary>
        /// Entry point 1. An exception escaping here makes SOLIDWORKS unload the add-in
        /// with no message at all, so nothing is allowed to escape.
        /// </summary>
        public bool ConnectToSW(object ThisSW, int Cookie)
        {
            // Bootstrap: until diagnostics exist there is nothing to report failures
            // with, so this first stretch gets its own last-resort handling.
            try
            {
                _swApp = ThisSW as SldWorks;
                _addinID = Cookie;
                InitialiseDiagnostics();
            }
            catch (Exception ex)
            {
                ReportBootstrapFailure(ex);
                return false;
            }

            // Each subsystem is wrapped separately: a broken tree tab should still
            // leave a working toolbar.
            try
            {
                _iCmdMgr = _swApp.GetCommandManager(Cookie);
                BuildCommandManager();
            }
            catch (Exception ex)
            {
                _errors.Handle(ex, nameof(BuildCommandManager));
            }

            try
            {
                StartJobTreeTabs();
            }
            catch (Exception ex)
            {
                _errors.Handle(ex, nameof(StartJobTreeTabs));
            }

            _log.Info("G-CAM connected");
            return true;
        }

        /// <summary>
        /// Entry point 2. Teardown never shows a dialog - blocking an unload behind a
        /// modal window strands SOLIDWORKS.
        /// </summary>
        public bool DisconnectFromSW()
        {
            try
            {
                StopJobTreeTabs();
            }
            catch (Exception ex)
            {
                _errors?.Handle(ex, nameof(StopJobTreeTabs), quiet: true);
            }

            try
            {
                RemoveCommandManager();
            }
            catch (Exception ex)
            {
                _errors?.Handle(ex, nameof(RemoveCommandManager), quiet: true);
            }

            try
            {
                if (_iCmdMgr != null)
                {
                    Marshal.ReleaseComObject(_iCmdMgr);
                    _iCmdMgr = null;
                }

                if (_swApp != null)
                {
                    Marshal.ReleaseComObject(_swApp);
                    _swApp = null;
                }
            }
            catch (Exception ex)
            {
                _errors?.Handle(ex, "ReleaseComObjects", quiet: true);
            }

            _log.Info("=== G-CAM session end ===");

            // Forces the managed pointers held by the interop wrappers to be released
            // so SOLIDWORKS can unload the add-in cleanly.
            GC.Collect();
            GC.WaitForPendingFinalizers();

            return true;
        }

        /// <summary>
        /// Builds the log, the error presenter and the handler that ties them together.
        /// Must run before anything that could fail.
        /// </summary>
        private void InitialiseDiagnostics()
        {
            _log = LoggingSetup.Create();
            LoggingSetup.WriteSessionHeader(_log, SafeSolidWorksVersion());

            // Constructed on the SOLIDWORKS main thread so it captures the right
            // dispatcher to marshal back to.
            var presenter = new WpfErrorPresenter(MainWindowHandle, LoggingSetup.LogDirectory);
            _errors = new ErrorHandler(_log, presenter);

            // Load never throws: missing or corrupt settings fall back to defaults
            // rather than stopping the add-in.
            _settings = XmlSettingsStore.Load(log: _log);
        }

        private string SafeSolidWorksVersion()
        {
            try
            {
                return _swApp?.RevisionNumber();
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Reports a failure that happened before logging existed. Best effort only -
        /// without this the user sees the add-in checkbox silently un-tick.
        /// </summary>
        private void ReportBootstrapFailure(Exception ex)
        {
            try
            {
                string dir = Path.Combine(
                    SysEnv.GetFolderPath(SysEnv.SpecialFolder.LocalApplicationData),
                    "G-CAM", "logs");
                Directory.CreateDirectory(dir);
                File.AppendAllText(
                    Path.Combine(dir, "startup-failure.log"),
                    DateTime.Now.ToString("u") + SysEnv.NewLine + ex + SysEnv.NewLine + SysEnv.NewLine);
            }
            catch (Exception)
            {
                // Nowhere left to write. Fall through to the message box.
            }

            try
            {
                _swApp?.SendMsgToUser2(
                    "G-CAM failed to start:" + SysEnv.NewLine + ex.Message,
                    (int)swMessageBoxIcon_e.swMbStop,
                    (int)swMessageBoxBtn_e.swMbOk);
            }
            catch (Exception)
            {
                // If even this fails there is genuinely nothing further to try.
            }
        }

        /// <summary>
        /// Starts keeping a G-CAM tab in the Manager Pane of every open part.
        /// </summary>
        /// <remarks>
        /// The tab belongs to a document rather than to the application, so this cannot
        /// be a single call at connect time - see <see cref="JobTreeTabs"/>, which owns
        /// the document notifications and the per-document tabs.
        ///
        /// CreateFeatureMgrControl4 activates <see cref="JobTreeTabHost"/> by ProgID as
        /// an ActiveX control, so GCam.SolidWorks.dll must be COM-registered for the tab
        /// to appear. If it silently fails to show, that registration is the first thing
        /// to check.
        /// </remarks>
        private void StartJobTreeTabs()
        {
            // Three bitmaps, one per resolution band. SOLIDWORKS picks by DPI and reads
            // them from disk, so they ship beside the assembly.
            string dir = Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
                "Resources", "icons");

            string[] tabIcons =
            {
                Path.Combine(dir, "main20.png"),
                Path.Combine(dir, "main32.png"),
                Path.Combine(dir, "main40.png"),
            };

            _jobTreeTabs = new JobTreeTabs(_swApp, tabIcons, _errors, _log);
            _jobTreeTabs.Start();
        }

        private void StopJobTreeTabs()
        {
            _jobTreeTabs?.Dispose();
            _jobTreeTabs = null;
        }
    }
}
