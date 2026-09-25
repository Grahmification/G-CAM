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
    /// Shared shape of a G-CAM PropertyManager page: rebuilt for every show, and putting
    /// the user back on the Manager Pane tab they came from afterwards.
    /// </summary>
    /// <remarks>
    /// SOLIDWORKS always draws a page on the PropertyManager tab, never inside ours, so
    /// <see cref="RestoreManagerPaneTab"/> returns to whichever tab was active before the
    /// show - not necessarily ours, since a page can be opened from elsewhere.
    ///
    /// The lifecycle callbacks are sealed so a page cannot skip the tab restore by
    /// overriding AfterClose; override <see cref="PageShown"/> and <see cref="PageClosed"/>
    /// instead.
    /// </remarks>
    public abstract class GCamPropertyPage : PmpHandlerBase, IDisposable
    {
        // Dialog units, not pixels. A single-entity box should not reserve three rows.
        private const short SingleRowSelectionHeight = 14;
        private const short ListSelectionHeight = 50;

        private readonly SldWorks _swApp;
        private readonly IGCamLog _log;

        private IPropertyManagerPage2 _page;
        private bool _isOpen;

        // A rebuild is a close and a show the derived page must not see: no commit, no
        // tab restore, no reload of what it is editing.
        private bool _rebuilding;
        private System.Windows.Forms.Timer _rebuildTimer;

        // Borrowed, never released - see manager-pane-tabs.md.
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

        /// <summary>True while the page is on screen and taking part in the UI.</summary>
        protected bool IsOpen => _isOpen;

        /// <summary>
        /// True while the page is being closed and shown again to change what it displays.
        /// </summary>
        /// <remarks>
        /// Callbacks still arrive during a rebuild, describing SOLIDWORKS taking the page
        /// apart rather than anything the user did. A page that reads state back out of its
        /// controls must ignore them.
        /// </remarks>
        protected bool IsRebuilding => _rebuilding;

        /// <summary>Text in the page's title bar.</summary>
        protected abstract string Title { get; }

        /// <summary>
        /// The blue explanatory box at the top of the page. Null or empty for no box.
        /// </summary>
        /// <remarks>
        /// Leave it empty once a page explains itself: the box costs panel height on every
        /// show.
        /// </remarks>
        protected virtual string Message => null;

        /// <summary>
        /// Adds the page's groups and controls to a freshly created page, before every
        /// show. SOLIDWORKS ignores controls added to a page already on screen.
        /// </summary>
        protected abstract void BuildControls(IPropertyManagerPage2 page);

        /// <summary>
        /// Puts the current values into the controls. Called before every show, while
        /// the page is closed.
        /// </summary>
        /// <remarks>
        /// Not from AfterActivation, which runs inside Show2: assigning a value fires the
        /// control's change callback while SOLIDWORKS is still assembling the page, and
        /// doing so left the page blank.
        /// </remarks>
        protected virtual void LoadControls() { }

        /// <summary>
        /// Builds the page afresh and displays it.
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

            // Rebuilt rather than cached. Reusing a page means reshaping it with
            // IPropertyManagerPageControl.Visible, which silently kills SOLIDWORKS after a
            // few shows. A fresh page creates each control at the visibility it needs, for
            // the price of a dozen API calls.
            ReleasePage();
            _page = Build();

            LoadControls();

            RememberManagerPaneTab();

            // Counted before Show2, not in AfterActivation: Show2 moves the Manager Pane to
            // the PropertyManager tab, and the job tree reads AnyOpen when that happens,
            // which may be before AfterActivation.
            bool counted = !_rebuilding;

            if (counted)
            {
                _openPages++;
            }

            try
            {
                // swPropertyManagerPageShowOptions_e defines only StackPage, and these
                // pages do not stack.
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
            catch
            {
                // A page that never opened never closes, so nothing else would decrement.
                if (counted)
                {
                    _openPages--;
                }

                throw;
            }
        }

        private IPropertyManagerPage2 Build()
        {
            const int pageOptions =
                (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_OkayButton |
                (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_CancelButton |
                (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_LockedPage |
                // Handlers here show and hide controls; without this the page repaints per
                // control and flickers.
                (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_DisablePageBuildDuringHandlers;

            // `ref` because the interop declares it [In, Out], though the help calls it an
            // output. Initialise it.
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

            // Skipped rather than set to "", which would show an empty box.
            if (!string.IsNullOrEmpty(Message))
            {
                page.SetMessage3(
                    Message,
                    (int)swPropertyManagerPageMessageVisibility.swMessageBoxVisible,
                    (int)swPropertyManagerPageMessageExpanded.swMessageBoxExpand,
                    Title);
            }

            BuildControls(page);

            return page;
        }

        // ---- Manager Pane tab -----------------------------------------------

        /// <summary>
        /// Closes and re-shows the page once the current handler has returned, for content
        /// that cannot be written to a shown page.
        /// </summary>
        /// <remarks>
        /// A combobox's item list is the known case: <c>Clear</c> and <c>InsertItem</c> each
        /// kill SOLIDWORKS silently on a shown page. Other writes, such as a label's caption,
        /// are safe - check the measured table in docs/solidworks-api/property-manager-pages.md
        /// before reaching for this.
        ///
        /// Deferred to a later turn of the message pump because the help warns that closing
        /// a page inside its own handler may crash - the same warning behind `LockedPage`.
        ///
        /// The derived page sees no <see cref="PageClosed"/> or tab restore, and
        /// <see cref="LoadControls"/> repopulates from what it is editing, so edits in
        /// progress survive.
        /// </remarks>
        protected void RebuildAfterHandlerReturns()
        {
            if (_rebuildTimer != null || !_isOpen)
            {
                return;
            }

            _rebuildTimer = new System.Windows.Forms.Timer { Interval = 1 };
            _rebuildTimer.Tick += OnRebuildTick;
            _rebuildTimer.Start();
        }

        /// <summary>Entry point 7. Runs on the STA thread, after the handler returned.</summary>
        private void OnRebuildTick(object sender, EventArgs e)
        {
            try
            {
                StopRebuildTimer();
                Rebuild();
            }
            catch (Exception ex)
            {
                Errors.Handle(ex, nameof(RebuildAfterHandlerReturns));
            }
        }

        private void Rebuild()
        {
            if (!_isOpen)
            {
                return;
            }

            _rebuilding = true;

            try
            {
                // Cancel, not OK: the user accepted nothing. AfterClose skips PageClosed
                // while _rebuilding, so nothing is committed or lost.
                _page.Close(false);
                Show();
            }
            finally
            {
                _rebuilding = false;
            }

            _log.Debug("{0}: rebuilt to show changed content.", Title);
        }

        private void StopRebuildTimer()
        {
            if (_rebuildTimer == null)
            {
                return;
            }

            _rebuildTimer.Stop();
            _rebuildTimer.Tick -= OnRebuildTick;
            _rebuildTimer.Dispose();
            _rebuildTimer = null;
        }

        private void RememberManagerPaneTab()
        {
            // Keep the original tab: mid-rebuild, the active one is the PropertyManager's.
            if (_rebuilding)
            {
                return;
            }

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
                // Expected if the document closed while the page was up. Quiet: the wrong
                // tab is a blemish, not a failure.
                Errors.Handle(ex, nameof(RestoreManagerPaneTab), quiet: true);
            }
            finally
            {
                _shownAgainst = null;
                _returnToTab = -1;
            }
        }

        // ---- Lifecycle ------------------------------------------------------

        /// <summary>
        /// True while any G-CAM page is on screen.
        /// </summary>
        /// <remarks>
        /// Static because SOLIDWORKS has one Manager Pane.
        ///
        /// For <c>JobTreeTabs</c>, which clears the job tree's selection when the pane
        /// leaves the G-CAM tab. Showing a page moves the pane, so without this, opening an
        /// operation would hide its toolpath just as it is being edited.
        ///
        /// A rebuild leaves the count alone - see <see cref="_rebuilding"/>.
        /// </remarks>
        public static bool AnyOpen => _openPages > 0;

        private static int _openPages;

        protected sealed override void AfterActivation()
        {
            _isOpen = true;
            PageShown();
        }

        protected sealed override void OnClose(swPropertyManagerPageCloseReasons_e reason)
        {
            // No real work is allowed here - the page is already closing. Record what
            // happened and act on it in AfterClose.
            _isOpen = false;

            // Both ends of a rebuild are skipped; counting only one would leave the count
            // stuck above zero.
            if (!_rebuilding && _openPages > 0)
            {
                _openPages--;
            }

            _closeReason = reason;
        }

        protected sealed override void AfterClose()
        {
            // Not a real close: committing would accept unfinished edits, and restoring the
            // tab would fight the re-show.
            if (_rebuilding)
            {
                return;
            }

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
            return Checked(
                page.AddGroupBox(id, caption, GroupBoxOptions) as IPropertyManagerPageGroup,
                id,
                caption);
        }

        /// <summary>A group box inside a tab rather than directly on the page.</summary>
        protected static IPropertyManagerPageGroup AddGroup(
            IPropertyManagerPageTab tab, int id, string caption)
        {
            return Checked(
                tab.AddGroupBox(id, caption, GroupBoxOptions) as IPropertyManagerPageGroup,
                id,
                caption);
        }

        /// <summary>
        /// A group whose header carries a checkbox, for parameters that can be turned off
        /// without being cleared. Changes arrive at <c>OnGroupCheck</c>.
        /// </summary>
        /// <remarks>
        /// SOLIDWORKS collapses the group when the box is cleared and expands it when set
        /// (IPropertyManagerPageGroup::Checked), so nothing here writes to a shown page and
        /// the controls keep their values.
        ///
        /// The initial state is an option rather than a later assignment because it is
        /// part of the page's shape.
        /// </remarks>
        protected static IPropertyManagerPageGroup AddCheckedGroup(
            IPropertyManagerPageTab tab, int id, string caption, bool isChecked)
        {
            int options =
                (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Visible |
                (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Checkbox |
                (isChecked
                    ? (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Checked |
                      (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Expanded
                    : 0);

            return Checked(tab.AddGroupBox(id, caption, options) as IPropertyManagerPageGroup, id, caption);
        }

        private const int GroupBoxOptions =
            (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Visible |
            (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Expanded;

        private static IPropertyManagerPageGroup Checked(
            IPropertyManagerPageGroup group, int id, string caption)
        {
            if (group == null)
            {
                throw new InvalidOperationException(
                    "AddGroupBox returned null for group " + id + " (" + caption + ").");
            }

            return group;
        }

        /// <summary>
        /// A tab across the top of the page. Groups go inside it rather than on the page.
        /// </summary>
        /// <remarks>
        /// Build-time only: the help says AddTab "cannot be used if the page is already
        /// displayed".
        ///
        /// No bitmap: the help wants a 16x18 file on disk, and an empty string means none.
        /// </remarks>
        protected static IPropertyManagerPageTab AddTab(
            IPropertyManagerPage2 page, int id, string caption)
        {
            var tab = page.AddTab(id, caption, string.Empty, 0) as IPropertyManagerPageTab;

            if (tab == null)
            {
                throw new InvalidOperationException(
                    "AddTab returned null for tab " + id + " (" + caption + ").");
            }

            return tab;
        }

        protected static IPropertyManagerPageTextbox AddTextbox(
            IPropertyManagerPageGroup group, int id, string tip)
        {
            return AddControl<IPropertyManagerPageTextbox>(
                group, id, swPropertyManagerPageControlType_e.swControlType_Textbox, string.Empty, tip);
        }

        /// <summary>
        /// A length box, in whatever units SOLIDWORKS exchanges - see
        /// <see cref="JobPropertyPage"/> for the conversion.
        /// </summary>
        /// <param name="allowNegative">
        /// Off by default, since most lengths are sizes or radii; on for offsets, which
        /// measure both ways from a datum.
        /// </param>
        protected static IPropertyManagerPageNumberbox AddLengthbox(
            IPropertyManagerPageGroup group,
            int id,
            string caption,
            string tip,
            bool visible = true,
            bool allowNegative = false)
        {
            var box = AddControl<IPropertyManagerPageNumberbox>(
                group, id, swPropertyManagerPageControlType_e.swControlType_Numberbox, caption, tip, visible);

            // Units cannot change once the page is shown. The bound is generous, not a
            // machine limit.
            box.SetRange2(
                (int)swNumberboxUnitType_e.swNumberBox_Length,
                Minimum: allowNegative ? -LengthLimit : 0,
                Maximum: LengthLimit,
                Inclusive: true,
                Increment: 1,
                FastIncr: 10,
                SlowIncr: 0.1);

            return box;
        }

        /// <summary>
        /// How far a length box will go, either side of zero.
        /// </summary>
        /// <remarks>
        /// Metres, as the box exchanges them. Only there to keep the range symmetrical.
        /// </remarks>
        private const double LengthLimit = 10000;

        /// <summary>
        /// A number box for something that is not a length - a speed, a feed, a count, an
        /// angle.
        /// </summary>
        /// <remarks>
        /// Unitless: there is no number box unit for mm/min or rpm, so the plain number
        /// goes through unconverted.
        /// </remarks>
        protected static IPropertyManagerPageNumberbox AddNumberbox(
            IPropertyManagerPageGroup group,
            int id,
            string caption,
            string tip,
            double maximum = 1000000,
            double increment = 1,
            bool visible = true)
        {
            var box = AddControl<IPropertyManagerPageNumberbox>(
                group, id, swPropertyManagerPageControlType_e.swControlType_Numberbox, caption, tip, visible);

            box.SetRange2(
                (int)swNumberboxUnitType_e.swNumberBox_UnitlessDouble,
                Minimum: 0,
                Maximum: maximum,
                Inclusive: true,
                Increment: increment,
                FastIncr: increment * 10,
                SlowIncr: increment / 10);

            return box;
        }

        /// <summary>
        /// A push button. Presses arrive at <c>OnButtonPress</c> with this id.
        /// </summary>
        /// <remarks>
        /// The caption is set explicitly as well as through <c>AddControl2</c>, to make
        /// clear that a button, unlike the boxes (see <see cref="AddLabel"/>), displays it.
        /// </remarks>
        protected static IPropertyManagerPageButton AddButton(
            IPropertyManagerPageGroup group, int id, string caption, string tip)
        {
            var button = AddControl<IPropertyManagerPageButton>(
                group, id, swPropertyManagerPageControlType_e.swControlType_Button, caption, tip);

            button.Caption = caption;

            return button;
        }

        protected static IPropertyManagerPageCheckbox AddCheckbox(
            IPropertyManagerPageGroup group, int id, string caption, string tip, bool visible = true)
        {
            return AddControl<IPropertyManagerPageCheckbox>(
                group, id, swPropertyManagerPageControlType_e.swControlType_Checkbox, caption, tip, visible);
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
        /// In <b>dialog units</b>, not pixels. Zero means one row for a single-entity box,
        /// three for a list.
        /// </param>
        /// <param name="mark">
        /// Tells SOLIDWORKS, and later ISelectionMgr::GetSelectedObject6, which box a pick
        /// belongs to, so each box on a page needs its own.
        ///
        /// <b>Must be a power of two.</b> Marks match bitwise, so 3 overlaps 1 and 2 and a
        /// pick lands in several boxes. Checked here because the failure is silent and
        /// surfaces elsewhere - as an operation with no contours while its box shows some.
        /// </param>
        protected static IPropertyManagerPageSelectionbox AddSelectionbox(
            IPropertyManagerPageGroup group,
            int id,
            int mark,
            swSelectType_e[] filters,
            bool singleEntityOnly,
            string tip,
            short height = 0,
            bool visible = true,
            bool wantRowChanges = false)
        {
            if (mark <= 0 || (mark & (mark - 1)) != 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(mark),
                    mark,
                    "A selection box mark must be a power of two (1, 2, 4, 8, …). Marks are " +
                    "matched bitwise, so anything else overlaps another box and their picks " +
                    "run together.");
            }

            var box = AddControl<IPropertyManagerPageSelectionbox>(
                group,
                id,
                swPropertyManagerPageControlType_e.swControlType_Selectionbox,
                string.Empty,
                tip,
                visible);

            box.Height = height > 0
                ? height
                : (singleEntityOnly ? SingleRowSelectionHeight : ListSelectionHeight);
            box.Mark = mark;
            box.SingleEntityOnly = singleEntityOnly;
            box.SetSelectionFilters(filters.Select(f => (int)f).ToArray());

            if (wantRowChanges)
            {
                // The only way to hear the highlighted row change: with this style,
                // OnListboxSelectionChanged reports selection boxes too. Build-time only.
                box.Style |= (int)swPropMgrPageSelectionBoxStyle_e
                    .swPropMgrPageSelectionBoxStyle_WantListboxSelectionChanged;
            }

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
            // AddControl2 requires swControlOptions_Visible explicitly; without it the
            // control is invisible rather than missing.
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
        /// Puts the keyboard focus on a control of the shown page.
        /// </summary>
        /// <remarks>
        /// <b>The only way to stop a selection box being the active one</b> - no call
        /// deactivates a box, so the focus has to go elsewhere.
        ///
        /// False when the page is not up or SOLIDWORKS declined; not worth throwing over.
        /// </remarks>
        protected bool FocusControl(int controlId)
        {
            return _page != null && _isOpen && _page.SetFocus(controlId);
        }

        /// <summary>
        /// Shows or hides a control on a page already on screen. <b>Never on a page about
        /// to be shown</b> - see <see cref="Show"/>.
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
        /// Number boxes, comboboxes, text boxes and selection boxes accept a caption and
        /// ignore it, so a label is the only way to name one.
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
        /// Releases the page. Called from DisconnectFromSW, never from a handler - the
        /// help warns against closing a page from its own callback.
        /// </summary>
        public void Dispose()
        {
            StopRebuildTimer();
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
