using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using GCam.Core.Abstractions;
using GCam.Core.Diagnostics;
using GCam.Core.Model;
using GCam.SolidWorks.Events;
using GCam.SolidWorks.Persistence;
using GCam.SolidWorks.PropertyPages;
using GCam.SolidWorks.Rendering;
using GCam.UI.ViewModels;
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
        /// <summary>
        /// What marks a Manager Pane tab as ours when SOLIDWORKS names one back to us.
        /// </summary>
        private const string TabIdentity = "G-CAM";

        private const string TabToolTip = "G-CAM jobs, setups and operations";

        private readonly SldWorks _swApp;
        private readonly string[] _tabIcons;
        private readonly ErrorHandler _errors;
        private readonly IGCamLog _log;
        private readonly Action _onTabActivated;
        private readonly IJobEditor _jobEditor;
        private readonly JobDocumentStorage _storage;

        // Keyed on the ModelDoc2 itself. The CLR hands out one runtime callable wrapper
        // per COM identity, so the same document is the same key however we reached it -
        // which is what the stock SOLIDWORKS add-in template relies on too.
        private readonly Dictionary<ModelDoc2, DocumentTab> _tabs =
            new Dictionary<ModelDoc2, DocumentTab>();

        private bool _subscribed;
        private bool _missingControlReported;

        /// <param name="tabIcons">
        /// Three absolute paths - small, medium and large - to the bitmap SOLIDWORKS
        /// draws on the tab. It reads them from disk, so they must exist next to the
        /// assembly rather than being embedded resources.
        /// </param>
        /// <param name="onTabActivated">
        /// Raised when the user selects the G-CAM tab in the Manager Pane, so the
        /// composition root can bring the matching CommandManager tab forward. A
        /// callback rather than a direct call because the ribbon belongs to GCam.AddIn,
        /// which this project does not know about.
        /// </param>
        /// <param name="jobEditor">
        /// What the tree calls when the user asks to edit a job or add an operation.
        /// Same reasoning as onTabActivated: the property pages belong to GCam.AddIn.
        /// </param>
        public JobTreeTabs(
            SldWorks swApp,
            string[] tabIcons,
            ErrorHandler errors,
            IGCamLog log,
            Action onTabActivated = null,
            IJobEditor jobEditor = null,
            JobDocumentStorage storage = null)
        {
            _swApp = swApp ?? throw new ArgumentNullException(nameof(swApp));
            _tabIcons = tabIcons ?? throw new ArgumentNullException(nameof(tabIcons));
            _errors = errors ?? throw new ArgumentNullException(nameof(errors));
            _log = log ?? NullLog.Instance;
            _onTabActivated = onTabActivated;
            _jobEditor = jobEditor;
            _storage = storage;
        }

        /// <summary>
        /// Tells SOLIDWORKS a document has unsaved CAM changes.
        /// </summary>
        /// <remarks>
        /// Without this there is no "Save Changes?" prompt, no
        /// <c>SaveToStorageStoreNotify</c>, and the jobs never reach the file - the user
        /// loses them without ever being asked. Every edit path has to end here, which is
        /// why it is public rather than something this class works out for itself.
        /// </remarks>
        public void MarkDirty(ModelDoc2 model)
        {
            if (model != null && _tabs.ContainsKey(model))
            {
                _storage?.MarkDirty(model);
            }
        }

        /// <summary>
        /// The jobs belonging to a document, or null if it has no G-CAM tab - which
        /// means it is not a part, or the tab could not be created.
        /// </summary>
        public JobDocument JobsFor(ModelDoc2 model)
        {
            if (model == null)
            {
                return null;
            }

            DocumentTab tab;
            return _tabs.TryGetValue(model, out tab) ? tab.Jobs : null;
        }

        /// <summary>The jobs belonging to whichever document is in front.</summary>
        public JobDocument JobsForActiveDocument() => JobsFor(_swApp.ActiveDoc as ModelDoc2);

        /// <summary>
        /// Rebuilds a document's tree after its jobs have changed. The viewmodel does
        /// this itself for changes it makes; this is for changes made elsewhere, such as
        /// a job created from the toolbar.
        /// </summary>
        public void RefreshJobs(ModelDoc2 model)
        {
            if (model == null)
            {
                return;
            }

            DocumentTab tab;
            if (_tabs.TryGetValue(model, out tab))
            {
                tab.Model.Refresh();
            }
        }

        /// <summary>Rebuilds the tree of whichever document is in front.</summary>
        public void RefreshActiveDocument() => RefreshJobs(_swApp.ActiveDoc as ModelDoc2);

        /// <summary>
        /// Selects a job in a document's tree, which is also what puts its stock and
        /// origin on screen.
        /// </summary>
        public void SelectJob(ModelDoc2 model, Job job)
        {
            if (model == null || job == null)
            {
                return;
            }

            DocumentTab tab;
            if (_tabs.TryGetValue(model, out tab))
            {
                tab.Model.SelectJob(job);
            }
        }

        /// <summary>
        /// Selects an operation in a document's tree, which is also what puts its toolpath
        /// on screen.
        /// </summary>
        public void SelectOperation(ModelDoc2 model, Operation operation)
        {
            if (model == null || operation == null)
            {
                return;
            }

            DocumentTab tab;
            if (_tabs.TryGetValue(model, out tab))
            {
                tab.Model.SelectOperation(operation);
            }
        }

        /// <summary>
        /// Generates whatever is selected in a document's tree.
        /// </summary>
        /// <remarks>
        /// For the toolbar's Generate button. The tree is where the selection lives, so the
        /// button states its intent here rather than working out what is selected itself.
        /// </remarks>
        public void GenerateSelection(ModelDoc2 model) => ModelFor(model)?.GenerateSelection();

        /// <summary>
        /// Notes in a document's tree which operation is being generated. Null clears it.
        /// </summary>
        public void ShowGenerating(ModelDoc2 model, Operation operation)
        {
            if (model == null)
            {
                return;
            }

            DocumentTab tab;
            if (_tabs.TryGetValue(model, out tab))
            {
                tab.Model?.ShowGenerating(operation);
            }
        }

        /// <summary>
        /// What draws a job in a document's 3D view, or null if that document has no
        /// G-CAM tab.
        /// </summary>
        /// <remarks>
        /// For the Job property page, which is built once for the session but has to
        /// preview into whichever part is in front. The tree reaches its own preview
        /// through the viewmodel instead.
        /// </remarks>
        public IJobPreview PreviewFor(ModelDoc2 model)
        {
            if (model == null)
            {
                return null;
            }

            DocumentTab tab;
            return _tabs.TryGetValue(model, out tab) ? tab.Preview : null;
        }

        /// <summary>The job preview for whichever document is in front.</summary>
        public IJobPreview PreviewForActiveDocument() => PreviewFor(_swApp.ActiveDoc as ModelDoc2);

        /// <summary>
        /// What draws cut-direction arrows in whichever document is in front, or null if
        /// that document has no G-CAM tab.
        /// </summary>
        /// <remarks>
        /// For the Operation page, which is built once for the session and has to draw
        /// into whichever part it was opened over - the same arrangement, and the same
        /// reason, as <see cref="PreviewForActiveDocument"/>.
        /// </remarks>
        public ICutDirectionPreview CutDirectionForActiveDocument()
        {
            var model = _swApp.ActiveDoc as ModelDoc2;

            DocumentTab tab;
            return model != null && _tabs.TryGetValue(model, out tab) ? tab.CutDirection : null;
        }

        /// <summary>
        /// One part's tab: the SOLIDWORKS view, the jobs it shows, the viewmodel tying
        /// them together, and what G-CAM draws in that part's 3D windows. All of it lives
        /// and dies with the document.
        /// </summary>
        /// <remarks>
        /// Rendering rides on the tab's lifetime because the two cover exactly the same
        /// documents - a part with a G-CAM tab is a part that can have jobs, and a job is
        /// the only thing there is to draw. It also avoids a second subscriber to the
        /// document notifications; when a third one appears, that is the moment to factor
        /// an Events layer out of this class rather than before.
        /// </remarks>
        private sealed class DocumentTab
        {
            public FeatMgrView View { get; set; }

            public JobDocument Jobs { get; set; }

            public JobTreeViewModel Model { get; set; }

            public ViewportRenderer Renderer { get; set; }

            public JobPreview Preview { get; set; }

            /// <summary>Draws which side of its contours the open operation will cut.</summary>
            public CutDirectionPreview CutDirection { get; set; }

            /// <summary>Saves this document's jobs into it. Null for a part that has none.</summary>
            public JobStorageHook Storage { get; set; }

            /// <summary>Marks this document's operations stale when the part is rebuilt.</summary>
            public PartRebuildWatcher Rebuilds { get; set; }
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

            var jobs = new JobDocument();

            var renderer = new ViewportRenderer(model, _errors, _log);
            var preview = new JobPreview(_swApp, model, renderer, _errors, _log);

            var tab = new DocumentTab
            {
                View = view,
                Jobs = jobs,
                Model = new JobTreeViewModel(jobs, _jobEditor, _log, preview, () => PartName(model)),
                Renderer = renderer,
                Preview = preview,
                CutDirection = new CutDirectionPreview(_swApp, model, renderer, _errors, _log),
            };

            _tabs[model] = tab;

            BindView(view, tab);

            // Per-document, because the notification is: PartDoc raises it, not SldWorks.
            var part = model as PartDoc;
            if (part != null)
            {
                part.FeatureManagerTabActivatedNotify += OnManagerPaneTabActivated;

                // Read now rather than waiting for LoadFromStorageStoreNotify. The help is
                // explicit that a fully open document can be read at any time, and this
                // avoids racing the notification: by the time a tab is built the document
                // is open, and the same path serves a part opened normally and the ten
                // already up when the add-in was switched on.
                LoadJobs(model, tab);

                if (_storage != null)
                {
                    // Whether the part already carries G-CAM data, known for free because
                    // LoadJobs has just tried to read it - no second trip to the storage,
                    // and nothing asked of it from inside a save notification. A part that
                    // has none is never written to unless a job is created in it; see
                    // JobStorageHook.OnSaveToStorage.
                    bool hasStoredData = tab.Jobs.Jobs.Count > 0;

                    tab.Storage = new JobStorageHook(
                        part, model, _storage, () => tab.Jobs, hasStoredData, _errors, _log);
                }

                // Subscribed after the jobs are loaded, so the load's own settling cannot
                // mark a freshly opened part's operations stale before anyone has seen
                // them.
                tab.Rebuilds = new PartRebuildWatcher(
                    part, () => tab.Jobs, () => RefreshJobs(model), _errors, _log);
            }

            _log.Debug("G-CAM tab added to {0}.", Describe(model));
        }

        /// <summary>
        /// Reads whatever CAM data the part already carries into its fresh job document.
        /// </summary>
        /// <remarks>
        /// Problems are logged rather than shown. A part that opens with a warning dialog
        /// every time is a part nobody opens; the operations that could not be read are
        /// reported on themselves, where the user is looking when they care.
        /// </remarks>
        private void LoadJobs(ModelDoc2 model, DocumentTab tab)
        {
            if (_storage == null)
            {
                return;
            }

            try
            {
                foreach (string problem in _storage.Load(model, tab.Jobs))
                {
                    _log.Warn("{0}: {1}", Describe(model), problem);
                }

                tab.Model?.Refresh();
            }
            catch (Exception ex)
            {
                // The part still opens, with no jobs. Losing the CAM data is bad; refusing
                // to open the part over it would be worse.
                _errors.Handle(ex, nameof(LoadJobs));
            }
        }

        /// <summary>
        /// Hands the freshly activated control its document.
        /// </summary>
        /// <remarks>
        /// SOLIDWORKS builds the hosting control through COM, so it has no constructor
        /// arguments and nothing was injected into it. GetControl hands back the
        /// instance that was activated, which is the only way to reach it.
        /// </remarks>
        private void BindView(FeatMgrView view, DocumentTab tab)
        {
            var host = view.GetControl() as JobTreeTabHost;

            if (host?.View == null)
            {
                // The control exists - CreateFeatureMgrControl4 returned a view - but it
                // is not ours, or its constructor fell back to the error label. Either
                // way the tab shows something; it just will not show jobs.
                _log.Warn("Could not reach the hosted G-CAM view, so the tab will stay empty.");
                return;
            }

            host.View.Bind(tab.Model, _errors);
        }

        /// <summary>
        /// Entry point 8. Fires whenever the active Manager Pane tab changes, in any
        /// part with a G-CAM tab. When the tab selected is ours, bring the G-CAM ribbon
        /// tab forward so the toolbar matches what the pane is showing.
        /// </summary>
        /// <remarks>
        /// The help's parameter descriptions for this delegate are copied and wrong -
        /// both are documented as "Index of the active tab". Observed: CommandTabName is
        /// the tooltip passed to CreateFeatureMgrControl4, which is the only
        /// human-readable string SOLIDWORKS was ever given for the tab. Matching on it
        /// beats matching on the index, which shifts with whatever other add-ins are
        /// installed.
        /// </remarks>
        private int OnManagerPaneTabActivated(int commandIndex, string commandTabName)
        {
            try
            {
                bool ours = IsOurTab(commandTabName);

                if (ours)
                {
                    _onTabActivated?.Invoke();
                }

                ShowTreeSelection(ours);
            }
            catch (Exception ex)
            {
                // Quiet: this fires on every tab click, so a dialog here would follow
                // the user around the Manager Pane.
                _errors.Handle(ex, nameof(OnManagerPaneTabActivated), quiet: true);
            }

            return 0;
        }

        /// <summary>
        /// Puts the tree's selection away when the pane moves off the G-CAM tab, and back
        /// when it returns.
        /// </summary>
        /// <remarks>
        /// <b>The selection is what draws the 3D preview</b>, and the preview is drawn over
        /// the part whether or not the tree is in front - so a tab nobody is looking at
        /// would otherwise leave a stock box standing over somebody else's work. Put away
        /// rather than discarded: coming back to the tab restores exactly what was there.
        ///
        /// <b>A G-CAM property page is not the user leaving.</b> Showing one moves the pane
        /// onto the PropertyManager's own tab, so this fires - and clearing then would take
        /// the toolpath off the screen at the moment the operation is being edited, which is
        /// when it is most wanted. <see cref="GCamPropertyPage.AnyOpen"/> is the exception;
        /// somebody else's page is not, because then the tree really is not in front.
        ///
        /// The pane belongs to whichever document is in front, which is why this asks for
        /// the active one rather than taking a document: the notification carries none.
        /// </remarks>
        private void ShowTreeSelection(bool ours)
        {
            JobTreeViewModel model = ModelFor(_swApp.ActiveDoc as ModelDoc2);

            if (model == null)
            {
                return;
            }

            if (ours)
            {
                model.TabShown();
            }
            else if (!GCamPropertyPage.AnyOpen)
            {
                model.TabHidden();
            }
        }

        private JobTreeViewModel ModelFor(ModelDoc2 model)
        {
            DocumentTab tab;

            return model != null && _tabs.TryGetValue(model, out tab) ? tab.Model : null;
        }

        private static bool IsOurTab(string tabName)
        {
            return !string.IsNullOrEmpty(tabName)
                   && tabName.IndexOf(TabIdentity, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Stops tracking a document and takes its tab away.
        /// </summary>
        private void Forget(ModelDoc2 model)
        {
            DocumentTab tab;
            if (!_tabs.TryGetValue(model, out tab))
            {
                return;
            }

            FeatMgrView view = tab.View;

            _tabs.Remove(model);

            // Before the view goes: the renderer is subscribed to this document's
            // windows, and unsubscribing needs them still to be there.
            tab.Renderer?.Dispose();

            // The document is closing, so the last save has already happened or been
            // declined. Nothing to flush here - writing is only legal inside the
            // notification this is unsubscribing from.
            tab.Storage?.Dispose();

            // Before the document goes, and before anything else can queue a redraw of a
            // scene that is about to be torn down.
            tab.Rebuilds?.Dispose();

            var part = model as PartDoc;
            if (part != null)
            {
                try
                {
                    part.FeatureManagerTabActivatedNotify -= OnManagerPaneTabActivated;
                }
                catch (Exception ex)
                {
                    // Expected when the document has already gone.
                    _errors.Handle(ex, nameof(Forget) + ".Unsubscribe", quiet: true);
                }
            }

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

        /// <summary>
        /// What to call the part at the top of its tree - its file name, without the
        /// extension.
        /// </summary>
        /// <remarks>
        /// From the path where there is one, because <c>GetTitle</c> carries the extension
        /// or not depending on a Windows setting nobody should have to think about. An
        /// unsaved part has no path and falls back to the title, which is what SOLIDWORKS
        /// is calling it in the window - "Part1".
        ///
        /// Asked for on every tree rebuild rather than held, so a Save As follows without
        /// anything having to subscribe to a rename. It is two string operations and a COM
        /// call on a rebuild that is already rebuilding every row.
        /// </remarks>
        private static string PartName(ModelDoc2 model)
        {
            if (model == null)
            {
                return string.Empty;
            }

            string path = model.GetPathName();

            return string.IsNullOrEmpty(path)
                ? Path.GetFileNameWithoutExtension(model.GetTitle() ?? string.Empty)
                : Path.GetFileNameWithoutExtension(path);
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
