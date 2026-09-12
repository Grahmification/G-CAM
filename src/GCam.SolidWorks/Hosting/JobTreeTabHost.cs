using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using GCam.UI.Views;

namespace GCam.SolidWorks.Hosting
{
    /// <summary>
    /// WinForms shell that lets SOLIDWORKS host the WPF <see cref="JobTreeView"/>
    /// in a FeatureManager tab.
    /// </summary>
    /// <remarks>
    /// ICommandManager aside, the FeatureManager pane is the one place SOLIDWORKS
    /// will not take a .NET control directly: IModelViewManager::CreateFeatureMgrControl4
    /// takes a CLSID or ProgID and activates it as an ActiveX control. So the chain is
    ///
    ///     SOLIDWORKS -> (ActiveX) JobTreeTabHost -> ElementHost -> WPF JobTreeView
    ///
    /// Consequences worth knowing:
    ///
    /// * This assembly must be registered with regasm, not just GCam.AddIn.dll.
    ///   The build does both; deploy/register.cmd does both.
    /// * The ProgId below is a published contract - GCamAddin passes this exact
    ///   string to CreateFeatureMgrControl4. Changing one without the other leaves
    ///   the tab silently missing.
    /// * ClassInterfaceType.AutoDual is what makes the control activatable here.
    /// </remarks>
    [ComVisible(true)]
    [Guid("f1d4dd4d-1992-427c-b840-402a5db1797f")]
    [ProgId(ProgIdValue)]
    [ClassInterface(ClassInterfaceType.AutoDual)]
    public class JobTreeTabHost : UserControl
    {
        /// <summary>ProgID SOLIDWORKS activates. Must match the [ProgId] attribute.</summary>
        public const string ProgIdValue = "GCam.SolidWorks.JobTreeTabHost";

        /// <summary>
        /// Entry point 6. SOLIDWORKS activates this through COM, so an exception here
        /// would surface as "the tab simply did not appear" with nothing explaining why.
        /// On failure the tab still opens, showing the error instead of the tree.
        /// </summary>
        public JobTreeTabHost()
        {
            try
            {
                Controls.Add(new ElementHost
                {
                    Dock = DockStyle.Fill,
                    Child = new JobTreeView(),
                });
            }
            catch (Exception ex)
            {
                // No ErrorHandler here: this object is constructed by COM, not by the
                // composition root, so it has no injected dependencies.
                Controls.Add(new Label
                {
                    Dock = DockStyle.Fill,
                    Padding = new Padding(10),
                    Text = "G-CAM could not load this panel." + Environment.NewLine +
                           Environment.NewLine + ex.Message,
                });
            }
        }
    }
}
