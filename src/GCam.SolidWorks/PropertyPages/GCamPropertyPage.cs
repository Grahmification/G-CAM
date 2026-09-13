using System;
using System.Runtime.InteropServices;
using GCam.Core.Diagnostics;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace GCam.SolidWorks.PropertyPages
{
    /// <summary>
    /// Shared shape of a G-CAM PropertyManager page: build once, show on demand, and
    /// put the user back on the Manager Pane tab they came from afterwards.
    /// </summary>
    /// <remarks>
    /// A PropertyManager page cannot be drawn inside our own Manager Pane tab -
    /// CreatePropertyManagerPage takes no parent, and SOLIDWORKS always renders the page
    /// on the PropertyManager tab. So editing looks like this:
    ///
    ///     G-CAM tab  ->  (edit)  ->  PropertyManager tab  ->  (OK/Cancel)  ->  G-CAM tab
    ///
    /// The last hop is the only one that is ours to arrange, and
    /// <see cref="RestoreManagerPaneTab"/> does it by remembering which tab was active
    /// before the page was shown rather than by hunting for our own tab's index. That
    /// also does the right thing when the page was opened from somewhere else.
    ///
    /// Derived pages supply a title, a message and their controls. The page lifecycle
    /// callbacks are sealed here so a page cannot silently skip the tab restore by
    /// overriding AfterClose; <see cref="PageShown"/> and <see cref="PageClosed"/> are
    /// the hooks to use instead.
    /// </remarks>
    public abstract class GCamPropertyPage : PmpHandlerBase, IDisposable
    {
        private readonly SldWorks _swApp;
        private readonly IGCamLog _log;

        private IPropertyManagerPage2 _page;
        private bool _isOpen;

        // The document the page was shown against, and the Manager Pane tab that was
        // active at the time. Borrowed, never released - see manager-pane-tabs.md.
        private ModelDoc2 _shownAgainst;
        private int _returnToTab = -1;

        private swPropertyManagerPageCloseReasons_e _closeReason;

        protected GCamPropertyPage(SldWorks swApp, ErrorHandler errors, IGCamLog log)
            : base(errors)
        {
            _swApp = swApp ?? throw new ArgumentNullException(nameof(swApp));
            _log = log ?? NullLog.Instance;
        }

        protected IGCamLog Log => _log;

        /// <summary>Text in the page's title bar.</summary>
        protected abstract string Title { get; }

        /// <summary>The blue explanatory box at the top of the page.</summary>
        protected abstract string Message { get; }

        /// <summary>
        /// Adds the page's groups and controls. Called once, while the page is closed -
        /// SOLIDWORKS ignores controls added to a page that is already on screen.
        /// </summary>
        protected abstract void BuildControls(IPropertyManagerPage2 page);

        /// <summary>
        /// Displays the page, building it on first use.
        /// </summary>
        /// <exception cref="GCamUserException">
        /// No part is open. A page can be created with no document, but not shown.
        /// </exception>
        public void Show()
        {
            if (_isOpen)
            {
                _log.Debug("{0} is already open.", Title);
                return;
            }

            if (_page == null)
            {
                _page = Build();
            }

            RememberManagerPaneTab();

            // 0 rather than a named value: swPropertyManagerPageShowOptions_e defines
            // only StackPage, and these pages do not stack.
            int status = _page.Show2(0);

            switch ((swPropertyManagerPageStatus_e)status)
            {
                case swPropertyManagerPageStatus_e.swPropertyManagerPage_Okay:
                    _isOpen = true;
                    break;

                case swPropertyManagerPageStatus_e.swPropertyManagerPage_NoDocument:
                    throw new GCamUserException("Open a part first.");

                default:
                    throw new GCamUserException(
                        "The " + Title + " panel could not be opened (status " + status + ").");
            }
        }

        private IPropertyManagerPage2 Build()
        {
            const int pageOptions =
                (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_OkayButton |
                (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_CancelButton |
                (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_LockedPage;

            // `ref`, not `out`, despite the help documenting it as an output parameter -
            // the interop declares it [In, Out]. Initialise it rather than trusting it.
            int errors = 0;
            var page = _swApp.CreatePropertyManagerPage(Title, pageOptions, this, ref errors)
                       as IPropertyManagerPage2;

            if (page == null || (swPropertyManagerPageStatus_e)errors
                != swPropertyManagerPageStatus_e.swPropertyManagerPage_Okay)
            {
                // UnsupportedHandler means SOLIDWORKS could not QueryInterface this
                // object for IPropertyManagerPage2Handler9 - check PmpHandlerBase first.
                throw new InvalidOperationException(
                    "CreatePropertyManagerPage failed for " + Title +
                    " with status " + errors + ".");
            }

            page.SetMessage3(
                Message,
                (int)swPropertyManagerPageMessageVisibility.swMessageBoxVisible,
                (int)swPropertyManagerPageMessageExpanded.swMessageBoxExpand,
                Title);

            BuildControls(page);

            return page;
        }

        // ---- Manager Pane tab -----------------------------------------------

        private void RememberManagerPaneTab()
        {
            _shownAgainst = null;
            _returnToTab = -1;

            var model = _swApp.ActiveDoc as ModelDoc2;
            if (model == null)
            {
                return;
            }

            _shownAgainst = model;
            _returnToTab = model.ModelViewManager.ActiveFeatureManagerTabIndex;
        }

        private void RestoreManagerPaneTab()
        {
            if (_shownAgainst == null || _returnToTab < 0)
            {
                return;
            }

            try
            {
                _shownAgainst.ModelViewManager.ActiveFeatureManagerTabIndex = _returnToTab;
            }
            catch (Exception ex)
            {
                // Expected if the document was closed while the page was up. Quiet
                // because landing on the wrong tab is a blemish, not a failure.
                Errors.Handle(ex, nameof(RestoreManagerPaneTab), quiet: true);
            }
            finally
            {
                _shownAgainst = null;
                _returnToTab = -1;
            }
        }

        // ---- Lifecycle ------------------------------------------------------

        protected sealed override void AfterActivation()
        {
            _isOpen = true;
            PageShown();
        }

        protected sealed override void OnClose(swPropertyManagerPageCloseReasons_e reason)
        {
            // SOLIDWORKS permits no real work here - the page and its command are
            // already closing. Record what happened and act on it in AfterClose.
            _isOpen = false;
            _closeReason = reason;
        }

        protected sealed override void AfterClose()
        {
            RestoreManagerPaneTab();
            PageClosed(_closeReason);
        }

        protected virtual void PageShown() { }

        /// <summary>
        /// Where a page commits its edits. <paramref name="reason"/> distinguishes OK
        /// from Cancel, Escape and the document closing underneath it.
        /// </summary>
        protected virtual void PageClosed(swPropertyManagerPageCloseReasons_e reason) { }

        // ---- Control helpers ------------------------------------------------

        protected static IPropertyManagerPageGroup AddGroup(
            IPropertyManagerPage2 page, int id, string caption)
        {
            var group = page.AddGroupBox(
                id,
                caption,
                (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Visible |
                (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Expanded)
                as IPropertyManagerPageGroup;

            if (group == null)
            {
                throw new InvalidOperationException(
                    "AddGroupBox returned null for group " + id + " (" + caption + ").");
            }

            return group;
        }

        protected static void AddLabel(IPropertyManagerPageGroup group, int id, string text)
        {
            // AddControl2, not AddControl: since 2014 the newer overload requires
            // swControlOptions_Visible explicitly, so an omitted option produces an
            // invisible control rather than a missing one - which looks like a bug in
            // the layout rather than in the options.
            group.AddControl2(
                id,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                text,
                (short)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge,
                (int)swAddControlOptions_e.swControlOptions_Visible |
                (int)swAddControlOptions_e.swControlOptions_Enabled,
                string.Empty);
        }

        /// <summary>
        /// Releases the page. Called from DisconnectFromSW, never from a handler -
        /// closing a page from inside its own callback is what the API help warns about.
        /// </summary>
        public void Dispose()
        {
            if (_page == null)
            {
                return;
            }

            if (_isOpen)
            {
                _page.Close(false);
                _isOpen = false;
            }

            Marshal.ReleaseComObject(_page);
            _page = null;
        }
    }
}
