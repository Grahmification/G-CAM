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

        // A rebuild is a close and a show that the derived page must not see as either:
        // no commit, no tab restore, no reloading of what it is editing.
        private bool _rebuilding;
        private System.Windows.Forms.Timer _rebuildTimer;

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

        /// <summary>True while the page is on screen and taking part in the UI.</summary>
        protected bool IsOpen => _isOpen;

        /// <summary>
        /// True while the page is being closed and shown again to change what it displays.
        /// </summary>
        /// <remarks>
        /// Callbacks still arrive during a rebuild, and they describe SOLIDWORKS taking the
        /// page apart rather than anything the user did. A page that reads its state back
        /// out of its controls has to ignore them - see
        /// <see cref="RebuildAfterHandlerReturns"/>.
        /// </remarks>
        protected bool IsRebuilding => _rebuilding;

        /// <summary>Text in the page's title bar.</summary>
        protected abstract string Title { get; }

        /// <summary>
        /// The blue explanatory box at the top of the page. Null or empty for no box.
        /// </summary>
        /// <remarks>
        /// Worth leaving empty once a page explains itself: the box costs a chunk of the
        /// panel's height on every show, and a caption nobody reads twice is worse than
        /// the space it takes.
        /// </remarks>
        protected virtual string Message => null;

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

            // Counted *before* Show2 rather than from AfterActivation. Showing a page moves
            // the Manager Pane onto the PropertyManager's tab, and whether that arrives
            // before or after AfterActivation is SOLIDWORKS' business - the job tree reads
            // this from that notification, so it has to be true for the whole of Show2.
            bool counted = !_rebuilding;

            if (counted)
            {
                _openPages++;
            }

            try
            {
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
            catch
            {
                // A page that never opened will never close, so nothing else would put the
                // count back down.
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

            // Skipped entirely rather than set to "", so a page with nothing to say gets
            // no box rather than an empty one.
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
        /// Rebuilds and re-shows the page once the current handler has returned, so it
        /// displays content that changed while it was up.
        /// </summary>
        /// <remarks>
        /// **This is the only way to change what a shown page shows.** A control's
        /// contents are fixed once the page is displayed: a combobox's item list and a
        /// label's caption both kill SOLIDWORKS outright if written to, with nothing in
        /// any log - see docs/solidworks-api/property-manager-pages.md. Since the page is
        /// rebuilt for every show anyway, building it again is cheap and is the sanctioned
        /// shape rather than a workaround.
        ///
        /// **Deferred, never immediate.** Closing the page inside a handler leaves it gone
        /// when the handler returns control to SOLIDWORKS, which the help says may crash -
        /// the same warning that makes these pages `LockedPage`. A one-shot timer moves
        /// the work to a later turn of the message pump, by which time the handler has
        /// returned normally.
        ///
        /// The derived page sees nothing: no <see cref="PageClosed"/>, no tab restore, and
        /// <see cref="LoadControls"/> repopulates from whatever it is editing, so edits in
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
                // Cancel, not Okay: this is not the user accepting anything. AfterClose
                // sees _rebuilding and skips PageClosed, so nothing is committed or lost.
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
            // A rebuild must keep the tab the page was originally opened from. By now
            // the active tab is the PropertyManager's own, so re-reading it would make
            // OK land somewhere the user never was.
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

        /// <summary>
        /// True while any G-CAM page is on screen.
        /// </summary>
        /// <remarks>
        /// Static because it describes something there is only one of: SOLIDWORKS has one
        /// Manager Pane, so "a G-CAM page has it" is a fact about the application rather
        /// than about any page.
        ///
        /// It exists for <c>JobTreeTabs</c>, which clears the job tree's selection when the
        /// pane moves off the G-CAM tab. Showing a page moves the pane onto the
        /// PropertyManager's own tab - that is why <see cref="RememberManagerPaneTab"/>
        /// exists - so without this, opening an operation for editing would take that
        /// operation's toolpath off the screen at exactly the wrong moment.
        ///
        /// A rebuild closes and re-shows a page, and <see cref="_rebuilding"/> keeps the
        /// count from dipping through zero on the way.
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
            // SOLIDWORKS permits no real work here - the page and its command are
            // already closing. Record what happened and act on it in AfterClose.
            _isOpen = false;

            // Both ends skip a rebuild, which closes and re-shows inside one call with
            // _rebuilding held true throughout. Counting one end of it and not the other is
            // how a counter like this ends up stuck above zero for the session.
            if (!_rebuilding && _openPages > 0)
            {
                _openPages--;
            }

            _closeReason = reason;
        }

        protected sealed override void AfterClose()
        {
            // A rebuild closes the page on its way to showing it again. It is not a close
            // the page is entitled to react to: committing here would accept edits the
            // user has not finished, and restoring the tab would fight the re-show.
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
        /// SOLIDWORKS collapses the group when the box is cleared and expands it when it
        /// is set - its own behaviour, documented under IPropertyManagerPageGroup::Checked
        /// - so the contents hide themselves and nothing here writes to a shown page. It
        /// leaves the controls' own states alone, which is exactly what is wanted: the
        /// numbers keep what was typed into them.
        ///
        /// The initial state is an option here rather than an assignment afterwards,
        /// because it is part of the page's shape like everything else.
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
        /// Build-time only, like every other part of a page's shape - the help is explicit
        /// that AddTab "cannot be used if the page is already displayed", which costs
        /// nothing here because these pages are rebuilt for every show anyway.
        ///
        /// No bitmap. The help wants a 16x18 file on disk and treats an empty string as
        /// "no bitmap", which is the behaviour wanted: text tabs, no image assets to
        /// deploy and find at runtime.
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
        /// A length box. Values are in whatever units SOLIDWORKS hands back - see
        /// <see cref="JobPropertyPage"/> for the conversion and why it is measured
        /// rather than assumed.
        /// </summary>
        /// <param name="allowNegative">
        /// Lets the box take a value below zero. Off by default, because most lengths on a
        /// page are sizes or radii and a negative one is meaningless - but an *offset*
        /// measures from a datum in both directions, and a box that will not accept a
        /// minus sign silently contradicts a tip that says "negative goes below".
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

            // Units cannot be changed once the page is shown, so this has to happen here.
            // The bound is deliberately generous rather than a guess at machine capacity.
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
        /// In the units the box exchanges, which for a length box is metres - so this is
        /// not the kilometre-ish limit it reads as. It is here to keep the two ends
        /// symmetrical rather than to police anything.
        /// </remarks>
        private const double LengthLimit = 10000;

        /// <summary>
        /// A number box for something that is not a length - a speed, a feed, a count, an
        /// angle.
        /// </summary>
        /// <remarks>
        /// Unitless on purpose. A length box exchanges metres whatever the document shows
        /// (see <see cref="JobPropertyPage"/>), and there is no equivalent unit type for
        /// mm/min or rpm - so these boxes carry the plain number and nothing converts.
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
        /// The caption is set explicitly as well as passed to <c>AddControl2</c>. A
        /// button is the one control here whose caption really is its visible text -
        /// <see cref="AddLabel"/> records that the boxes ignore theirs - and leaving it
        /// to the shared path invites the wrong conclusion about which of the two is
        /// doing the work.
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
        /// Height in <b>dialog units</b>, not pixels. Zero picks a sensible default: one
        /// row for a single-entity box, three for a list.
        /// </param>
        /// <param name="mark">
        /// Distinguishes this box from every other selection box on the page. It is how
        /// SOLIDWORKS decides which box a click belongs to, and how
        /// ISelectionMgr::GetSelectedObject6 later tells them apart, so each box on a
        /// page needs its own.
        ///
        /// <b>Must be a power of two.</b> The help requires it and marks are matched
        /// bitwise, so 3 overlaps both 1 and 2 - a pick lands in several boxes at once and
        /// reads back as whichever of them answers first. Checked here because the failure
        /// is silent and turns up somewhere else entirely: the first time this was got
        /// wrong, an operation refused to generate for having no contours while its
        /// contour box plainly showed some.
        /// </param>
        protected static IPropertyManagerPageSelectionbox AddSelectionbox(
            IPropertyManagerPageGroup group,
            int id,
            int mark,
            swSelectType_e[] filters,
            bool singleEntityOnly,
            string tip,
            short height = 0,
            bool visible = true)
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
        /// Puts the keyboard focus on a control of the shown page.
        /// </summary>
        /// <remarks>
        /// <b>The only way to stop a selection box being the active one.</b>
        /// <see cref="IPropertyManagerPageSelectionbox.SetSelectionFocus"/> makes a box
        /// active and there is no call that makes none active, so a page that wants
        /// clicks in the graphics area to stop landing in a box has to give the focus to
        /// something else.
        ///
        /// False when the page is not up, or SOLIDWORKS declined - neither is worth
        /// throwing over, because the focus is an convenience and the page works without
        /// it.
        /// </remarks>
        protected bool FocusControl(int controlId)
        {
            return _page != null && _isOpen && _page.SetFocus(controlId);
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
