using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using GCam.Core.Diagnostics;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace GCam.SolidWorks.Hosting
{
    /// <summary>
    /// Keeps a G-CAM tab in the Manager Pane of every open part - the strip of icons
    /// beside the FeatureManager design tree and PropertyManager tabs, which is where
    /// HSMWorks and CAMWorks put their CAM trees.
    /// </summary>
    /// <remarks>
    /// A FeatureManager tab belongs to a *document*, not to the application:
    /// CreateFeatureMgrControl4 hangs off IModelDoc2::ModelViewManager. So one call at
    /// connect time is not enough - and it is usually worse than not enough, because
    /// add-ins connect before any document is open, so the single call had nothing to
    /// attach to and did nothing at all.
    ///
    /// Rather than handle open, new, activate and close separately, every notification
    /// runs the same <see cref="Sync"/>: compare the documents SOLIDWORKS has open
    /// against the tabs we are holding, and add or drop the difference. Idempotent, so
    /// a duplicated or missed event costs nothing.
    /// </remarks>
    public sealed class JobTreeTabs : IDisposable
    {
        /// <summary>Caption on the tab. Shown when the pane is wide enough for text.</summary>
        private const string TabTitle = "G-CAM";

        private const string TabToolTip = "G-CAM jobs, setups and operations";

        private readonly SldWorks _swApp;
        private readonly string[] _tabIcons;
        private readonly ErrorHandler _errors;
        private readonly IGCamLog _log;

        // Keyed on the ModelDoc2 itself. The CLR hands out one runtime callable wrapper
        // per COM identity, so the same document is the same key however we reached it -
        // which is what the stock SOLIDWORKS add-in template relies on too.
        private readonly Dictionary<ModelDoc2, FeatMgrView> _tabs =
            new Dictionary<ModelDoc2, FeatMgrView>();

        private bool _subscribed;
        private bool _missingControlReported;

        /// <param name="tabIcons">
        /// Three absolute paths - small, medium and large - to the bitmap SOLIDWORKS
        /// draws on the tab. It reads them from disk, so they must exist next to the
        /// assembly rather than being embedded resources.
        /// </param>
        public JobTreeTabs(SldWorks swApp, string[] tabIcons, ErrorHandler errors, IGCamLog log)
        {
            _swApp = swApp ?? throw new ArgumentNullException(nameof(swApp));
            _tabIcons = tabIcons ?? throw new ArgumentNullException(nameof(tabIcons));
            _errors = errors ?? throw new ArgumentNullException(nameof(errors));
            _log = log ?? NullLog.Instance;
        }

        /// <summary>
        /// Subscribes to the document notifications and catches up with whatever is
        /// already open - the add-in can be switched on mid-session with ten parts up.
        /// </summary>
        public void Start()
        {
            _swApp.ActiveModelDocChangeNotify += OnActiveModelDocChange;
            _swApp.FileCloseNotify += OnFileClose;
            _subscribed = true;

            Sync();
        }

        /// <summary>
        /// Entry point 8. Fires when the user switches windows, and after a document is
        /// opened, created or closed - which is why this one notification covers all of
        /// them.
        /// </summary>
        private int OnActiveModelDocChange()
        {
            try
            {
                Sync();
            }
            catch (Exception ex)
            {
                // Not quiet: a tab that silently fails to appear is the exact failure
                // this class exists to fix. Repeat suppression holds it to one dialog.
                _errors.Handle(ex, nameof(OnActiveModelDocChange));
            }

            return 0;
        }

        /// <summary>
        /// Entry point 8. Fires *before* the document has gone, so the closing document
        /// may still be enumerated here; the next active-document change prunes it.
        /// </summary>
        private int OnFileClose(string fileName, int reason)
        {
            try
            {
                Sync();
            }
            catch (Exception ex)
            {
                _errors.Handle(ex, nameof(OnFileClose));
            }

            return 0;
        }

        /// <summary>
        /// Brings the set of tabs into line with the set of open documents.
        /// </summary>
        private void Sync()
        {
            List<ModelDoc2> open = OpenDocuments();
            var stillOpen = new HashSet<ModelDoc2>(open);

            // Prune against every open document, not just the ones that want a tab: a
            // part can stop being visible without being closed, and losing its tab for
            // that would be wrong.
            foreach (ModelDoc2 gone in _tabs.Keys.Where(d => !stillOpen.Contains(d)).ToList())
            {
                Forget(gone);
            }

            foreach (ModelDoc2 model in open.Where(WantsTab).Where(d => !_tabs.ContainsKey(d)))
            {
                AddTab(model);
            }
        }

        /// <summary>
        /// Parts only, and only ones with a window of their own. EnumDocuments2 also
        /// returns documents loaded as references - the parts inside an open assembly -
        /// and those have no Manager Pane to put a tab in.
        /// </summary>
        private static bool WantsTab(ModelDoc2 model)
        {
            // IModelDoc2 declares its own GetType() returning a swDocumentTypes_e, which
            // hides object.GetType(). Startling to read, but correct.
            return (swDocumentTypes_e)model.GetType() == swDocumentTypes_e.swDocPART
                   && model.Visible;
        }

        private void AddTab(ModelDoc2 model)
        {
            FeatMgrView view = model.ModelViewManager.CreateFeatureMgrControl4(
                _tabIcons,
                JobTreeTabHost.ProgIdValue,
                string.Empty,
                TabToolTip,
                // swFeatMgrPaneBottom is what the help prescribes for "add a tab to the
                // FeatureManager design tree". It only means a *pane* when the tree has
                // been split; unsplit, this is an ordinary tab.
                (int)swFeatMgrPane_e.swFeatMgrPaneBottom);

            if (view == null)
            {
                ReportMissingControl();
                return;
            }

            _tabs[model] = view;
            _log.Debug("G-CAM tab added to {0}.", Describe(model));
        }

        /// <summary>
        /// Stops tracking a document and takes its tab away.
        /// </summary>
        private void Forget(ModelDoc2 model)
        {
            FeatMgrView view;
            if (!_tabs.TryGetValue(model, out view))
            {
                return;
            }

            _tabs.Remove(model);

            try
            {
                // DeleteView, not just a release: dropping the reference leaves the tab
                // in the document with a dead control behind it.
                view.DeleteView();
            }
            catch (Exception ex)
            {
                // Expected when the document has already gone.
                _errors.Handle(ex, nameof(Forget), quiet: true);
            }

            Marshal.ReleaseComObject(view);

            // The ModelDoc2 is deliberately not released. Unlike the view, we did not
            // create it, and its wrapper is the one shared by everything else in the
            // process - releasing it would invalidate the document for SOLIDWORKS' own
            // code and for other add-ins.
        }

        private List<ModelDoc2> OpenDocuments()
        {
            var docs = new List<ModelDoc2>();

            EnumDocuments2 enumerator = _swApp.EnumDocuments2();
            if (enumerator == null)
            {
                return docs;
            }

            try
            {
                enumerator.Reset();

                while (true)
                {
                    ModelDoc2 doc;

                    // `ref`, not `out`, however much the help calls it an output: the
                    // interop declares it [In, Out]. Initialise it yourself.
                    int fetched = 0;
                    enumerator.Next(1, out doc, ref fetched);

                    if (fetched != 1 || doc == null)
                    {
                        break;
                    }

                    docs.Add(doc);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(enumerator);
            }

            return docs;
        }

        /// <summary>
        /// CreateFeatureMgrControl4 returning null almost always means
        /// GCam.SolidWorks.dll is not COM-registered, so SOLIDWORKS could not activate
        /// the control by ProgID.
        /// </summary>
        /// <remarks>
        /// Reported once per session rather than once per document: with six parts open
        /// the same unfixable problem would otherwise be announced six times.
        /// </remarks>
        private void ReportMissingControl()
        {
            const string message =
                "The G-CAM tab could not be added to this part.\n\n" +
                "GCam.SolidWorks.dll is most likely not registered. Run " +
                "deploy\\register.cmd from an elevated prompt, then restart SOLIDWORKS.";

            _log.Warn(
                "CreateFeatureMgrControl4 returned null for ProgID {0}. Is GCam.SolidWorks.dll registered?",
                JobTreeTabHost.ProgIdValue);

            if (_missingControlReported)
            {
                return;
            }

            _missingControlReported = true;
            _errors.Handle(new GCamUserException(message), nameof(AddTab));
        }

        private static string Describe(ModelDoc2 model)
        {
            string path = model.GetPathName();
            return string.IsNullOrEmpty(path) ? model.GetTitle() : path;
        }

        public void Dispose()
        {
            if (_subscribed)
            {
                _swApp.ActiveModelDocChangeNotify -= OnActiveModelDocChange;
                _swApp.FileCloseNotify -= OnFileClose;
                _subscribed = false;
            }

            foreach (ModelDoc2 model in _tabs.Keys.ToList())
            {
                Forget(model);
            }
        }
    }
}
