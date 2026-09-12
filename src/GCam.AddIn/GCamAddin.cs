using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using GCam.SolidWorks.Hosting;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;

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
        private FeatMgrView _jobTreeTab;

        public bool ConnectToSW(object ThisSW, int Cookie)
        {
            _swApp = ThisSW as SldWorks;
            _addinID = Cookie;

            _iCmdMgr = _swApp.GetCommandManager(Cookie);

            BuildCommandManager();
            CreateJobTreeTab();

            return true;
        }

        public bool DisconnectFromSW()
        {
            RemoveJobTreeTab();
            RemoveCommandManager();

            if (_iCmdMgr != null)
            {
                Marshal.ReleaseComObject(_iCmdMgr);
                _iCmdMgr = null;
            }

            Marshal.ReleaseComObject(_swApp);
            _swApp = null;

            // Forces the managed pointers held by the interop wrappers to be released
            // so SOLIDWORKS can unload the add-in cleanly.
            GC.Collect();
            GC.WaitForPendingFinalizers();

            return true;
        }

        /// <summary>
        /// Adds the G-CAM tab to the FeatureManager pane.
        /// </summary>
        /// <remarks>
        /// CreateFeatureMgrControl4 activates <see cref="JobTreeTabHost"/> by ProgID as
        /// an ActiveX control, so GCam.SolidWorks.dll must be COM-registered for the tab
        /// to appear. If it silently fails to show, that registration is the first thing
        /// to check.
        ///
        /// The tab is created against the active document; it is not global. Documents
        /// opened later need the same call, which is what the document-open event will
        /// be for once there is state worth showing.
        /// </remarks>
        private void CreateJobTreeTab()
        {
            var model = _swApp.ActiveDoc as ModelDoc2;
            if (model == null)
            {
                return;
            }

            // Three bitmaps, one per resolution band. SOLIDWORKS picks by DPI.
            string dir = Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
                "Resources", "icons");

            string[] tabIcons =
            {
                Path.Combine(dir, "main20.png"),
                Path.Combine(dir, "main32.png"),
                Path.Combine(dir, "main40.png"),
            };

            _jobTreeTab = model.ModelViewManager.CreateFeatureMgrControl4(
                tabIcons,
                JobTreeTabHost.ProgIdValue,
                string.Empty,
                "G-CAM",
                (int)swFeatMgrPane_e.swFeatMgrPaneBottom);
        }

        private void RemoveJobTreeTab()
        {
            if (_jobTreeTab == null)
            {
                return;
            }

            Marshal.ReleaseComObject(_jobTreeTab);
            _jobTreeTab = null;
        }
    }
}
