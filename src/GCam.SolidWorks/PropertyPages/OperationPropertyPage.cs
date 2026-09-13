using GCam.Core.Diagnostics;
using SolidWorks.Interop.sldworks;

namespace GCam.SolidWorks.PropertyPages
{
    /// <summary>
    /// Editing surface for a single operation - its strategy, tool, geometry and
    /// heights. A shell: it appears, it takes OK and Cancel, and it carries no settings
    /// yet.
    /// </summary>
    /// <remarks>
    /// One page per operation *type* is the likely end state, since a drill and a 2D
    /// contour share little beyond the tool. This one stands in for all of them until
    /// there is a strategy to specialise for.
    /// </remarks>
    public sealed class OperationPropertyPage : GCamPropertyPage
    {
        private const int GroupTool = 1;
        private const int GroupGeometry = 2;
        private const int LabelTool = 10;
        private const int LabelGeometry = 20;

        public OperationPropertyPage(SldWorks swApp, ErrorHandler errors, IGCamLog log)
            : base(swApp, errors, log)
        {
        }

        protected override string Title => "G-CAM Operation";

        protected override string Message =>
            "An operation cuts one piece of geometry with one tool. " +
            "Nothing is configured yet.";

        protected override void BuildControls(IPropertyManagerPage2 page)
        {
            // Two groups rather than one, to check that the shared helpers hold up for
            // a page with more than a single section.
            var tool = AddGroup(page, GroupTool, "Tool");
            AddLabel(tool, LabelTool, "Tool selection will appear here.");

            var geometry = AddGroup(page, GroupGeometry, "Geometry");
            AddLabel(geometry, LabelGeometry, "Faces, edges and heights will appear here.");
        }
    }
}
