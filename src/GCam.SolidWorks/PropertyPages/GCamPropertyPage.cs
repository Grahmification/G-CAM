using System;
using System.Collections.Generic;
using System.Linq;
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
        // Selection box heights, in dialog units rather than pixels. A box that can only
        // ever hold one thing should not reserve three rows of empty space.
        private const short SingleRowSelectionHeight = 14;
        private const short ListSelectionHeight = 50;

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

        protected SldWorks SwApp => _swApp;

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
        /// Puts the current values into the controls. Called before every show, while
        /// the page is closed.
        /// </summary>
        /// <remarks>
        /// Deliberately not called from AfterActivation. Assigning to a combobox or a
        /// number box fires that control's change callback, and a page that reacts to
        /// those by showing and hiding controls would be rearranging itself while
        /// SOLIDWORKS is still building it.
        /// </remarks>
        protected virtual void LoadControls() { }

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

            // Rebuilt every time rather than cached.
            //
            // A reused page has to be reshaped before each show, and reshaping means
            // setting IPropertyManagerPageControl.Visible - which kills SOLIDWORKS after
            // a handful of shows, silently. Building afresh lets every control be
            // created with the visibility it needs, so that property is never touched on
            // the way in. Building a page is a dozen API calls; it is not worth caching
            // at this price.
            ReleasePage();
            _page = Build();

            // Populated while the page is still closed. The help is explicit that a page
            // is configured before it is displayed, and doing it from AfterActivation
            // instead - nested inside Show2 - left the page blank: setting a combobox
            // fires its change callback while SOLIDWORKS is still assembling the page.
            LoadControls();

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
                (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_LockedPage |
                // Pages here show and hide controls from inside their own handlers - the
                // stock mode dropdown does it on every change. Without this the page
                // repaints per control and visibly flickers.
                (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_DisablePageBuildDuringHandlers;

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

        protected static IPropertyManagerPageTextbox AddTextbox(
            IPropertyManagerPageGroup group, int id, string tip)
        {
            return AddControl<IPropertyManagerPageTextbox>(
                group, id, swPropertyManagerPageControlType_e.swControlType_Textbox, string.Empty, tip);
        }

        /// <summary>
        /// A length box. Values are in whatever units SOLIDWORKS hands back - see
        /// <see cref="JobPropertyPage"/> for the conversion and why it is measured
        /// rather than assumed.
        /// </summary>
        protected static IPropertyManagerPageNumberbox AddLengthbox(
            IPropertyManagerPageGroup group, int id, string caption, string tip, bool visible = true)
        {
            var box = AddControl<IPropertyManagerPageNumberbox>(
                group, id, swPropertyManagerPageControlType_e.swControlType_Numberbox, caption, tip, visible);

            // Units cannot be changed once the page is shown, so this has to happen here.
            // The upper bound is deliberately generous rather than a guess at machine
            // capacity; it exists to stop a typo becoming a kilometre of stock.
            box.SetRange2(
                (int)swNumberboxUnitType_e.swNumberBox_Length,
                Minimum: 0,
                Maximum: 10000,
                Inclusive: true,
                Increment: 1,
                FastIncr: 10,
                SlowIncr: 0.1);

            return box;
        }

        /// <summary>
        /// A drop-down. Give it a <see cref="AddLabel"/> above it - a combobox does not
        /// render a caption of its own.
        /// </summary>
        protected static IPropertyManagerPageCombobox AddCombobox(
            IPropertyManagerPageGroup group, int id, IEnumerable<string> items, string tip)
        {
            var combo = AddControl<IPropertyManagerPageCombobox>(
                group, id, swPropertyManagerPageControlType_e.swControlType_Combobox, string.Empty, tip);

            combo.Height = 0;    // 0 lets SOLIDWORKS size the drop-down to its content.
            combo.AddItems(items.ToArray());

            return combo;
        }

        /// <param name="height">
        /// Height in <b>dialog units</b>, not pixels. Zero picks a sensible default: one
        /// row for a single-entity box, three for a list.
        /// </param>
        /// <param name="mark">
        /// Distinguishes this box from every other selection box on the page. It is how
        /// SOLIDWORKS decides which box a click belongs to, and how
        /// ISelectionMgr::GetSelectedObject6 later tells them apart, so each box on a
        /// page needs its own.
        /// </param>
        protected static IPropertyManagerPageSelectionbox AddSelectionbox(
            IPropertyManagerPageGroup group,
            int id,
            int mark,
            swSelectType_e[] filters,
            bool singleEntityOnly,
            string tip,
            short height = 0)
        {
            var box = AddControl<IPropertyManagerPageSelectionbox>(
                group, id, swPropertyManagerPageControlType_e.swControlType_Selectionbox, string.Empty, tip);

            box.Height = height > 0
                ? height
                : (singleEntityOnly ? SingleRowSelectionHeight : ListSelectionHeight);
            box.Mark = mark;
            box.SingleEntityOnly = singleEntityOnly;
            box.SetSelectionFilters(filters.Select(f => (int)f).ToArray());

            return box;
        }

        private static T AddControl<T>(
            IPropertyManagerPageGroup group,
            int id,
            swPropertyManagerPageControlType_e type,
            string caption,
            string tip,
            bool visible = true,
            swPropertyManagerPageControlLeftAlign_e align =
                swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent)
            where T : class
        {
            // AddControl2, not AddControl: since 2014 the newer overload requires
            // swControlOptions_Visible explicitly, so an omitted option produces an
            // invisible control rather than a missing one.
            var control = group.AddControl2(
                id,
                (short)type,
                caption,
                (short)align,
                (visible ? (int)swAddControlOptions_e.swControlOptions_Visible : 0) |
                (int)swAddControlOptions_e.swControlOptions_Enabled,
                tip ?? string.Empty) as T;

            if (control == null)
            {
                throw new InvalidOperationException(
                    $"AddControl2 returned null or the wrong type for {type} with id {id}.");
            }

            return control;
        }

        /// <summary>
        /// Shows or hides a control. Every control is created up front, because they
        /// cannot be added to a page that is already on screen; this is how a page
        /// changes shape afterwards.
        /// </summary>
        protected static void SetVisible(object control, bool visible)
        {
            var asControl = control as IPropertyManagerPageControl;
            if (asControl != null)
            {
                asControl.Visible = visible;
            }
        }

        /// <summary>
        /// A line of static text.
        /// </summary>
        /// <remarks>
        /// Number boxes, comboboxes, text boxes and selection boxes do **not** display
        /// the caption passed to AddControl2 - the caption is accepted and ignored, and
        /// SOLIDWORKS' own example passes an empty string for all of them. A label
        /// control is the only way to put a name next to one.
        /// </remarks>
        protected static IPropertyManagerPageLabel AddLabel(
            IPropertyManagerPageGroup group, int id, string text, bool visible = true)
        {
            return AddControl<IPropertyManagerPageLabel>(
                group,
                id,
                swPropertyManagerPageControlType_e.swControlType_Label,
                text,
                null,
                visible,
                swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge);
        }

        /// <summary>
        /// Releases the page. Called from DisconnectFromSW, never from a handler -
        /// closing a page from inside its own callback is what the API help warns about.
        /// </summary>
        public void Dispose()
        {
            ReleasePage();
        }

        private void ReleasePage()
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
