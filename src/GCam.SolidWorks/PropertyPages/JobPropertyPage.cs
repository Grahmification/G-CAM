using GCam.Core.Diagnostics;
using SolidWorks.Interop.sldworks;

namespace GCam.SolidWorks.PropertyPages
{
    /// <summary>
    /// Editing surface for a job - the stock, the coordinate system and the machine the
    /// setups underneath it inherit. A shell: it appears, it takes OK and Cancel, and it
    /// carries no settings yet.
    /// </summary>
    public sealed class JobPropertyPage : GCamPropertyPage
    {
        // Control IDs are resource ids scoped to this page and referenced nowhere else,
        // so they stay private here rather than becoming shared constants.
        private const int GroupJob = 1;
        private const int LabelPlaceholder = 2;

        public JobPropertyPage(SldWorks swApp, ErrorHandler errors, IGCamLog log)
            : base(swApp, errors, log)
        {
        }

        protected override string Title => "G-CAM Job";

        protected override string Message =>
            "A job owns the stock, the coordinate system and the machine. " +
            "Nothing is configured yet.";

        protected override void BuildControls(IPropertyManagerPage2 page)
        {
            var group = AddGroup(page, GroupJob, "Job");
            AddLabel(group, LabelPlaceholder, "Stock and coordinate system will appear here.");
        }
    }
}
