using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swpublished;
using System;
using System.Runtime.InteropServices;

namespace G_CAM
{
    public partial class GCamAddin : ISwAddin
    {
        private SldWorks _swApp;
        private int _addinID = -1;
        private ICommandManager _iCmdMgr;

        public bool ConnectToSW(object ThisSW, int Cookie)
        {
            _swApp = ThisSW as SldWorks;
            _addinID = Cookie;

            //Setup command manager
            _iCmdMgr = _swApp.GetCommandManager(Cookie);

            _swApp.SendMsgToUser("Loaded Addin");
            return true;
        }
        public bool DisconnectFromSW()
        {
            Marshal.ReleaseComObject(_swApp);

            // Call GC.Collect() here in order to retrieve all managed code pointers 
            GC.Collect();
            GC.WaitForPendingFinalizers();

            return true;
        }
    }
}
