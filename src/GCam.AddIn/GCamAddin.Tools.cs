using GCam.Core.Diagnostics;
using GCam.Core.Model;
using GCam.Core.Tooling;
using GCam.UI.Views;
using SolidWorks.Interop.sldworks;

namespace GCam.AddIn
{
    /// <summary>
    /// The part's tool list: getting cutters into it.
    /// </summary>
    /// <remarks>
    /// Here rather than in GCam.SolidWorks because it is the join between two projects
    /// that cannot see each other - the browser is WPF in GCam.UI, the tool list is Core,
    /// and the page that wants both is in GCam.SolidWorks. The add-in is the only project
    /// that knows all three exist, which is exactly what the composition root is for.
    /// </remarks>
    public partial class GCamAddin
    {
        /// <summary>
        /// Opens the tool library browser as a chooser and copies what comes back into
        /// the active part. Returns the part's own copy, or null if nothing was picked.
        /// </summary>
        /// <remarks>
        /// Reached from the Operation page's Browse button, so it runs inside entry point
        /// 7 and throws rather than catching - <c>PmpHandlerBase</c> wraps the callback
        /// and reports.
        ///
        /// **The part's copy is returned, not the library's.** An operation references a
        /// tool by id into <see cref="JobDocument.Tools"/>, and
        /// <see cref="JobDocument.AddTool"/> is idempotent by <see cref="Tool.Id"/> - so
        /// picking a tool the part already holds hands back the copy that is here.
        /// Returning the library's copy instead would point the operation at an object
        /// the document does not contain.
        ///
        /// The part is marked dirty because the tool is now part of the document.
        /// Without that the user closes the part, is never offered a save, and the tool
        /// goes with it - the same trap <see cref="OnJobCommitted"/> records.
        /// </remarks>
        private Tool PickToolIntoPart()
        {
            var model = _swApp.ActiveDoc as ModelDoc2;
            JobDocument jobs = _jobTreeTabs?.JobsFor(model);

            if (model == null || jobs == null)
            {
                throw new GCamUserException("Open a part before choosing a tool.");
            }

            Tool picked = ToolLibraryDialog.PickTool(MainWindowHandle(), _settings, _log);

            if (picked == null)
            {
                return null;
            }

            int before = jobs.Tools.Count;
            Tool inPart = jobs.AddTool(picked);

            if (jobs.Tools.Count != before)
            {
                _log.Info(
                    "Added tool '{0}' to the part from library '{1}'.",
                    inPart.DisplayName, inPart.SourceLibraryId);

                _jobTreeTabs.MarkDirty(model);
            }

            // Two tools in one pocket is a part that cannot be set up as written. Not
            // fatal and not this dialog's business to refuse, so it is logged here and
            // reported properly before posting.
            foreach (string problem in jobs.ValidateTools())
            {
                _log.Warn("{0}", problem);
            }

            return inPart;
        }
    }
}
