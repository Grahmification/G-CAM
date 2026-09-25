using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GCam.Core;
using GCam.Core.Abstractions;
using GCam.Core.Diagnostics;
using GCam.Core.Model;
using GCam.Core.Model.Heights;
using GCam.Core.Strategies.Contour2d;
using GCam.Core.Strategies.Shared;
using GCam.Core.Tooling;
using GCam.SolidWorks.Selection;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace GCam.SolidWorks.PropertyPages
{
    /// <summary>
    /// Creating and editing a 2D contour operation.
    /// </summary>
    /// <remarks>
    /// Tabs follow HSMWorks' order - Tool, Geometry, Heights, Passes, Linking. Tool and
    /// Heights are common to every strategy; the rest are contour2d's own.
    ///
    /// The contour2d halves are built by methods here rather than through the
    /// <c>IPageBuilder</c> seam in docs/design/operations.md, which would have a single
    /// implementation until a second strategy lands. They are grouped so extracting it
    /// then is mechanical.
    ///
    /// Edits a clone and commits on OK, like <see cref="JobPropertyPage"/>, so Cancel
    /// leaves nothing behind.
    /// </remarks>
    public sealed class OperationPropertyPage : GCamPropertyPage
    {
        // Groups.
        private const int GroupTool = 2;
        private const int GroupGeometry = 3;
        private const int GroupHeights = 4;
        private const int GroupPasses = 5;
        private const int GroupLinking = 6;
        private const int GroupFeeds = 7;
        private const int GroupStockToLeave = 8;

        // Tabs, numbered clear of the groups and controls: nothing documents whether they
        // share a namespace, and duplicate ids are accepted in silence.
        private const int TabTool = 100;
        private const int TabGeometry = 101;
        private const int TabHeights = 102;
        private const int TabPasses = 103;
        private const int TabLinking = 104;

        // Tool. Ids are spaced so a control can be added to a group without renumbering.
        private const int IdToolName = 21;
        private const int IdToolBrowse = 22;

        // Geometry.
        private const int IdContours = 30;
        private const int IdContoursHint = 31;
        private const int IdContoursReverse = 32;
        private const int IdContoursStatus = 33;
        private const int IdPropagateTangent = 34;
        private const int IdPropagateAlongZ = 35;
        private const int IdTangentialExtensionLabel = 36;
        private const int IdTangentialExtension = 37;

        // Heights: label, mode, offset for each of the five.
        private const int IdClearanceLabel = 40;
        private const int IdClearanceMode = 41;
        private const int IdClearanceOffset = 42;
        private const int IdRetractLabel = 43;
        private const int IdRetractMode = 44;
        private const int IdRetractOffset = 45;
        private const int IdFeedLabel = 46;
        private const int IdFeedMode = 47;
        private const int IdFeedOffset = 48;
        private const int IdTopLabel = 49;
        private const int IdTopMode = 50;
        private const int IdTopOffset = 51;
        private const int IdBottomLabel = 52;
        private const int IdBottomMode = 53;
        private const int IdBottomOffset = 54;

        // Heights: the box each row shows when its mode is "Selection", directly under
        // that row's drop-down.
        private const int IdClearanceSelection = 55;
        private const int IdRetractSelection = 56;
        private const int IdFeedSelection = 57;
        private const int IdTopSelection = 58;
        private const int IdBottomSelection = 59;

        // Passes.
        private const int IdMultipleDepths = 62;
        private const int IdMaximumStepdown = 63;
        private const int IdEvenStepdowns = 64;
        private const int IdDirectionLabel = 65;
        private const int IdDirection = 66;
        private const int IdTolerance = 67;

        // Linking.
        private const int IdLeadIn = 70;
        private const int IdLeadInRadius = 71;
        private const int IdLeadOutSame = 72;
        private const int IdLeadOutRadius = 73;

        // Feed and speed: a label above each control. On the Tool tab, but numbered last
        // so the blocks above keep their gaps.
        private const int IdSpindleRpmLabel = 80;
        private const int IdSpindleRpm = 81;
        private const int IdCuttingFeedLabel = 82;
        private const int IdCuttingFeed = 83;
        private const int IdPlungeFeedLabel = 84;
        private const int IdPlungeFeed = 85;
        private const int IdCoolantLabel = 86;
        private const int IdCoolant = 87;

        // Stock to leave. On the Passes tab, in a group of its own.
        private const int IdStockToLeaveLabel = 90;
        private const int IdStockToLeave = 91;
        private const int IdVerticalStockToLeaveLabel = 92;
        private const int IdVerticalStockToLeave = 93;

        /// <summary>
        /// Tells this page's contour box from every other box on the page. A power of
        /// two, as every mark must be - see <see cref="MarkFor"/>.
        /// </summary>
        private const int MarkContours = 1;

        /// <summary>
        /// A mark for each of the five height boxes: 2, 4, 8, 16, 32.
        /// </summary>
        /// <remarks>
        /// Powers of two, as every mark must be - see
        /// <see cref="GCamPropertyPage.AddSelectionbox"/>. One each rather than shared,
        /// because a hidden box with the same mark still answers.
        /// </remarks>
        private static int MarkFor(HeightKind kind) => 1 << ((int)kind + 1);

        /// <summary>
        /// What the tool header reads when the operation has no tool.
        /// </summary>
        /// <remarks>
        /// Every new operation starts with no tool, so the header must be able to say so
        /// rather than show a tool the operation is not using.
        /// </remarks>
        private const string NoToolCaption = "No tool chosen";

        /// <summary>
        /// The least time between two checks of the contour box, milliseconds.
        /// </summary>
        /// <remarks>
        /// Idle fires continuously; this keeps the check to a couple of COM reads a second,
        /// yet fast enough that a stale highlight is not believed.
        /// </remarks>
        private const int SelectionWatchInterval = 400;

        /// <summary>
        /// The height modes offered, in the order they appear. Kept beside the captions so
        /// the two cannot drift.
        /// </summary>
        /// <remarks>
        /// Each row offers a subset (see <see cref="ModesFor"/>), so a drop-down's index
        /// is into that row's list, never this one.
        /// </remarks>
        private static readonly HeightMode[] HeightModes =
        {
            HeightMode.FromStockTop,
            HeightMode.FromStockBottom,
            HeightMode.FromModelTop,
            HeightMode.FromModelBottom,
            HeightMode.FromJobOrigin,
            HeightMode.FromSelection,
            HeightMode.FromContour,
            HeightMode.FromTop,
            HeightMode.FromRetract,
        };

        private static readonly string[] HeightModeCaptions =
        {
            "Stock top",
            "Stock bottom",
            "Model top",
            "Model bottom",
            "Job origin",
            "Selection",
            "Contour",
            "Top height",
            "Retract height",
        };

        private static readonly CoolantMode[] Coolants =
        {
            CoolantMode.Off,
            CoolantMode.Flood,
            CoolantMode.Mist,
            CoolantMode.ThroughTool,
        };

        private readonly Func<ModelDoc2> _activeDocument;
        private readonly Func<JobDocument> _jobsForActiveDocument;
        private readonly Func<Tool> _pickToolIntoPart;
        private readonly Func<ICutDirectionPreview> _cutDirection;
        private readonly Func<IHeightsPreview> _heightsPreview;

        /// <summary>
        /// The height whose offset box has the focus, and so is drawn filled. Null when
        /// the focus is anywhere else.
        /// </summary>
        private HeightKind? _focusedHeight;

        private IPropertyManagerPageLabel _toolName;
        private IPropertyManagerPageButton _toolBrowse;
        private IPropertyManagerPageNumberbox _spindleRpm;
        private IPropertyManagerPageNumberbox _cuttingFeed;
        private IPropertyManagerPageNumberbox _plungeFeed;
        private IPropertyManagerPageCombobox _coolant;
        private IPropertyManagerPageSelectionbox _contours;
        private IPropertyManagerPageButton _contoursReverse;
        private IPropertyManagerPageLabel _contoursStatus;
        private IPropertyManagerPageCheckbox _propagateTangent;
        private IPropertyManagerPageCheckbox _propagateAlongZ;
        private IPropertyManagerPageNumberbox _tangentialExtension;
        private IPropertyManagerPageCombobox _direction;
        private IPropertyManagerPageNumberbox _stockToLeave;
        private IPropertyManagerPageNumberbox _verticalStockToLeave;
        private IPropertyManagerPageCheckbox _multipleDepths;
        private IPropertyManagerPageNumberbox _maximumStepdown;
        private IPropertyManagerPageCheckbox _evenStepdowns;
        private IPropertyManagerPageNumberbox _tolerance;
        private IPropertyManagerPageCheckbox _leadIn;
        private IPropertyManagerPageNumberbox _leadInRadius;
        private IPropertyManagerPageCheckbox _leadOutSame;
        private IPropertyManagerPageNumberbox _leadOutRadius;

        private readonly Dictionary<int, HeightField> _heights = new Dictionary<int, HeightField>();

        /// <summary>The tabs of the page as currently built, by tab id.</summary>
        /// <remarks>
        /// Rebuilt with the page; a tab from a previous build belongs to a released page.
        /// </remarks>
        private readonly Dictionary<int, IPropertyManagerPageTab> _tabs =
            new Dictionary<int, IPropertyManagerPageTab>();

        /// <summary>Which tab to open on. Survives a rebuild; reset by a fresh show.</summary>
        private int _activeTab = TabTool;

        private Operation _target;
        private Operation _working;
        private Job _job;

        /// <summary>The part tool the working operation uses, or null. Drives the header.</summary>
        private Tool _currentTool;

        /// <summary>
        /// What the propagation checkboxes are currently showing. Keeps a write to a shown
        /// page to the times it would change something, and is what a new pick inherits.
        /// </summary>
        private bool? _shownTangent;
        private bool? _shownAlongZ;

        /// <summary>
        /// How many entities were in the contour box when this page last read it. What
        /// <see cref="CheckContourSelection"/> compares against.
        /// </summary>
        private int _syncedContours;

        /// <summary>True while subscribed to the idle notification that runs the watch.</summary>
        private bool _watchingIdle;

        /// <summary>When the contour box was last checked, from <c>TickCount</c>.</summary>
        private int _lastSelectionCheck;

        /// <summary>
        /// True when the Geometry tab's controls are describing a contour list that has
        /// since changed. Cleared by <see cref="RefreshContourControls"/>, at idle.
        /// </summary>
        private bool _controlsOutOfStep;

        /// <summary>
        /// True when the tab in front has changed and the planes and the active selection
        /// box have not caught up. Cleared by <see cref="ApplyTabChange"/>, at idle.
        /// </summary>
        private bool _tabChanged;

        /// <summary>Selection boxes that have changed and not yet been read.</summary>
        private readonly HashSet<int> _pendingSelections = new HashSet<int>();

        /// <summary>True while a modal dialog of ours is over the page.</summary>
        private bool _browsing;

        private bool _closing;

        // True while LoadControls assigns: each assignment fires the control's change
        // callback, which would write the value straight back.
        private bool _loading;

        /// <param name="pickToolIntoPart">
        /// Chooses a library tool and puts it in the active part, returning the part's copy
        /// or null. A delegate because the browser is WPF in GCam.UI, which this project
        /// cannot reference. Null means no Browse button.
        /// </param>
        /// <param name="cutDirection">
        /// Draws which side of the selected contours the cutter runs on. A function because
        /// the preview belongs to a document and this object outlives any one. Null means
        /// no arrows.
        /// </param>
        /// <param name="heightsPreview">
        /// Draws the heights as planes while the Heights tab is open. A function for the
        /// same reason as <paramref name="cutDirection"/>; null means no planes.
        /// </param>
        public OperationPropertyPage(
            SldWorks swApp,
            ErrorHandler errors,
            IGCamLog log,
            Func<ModelDoc2> activeDocument,
            Func<JobDocument> jobsForActiveDocument,
            Func<Tool> pickToolIntoPart = null,
            Func<ICutDirectionPreview> cutDirection = null,
            Func<IHeightsPreview> heightsPreview = null)
            : base(swApp, errors, log)
        {
            _activeDocument = activeDocument ?? throw new ArgumentNullException(nameof(activeDocument));
            _jobsForActiveDocument = jobsForActiveDocument
                                     ?? throw new ArgumentNullException(nameof(jobsForActiveDocument));
            _pickToolIntoPart = pickToolIntoPart;
            _cutDirection = cutDirection;
            _heightsPreview = heightsPreview;
        }

        /// <summary>Raised when the page is accepted, with the operation as edited.</summary>
        public event EventHandler<Operation> Committed;

        /// <summary>
        /// The operation's own name, so the panel says which one is being edited.
        /// </summary>
        /// <remarks>
        /// Read from the clone, so a new operation not yet in the job is named too. Fixed
        /// at build time, which is safe because the name is not editable here.
        ///
        /// Never empty: a nameless panel looks broken, and failure messages use it too.
        /// </remarks>
        protected override string Title =>
            string.IsNullOrWhiteSpace(_working?.Name) ? "G-CAM Operation" : _working.Name;

        /// <summary>
        /// Opens the page for an operation, which may or may not be in the job yet.
        /// </summary>
        public void Show(Job job, Operation operation)
        {
            _job = job ?? throw new ArgumentNullException(nameof(job));
            _target = operation ?? throw new ArgumentNullException(nameof(operation));
            _working = operation.Clone();
            _closing = false;
            _focusedHeight = null;

            JobDocument jobs = _jobsForActiveDocument();
            _currentTool = jobs?.FindTool(_working.ToolId);

            // A fresh show opens where the work is: Tool for a new operation, which means
            // little until it has one (and is not in the job until OK), and Geometry for an
            // existing one, which is usually reopened to change what it cuts. Here rather
            // than in BuildControls because a rebuild keeps its place.
            _activeTab = job.Operations.Contains(operation) ? TabGeometry : TabTool;

            Show();
        }

        /// <remarks>
        /// Nothing above the tabs: the name is the panel <see cref="Title"/>, and renaming
        /// happens in the job tree.
        /// </remarks>
        protected override void BuildControls(IPropertyManagerPage2 page)
        {
            _heights.Clear();
            _tabs.Clear();

            BuildToolTab(page);
            BuildGeometryTab(page);
            BuildHeightsTab(page);
            BuildPassesTab(page);
            BuildLinkingTab(page);

            ActivateRememberedTab();
        }

        /// <summary>
        /// Opens the page on the tab the user was last on.
        /// </summary>
        /// <remarks>
        /// <see cref="IPropertyManagerPageTab.Activate"/> is build-time only. Without it a
        /// rebuild would throw the user back to the first tab every time they picked a tool.
        /// </remarks>
        private void ActivateRememberedTab()
        {
            IPropertyManagerPageTab tab;

            if (_tabs.TryGetValue(_activeTab, out tab))
            {
                tab.Activate();
            }
        }

        // ---- Common to every strategy ----------------------------------------

        private void BuildToolTab(IPropertyManagerPage2 page)
        {
            IPropertyManagerPageTab tab = AddTab(page, TabTool, "Tool");
            _tabs[TabTool] = tab;

            // Split as in HSMWorks: one physical tool shared across the part, but feeds and
            // speeds that belong to this operation.
            IPropertyManagerPageGroup group = AddGroup(tab, GroupTool, "Tool");

            // A header and Browse rather than a combobox: Browse changes the list of part
            // tools, and a combobox's items cannot change while the page is shown.
            //
            // Not bold: Label.Bold takes a character range, so it would need re-applying on
            // every caption change - more writes to a shown page, for nothing but weight.
            _toolName = AddLabel(group, IdToolName, NoToolCaption);

            if (_pickToolIntoPart != null)
            {
                _toolBrowse = AddButton(
                    group, IdToolBrowse, "Browse…",
                    "Choose a tool from a library and copy it into this part.");
            }

            IPropertyManagerPageGroup feeds = AddGroup(tab, GroupFeeds, "Feed and speed");

            AddLabel(feeds, IdSpindleRpmLabel, "Spindle speed (rpm)");
            _spindleRpm = AddNumberbox(
                feeds, IdSpindleRpm, "Spindle speed (rpm)",
                "This operation's spindle speed. Seeded from the tool, then its own.",
                maximum: 100000, increment: 100);

            AddLabel(feeds, IdCuttingFeedLabel, "Cutting feed (mm/min)");
            _cuttingFeed = AddNumberbox(
                feeds, IdCuttingFeed, "Cutting feed (mm/min)",
                "Feed for cutting moves.", maximum: 100000, increment: 10);

            AddLabel(feeds, IdPlungeFeedLabel, "Plunge feed (mm/min)");
            _plungeFeed = AddNumberbox(
                feeds, IdPlungeFeed, "Plunge feed (mm/min)",
                "Feed straight down. Usually a fraction of the cutting feed.",
                maximum: 100000, increment: 10);

            AddLabel(feeds, IdCoolantLabel, "Coolant");
            _coolant = AddCombobox(
                feeds, IdCoolant, Coolants.Select(c => c.ToString()), "Coolant to request.");
        }

        private void BuildHeightsTab(IPropertyManagerPage2 page)
        {
            IPropertyManagerPageTab tab = AddTab(page, TabHeights, "Heights");
            _tabs[TabHeights] = tab;

            IPropertyManagerPageGroup group = AddGroup(tab, GroupHeights, "Heights");

            AddHeight(group, HeightKind.Clearance, "Clearance",
                IdClearanceLabel, IdClearanceMode, IdClearanceSelection, IdClearanceOffset,
                "Where rapids cross, above everything including clamps.",
                _working.Heights.Clearance);
            AddHeight(group, HeightKind.Retract, "Retract",
                IdRetractLabel, IdRetractMode, IdRetractSelection, IdRetractOffset,
                "Where the tool goes between passes.", _working.Heights.Retract);
            AddHeight(group, HeightKind.Feed, "Feed",
                IdFeedLabel, IdFeedMode, IdFeedSelection, IdFeedOffset,
                "Where rapid becomes feed on the way down.", _working.Heights.Feed);
            AddHeight(group, HeightKind.Top, "Top",
                IdTopLabel, IdTopMode, IdTopSelection, IdTopOffset,
                "Where cutting starts.", _working.Heights.Top);
            AddHeight(group, HeightKind.Bottom, "Bottom",
                IdBottomLabel, IdBottomMode, IdBottomSelection, IdBottomOffset,
                "Where cutting stops.", _working.Heights.Bottom);
        }

        /// <summary>
        /// One height: a caption, what it is measured from, what to measure from when that
        /// is a piece of geometry, and how far off.
        /// </summary>
        /// <remarks>
        /// The selection box is created at the visibility it needs - see
        /// <see cref="GCamPropertyPage.Show"/>. Only a live drop-down change toggles it
        /// (<see cref="ShowSelectionBoxFor"/>).
        /// </remarks>
        private void AddHeight(
            IPropertyManagerPageGroup group,
            HeightKind kind,
            string caption,
            int labelId,
            int modeId,
            int selectionId,
            int offsetId,
            string tip,
            HeightSetting height)
        {
            AddLabel(group, labelId, caption + " — measured from");

            HeightMode[] modes = ModesFor(kind);

            _heights[modeId] = new HeightField
            {
                Kind = kind,
                Modes = modes,
                Mode = AddCombobox(
                    group,
                    modeId,
                    modes.Select(m => HeightModeCaptions[Array.IndexOf(HeightModes, m)]).ToArray(),
                    tip),
                Selection = AddSelectionbox(
                    group,
                    selectionId,
                    MarkFor(kind),
                    new[]
                    {
                        swSelectType_e.swSelFACES,
                        swSelectType_e.swSelEDGES,
                        swSelectType_e.swSelVERTICES,
                    },
                    singleEntityOnly: true,
                    tip: "A flat face, a flat edge or a vertex to measure " + caption.ToLowerInvariant() +
                         " from. Anything that is not at one height is refused.",
                    visible: height.Mode == HeightMode.FromSelection),
                SelectionId = selectionId,
                Offset = AddLengthbox(
                    group, offsetId, caption + " offset",
                    "Distance above that datum. Negative goes below.",
                    allowNegative: true),
                OffsetId = offsetId,
            };
        }

        /// <summary>
        /// The modes one row offers: Contour for the cutting heights only, Top height for
        /// the feed height only, Retract height for the clearance only, and the rest
        /// everywhere.
        /// </summary>
        /// <remarks>
        /// Clearance and retract are crossed between contours, so they must be one plane
        /// for all of them; feed follows the contour only through the top. The same rules
        /// <see cref="OperationHeights.Validate"/> enforces on a file.
        /// </remarks>
        private static HeightMode[] ModesFor(HeightKind kind)
        {
            bool cutting = kind == HeightKind.Top || kind == HeightKind.Bottom;

            return HeightModes
                .Where(m => m != HeightMode.FromContour || cutting)
                .Where(m => m != HeightMode.FromTop || kind == HeightKind.Feed)
                .Where(m => m != HeightMode.FromRetract || kind == HeightKind.Clearance)
                .ToArray();
        }

        /// <summary>A height's controls, kept together so loading cannot mismatch them.</summary>
        private sealed class HeightField
        {
            public HeightKind Kind { get; set; }

            /// <summary>What this row's drop-down offers, in its order.</summary>
            public HeightMode[] Modes { get; set; }

            public IPropertyManagerPageCombobox Mode { get; set; }

            /// <summary>Shown only while the mode is <see cref="HeightMode.FromSelection"/>.</summary>
            public IPropertyManagerPageSelectionbox Selection { get; set; }

            public int SelectionId { get; set; }

            public IPropertyManagerPageNumberbox Offset { get; set; }

            public int OffsetId { get; set; }
        }

        // ---- 2D contour's own ------------------------------------------------

        private void BuildGeometryTab(IPropertyManagerPage2 page)
        {
            IPropertyManagerPageTab tab = AddTab(page, TabGeometry, "Geometry");
            _tabs[TabGeometry] = tab;

            IPropertyManagerPageGroup group = AddGroup(tab, GroupGeometry, "Geometry");

            _contours = AddSelectionbox(
                group,
                IdContours,
                MarkContours,
                new[] { swSelectType_e.swSelEDGES, swSelectType_e.swSelFACES },
                singleEntityOnly: false,
                tip: "Edges or faces to follow. They need not form a closed profile.",
                height: 60,
                wantRowChanges: true);

            AddLabel(
                group, IdContoursHint,
                "Pick edges to follow, or a face to follow its boundary.");

            // Per contour, not per operation: each pick runs as far as its own two boxes
            // say it does, and highlighting a row brings that row's settings back up.
            _propagateTangent = AddCheckbox(
                group, IdPropagateTangent, "Tangential propagation",
                "Follow edges that run smoothly on from this one, forwards only.");
            _propagateAlongZ = AddCheckbox(
                group, IdPropagateAlongZ, "Propagate along Z",
                "Follow joining edges that lie at this one's height, both ways.");

            _contoursReverse = AddButton(
                group, IdContoursReverse, "Reverse",
                "Cut the highlighted contour the other way round, which turns its arrow " +
                "round and puts the cutter on its other side.");

            _contoursStatus = AddLabel(group, IdContoursStatus, string.Empty);

            // Below the per-contour controls because it is not one of them: it applies to
            // every open contour the operation cuts.
            AddLabel(group, IdTangentialExtensionLabel, "Tangential extension");
            _tangentialExtension = AddLengthbox(
                group, IdTangentialExtension, "Tangential extension",
                "Run each open contour on past both of its ends before the cutter is " +
                "offset from it. Negative shortens it; closed contours are unaffected.",
                allowNegative: true);
        }

        private void BuildPassesTab(IPropertyManagerPage2 page)
        {
            IPropertyManagerPageTab tab = AddTab(page, TabPasses, "Passes");
            _tabs[TabPasses] = tab;

            IPropertyManagerPageGroup group = AddGroup(tab, GroupPasses, "Passes");

            AddLabel(group, IdDirectionLabel, "Direction");
            _direction = AddCombobox(
                group, IdDirection, new[] { "Climb", "Conventional" },
                "Which way round the profile runs.");

            _multipleDepths = AddCheckbox(
                group, IdMultipleDepths, "Multiple depths",
                "Take the depth in several passes rather than one.");
            _maximumStepdown = AddLengthbox(
                group, IdMaximumStepdown, "Maximum stepdown", "Deepest axial cut in one pass.");
            _evenStepdowns = AddCheckbox(
                group, IdEvenStepdowns, "Even stepdowns",
                "Spread the depth evenly rather than taking full steps and a thin remainder.");

            _tolerance = AddLengthbox(
                group, IdTolerance, "Tolerance",
                "How far the toolpath may deviate from the model.");

            BuildStockToLeaveGroup(tab);
        }

        /// <summary>
        /// Stock to leave, in a group of its own so the header checkbox can turn it off.
        /// </summary>
        /// <remarks>
        /// A checked group because SOLIDWORKS collapses it when cleared, so the amounts
        /// hide with the setting and come back as typed. The model keeps them either way;
        /// see <see cref="Contour2dSettings.StockToLeaveEnabled"/>.
        /// </remarks>
        private void BuildStockToLeaveGroup(IPropertyManagerPageTab tab)
        {
            IPropertyManagerPageGroup group = AddCheckedGroup(
                tab, GroupStockToLeave, "Stock to leave", Settings().StockToLeaveEnabled);

            // Negative is allowed on both: it cuts past the profile rather than short of
            // it, which is how you take out a cutter running undersize.
            AddLabel(group, IdStockToLeaveLabel, "Radial (wall)");
            _stockToLeave = AddLengthbox(
                group, IdStockToLeave, "Radial", "Material left on the wall. Negative cuts past it.",
                allowNegative: true);

            AddLabel(group, IdVerticalStockToLeaveLabel, "Axial (floor)");
            _verticalStockToLeave = AddLengthbox(
                group, IdVerticalStockToLeave, "Axial",
                "Material left on the floor. Negative cuts below it.",
                allowNegative: true);
        }

        private void BuildLinkingTab(IPropertyManagerPage2 page)
        {
            IPropertyManagerPageTab tab = AddTab(page, TabLinking, "Linking");
            _tabs[TabLinking] = tab;

            IPropertyManagerPageGroup group = AddGroup(tab, GroupLinking, "Linking");

            _leadIn = AddCheckbox(
                group, IdLeadIn, "Lead in",
                "Arc onto the profile, so the entry mark lands off the wall.");
            _leadInRadius = AddLengthbox(
                group, IdLeadInRadius, "Lead-in radius", "Radius of the approach arc.");

            _leadOutSame = AddCheckbox(
                group, IdLeadOutSame, "Lead out same as lead in",
                "Leave the profile the same way it was entered.");
            _leadOutRadius = AddLengthbox(
                group, IdLeadOutRadius, "Lead-out radius", "Radius of the exit arc.");
        }

        // ---- Populating ------------------------------------------------------

        /// <remarks>
        /// From here, never from AfterActivation - see
        /// docs/solidworks-api/property-manager-pages.md.
        /// </remarks>
        protected override void LoadControls()
        {
            _loading = true;

            try
            {
                Contour2dSettings settings = Settings();

                _toolName.Caption = ToolLabel();
                _contoursStatus.Caption = ContourStatus();

                // Nothing is shown yet, so the checkboxes are written unconditionally -
                // which is also what gives ShowContourModifiers something to compare with.
                _shownTangent = null;
                _shownAlongZ = null;
                ShowContourModifiers();

                ShowCuttingData();

                LoadHeight(IdClearanceMode, _working.Heights.Clearance);
                LoadHeight(IdRetractMode, _working.Heights.Retract);
                LoadHeight(IdFeedMode, _working.Heights.Feed);
                LoadHeight(IdTopMode, _working.Heights.Top);
                LoadHeight(IdBottomMode, _working.Heights.Bottom);

                _tangentialExtension.Value = ToBoxLength(settings.TangentialExtensionDistance);

                _direction.CurrentSelection = (short)(settings.Direction == CutDirection.Climb ? 0 : 1);
                _stockToLeave.Value = ToBoxLength(settings.StockToLeave);
                _verticalStockToLeave.Value = ToBoxLength(settings.VerticalStockToLeave);
                _multipleDepths.Checked = settings.MultipleDepths.Enabled;
                _maximumStepdown.Value = ToBoxLength(settings.MultipleDepths.MaximumStepdown);
                _evenStepdowns.Checked = settings.MultipleDepths.UseEvenStepdowns;
                _tolerance.Value = ToBoxLength(_working.Tolerance);

                _leadIn.Checked = settings.LeadIn.Enabled;
                _leadInRadius.Value = ToBoxLength(settings.LeadIn.Radius);
                _leadOutSame.Checked = settings.LeadOutMatchesLeadIn;
                _leadOutRadius.Value = ToBoxLength(settings.LeadOut.Radius);
            }
            finally
            {
                _loading = false;
            }

            // Selections are NOT restored here - they need a live page. See PageShown.
        }

        private void LoadHeight(int modeId, HeightSetting height)
        {
            HeightField field = _heights[modeId];

            field.Mode.CurrentSelection = (short)Math.Max(0, Array.IndexOf(field.Modes, height.Mode));
            field.Offset.Value = ToBoxLength(height.Offset);
        }

        /// <summary>
        /// Runs once the page is on screen.
        /// </summary>
        /// <remarks>
        /// Selections need a live page: SelectByID2 routes by mark, and the marks belong to
        /// selection boxes on a page that actually exists.
        /// </remarks>
        protected override void PageShown()
        {
            // What a pending tab change would do is done below, for the tab being shown.
            _tabChanged = false;

            RestoreContourSelection();
            StartSelectionWatch();
            ShowCutDirection();
            ShowHeights();

            // Last, because restoring the selections moves the focus about. A fresh show
            // opens on the Tool tab, where nothing should be collecting clicks.
            ActivateSelectionForTab();
        }

        /// <summary>
        /// Redraws the height planes, or takes them down when the Heights tab is not the
        /// one in front.
        /// </summary>
        /// <remarks>
        /// Only for the Heights tab: the planes span the part and would bury the contours
        /// the Geometry tab is about.
        ///
        /// Called on every show, tab change, height edit and focus move between the offset
        /// boxes - each changes what is drawn or which plane is filled. A tab change reaches
        /// it at idle, never from inside the click (<see cref="OnTabClicked"/>).
        /// </remarks>
        private void ShowHeights()
        {
            if (_closing)
            {
                return;
            }

            IHeightsPreview preview = _heightsPreview?.Invoke();

            if (preview == null)
            {
                return;
            }

            if (_activeTab != TabHeights)
            {
                preview.Clear();
                return;
            }

            preview.Show(_job, _working, _focusedHeight);
        }

        /// <summary>
        /// Redraws the arrows saying which side of each contour will be cut.
        /// </summary>
        /// <remarks>
        /// Drawn from the working clone, so Cancel leaves the tree's operation untouched.
        ///
        /// Called from <see cref="PageShown"/>, where the contours are restored, and from
        /// whatever changes the arrows: the picks, the cut direction, the propagation
        /// checkboxes and Reverse.
        /// </remarks>
        private void ShowCutDirection()
        {
            if (_closing)
            {
                return;
            }

            _cutDirection?.Invoke()?.Show(_job, _working);
        }

        /// <summary>
        /// Puts the stored contours back into the selection box.
        /// </summary>
        /// <remarks>
        /// A reference that no longer resolves is reported once, here, where the user can
        /// act on it - not on every repaint.
        /// </remarks>
        private void RestoreContourSelection()
        {
            ModelDoc2 model = _activeDocument();

            if (model == null)
            {
                return;
            }

            var missing = new List<string>();

            // Assigning, not reacting: the selection callbacks this fires would rebuild the
            // contour list from a half-filled box.
            _loading = true;

            try
            {
                // **Clear first, or nothing restores.** With a selection-box page up,
                // IEntity::Select4 *deselects* an already-selected entity (per the help),
                // and the picks are still selected from the last close - so restoring onto
                // them empties the box.
                model.ClearSelection2(true);

                foreach (ContourSelection contour in Settings().Contours)
                {
                    if (!JobSelections.SelectContour(model, contour, MarkContours, Log))
                    {
                        missing.Add(contour.ToString());
                    }
                }

                // The height references go back into their own boxes in the same pass,
                // because the clear above took them off screen too.
                foreach (HeightField field in _heights.Values)
                {
                    HeightSetting height = SettingFor(field.Kind);

                    if (height?.Mode != HeightMode.FromSelection || height.Reference == null)
                    {
                        continue;
                    }

                    if (!JobSelections.SelectRef(model, height.Reference, MarkFor(field.Kind), Log))
                    {
                        missing.Add(height.Reference.ToString());
                    }
                }
            }
            finally
            {
                _loading = false;
            }

            // What the watch compares against: everything just put back is a selection
            // this page already knows about.
            _syncedContours = JobSelections.CountWithMark(model, MarkContours);

            if (missing.Count > 0)
            {
                Log.Warn(
                    "{0} of this operation's references are no longer in the model: {1}",
                    missing.Count, string.Join(", ", missing));
            }
        }

        // ---- The geometry ----------------------------------------------------

        /// <summary>What the line under the Reverse button says.</summary>
        /// <remarks>
        /// Numbered from 1, down the box. The rows' text belongs to SOLIDWORKS, so this line
        /// is the only place to say which contour the checkboxes describe and which are
        /// reversed.
        /// </remarks>
        private string ContourStatus()
        {
            List<ContourSelection> contours = Settings().Contours;

            if (contours.Count == 0)
            {
                return "Nothing selected.";
            }

            int row;
            TargetContour(out row);

            string status = "Contour " + (row + 1).ToString(CultureInfo.CurrentCulture) +
                            " of " + contours.Count + ".";

            var reversed = new List<string>();

            for (int i = 0; i < contours.Count; i++)
            {
                if (contours[i] != null && contours[i].Reversed)
                {
                    reversed.Add((i + 1).ToString(CultureInfo.CurrentCulture));
                }
            }

            return reversed.Count == 0
                ? status
                : status + " Reversed: " + string.Join(", ", reversed) + ".";
        }

        /// <summary>
        /// Watches the contour box for a change SOLIDWORKS did not report.
        /// </summary>
        /// <remarks>
        /// <b>Deleting rows does not always raise <c>OnSelectionboxListChanged</c></b> -
        /// measured on 2025 SP3 with the box's own right-click menu. Without this the page
        /// keeps deleted contours highlighted, and OK commits them.
        ///
        /// Compares the count with what this page last read, not with its contour list,
        /// which legitimately drops anything but edges and faces and would otherwise
        /// re-read for ever.
        ///
        /// <b>Driven by idle, never a timer.</b> A WinForms timer ticks inside the
        /// right-click menu's nested message loop and re-enters SOLIDWORKS mid-deletion,
        /// which kills it. <c>OnIdleNotify</c> fires "after all of the messages have been
        /// processed, including posted repaints".
        /// </remarks>
        private void StartSelectionWatch()
        {
            if (_watchingIdle)
            {
                return;
            }

            SwApp.OnIdleNotify += OnIdle;
            _watchingIdle = true;
        }

        private void StopSelectionWatch()
        {
            if (!_watchingIdle)
            {
                return;
            }

            _watchingIdle = false;

            try
            {
                SwApp.OnIdleNotify -= OnIdle;
            }
            catch (Exception ex)
            {
                Errors.Handle(ex, nameof(StopSelectionWatch), quiet: true);
            }
        }

        /// <summary>
        /// An entry point: SOLIDWORKS' idle notification, fired when it has finished
        /// everything it had to do.
        /// </summary>
        /// <remarks>
        /// Cheap, since idle fires continuously: a clock comparison until
        /// <see cref="SelectionWatchInterval"/> has passed, then one count.
        ///
        /// Checks only on the Geometry tab, the one time the contour box can be picked
        /// into, and never with a dialog of ours over the page - redrawing from under a
        /// modal window is what <see cref="BrowseForTool"/> shows to be dangerous.
        /// </remarks>
        private int OnIdle()
        {
            try
            {
                if (!IsOpen || IsRebuilding || _closing || _loading || _browsing)
                {
                    return 0;
                }

                // One thing per turn of the pump, controls last: this is the ordering the
                // silent kill in SelectionChanged is avoided by.
                if (_pendingSelections.Count > 0)
                {
                    ApplyPendingSelections();
                    return 0;
                }

                if (_controlsOutOfStep)
                {
                    RefreshContourControls();
                    return 0;
                }

                if (_tabChanged)
                {
                    ApplyTabChange();
                    return 0;
                }

                if (_activeTab != TabGeometry)
                {
                    return 0;
                }

                int now = System.Environment.TickCount;

                if (unchecked(now - _lastSelectionCheck) < SelectionWatchInterval)
                {
                    return 0;
                }

                _lastSelectionCheck = now;

                CheckContourSelection();
            }
            catch (Exception ex)
            {
                Errors.Handle(ex, nameof(OnIdle), quiet: true);
            }

            return 0;
        }

        private void CheckContourSelection()
        {
            ModelDoc2 model = _activeDocument();

            if (model == null)
            {
                return;
            }

            int count = JobSelections.CountWithMark(model, MarkContours);

            if (count == _syncedContours)
            {
                return;
            }

            Log.Debug(
                "The contour box holds {0} item(s) and nothing said so; re-reading from {1}.",
                count, _syncedContours);

            SelectionChanged(IdContours);
        }

        /// <summary>
        /// The contour the Geometry tab's controls act on: the highlighted row, or the
        /// most recent pick when no row is highlighted.
        /// </summary>
        /// <remarks>
        /// <see cref="IPropertyManagerPageSelectionbox.CurrentSelection"/> is -1 straight
        /// after a pick, so falling back to the last row makes the checkboxes describe the
        /// edge just picked. The status line names it either way.
        /// </remarks>
        private ContourSelection TargetContour(out int row)
        {
            List<ContourSelection> contours = Settings().Contours;

            row = _contours == null ? -1 : _contours.CurrentSelection;

            if (row < 0 || row >= contours.Count)
            {
                row = contours.Count - 1;
            }

            return row >= 0 ? contours[row] : null;
        }

        /// <summary>
        /// Brings the propagation checkboxes up to date with the contour they describe.
        /// </summary>
        /// <remarks>
        /// <b>Written only when the value changes.</b> A checkbox write on a shown page has
        /// not been measured, so it is kept rare: moving between rows that agree writes
        /// nothing.
        /// </remarks>
        private void ShowContourModifiers()
        {
            int row;
            ContourSelection target = TargetContour(out row) ?? new ContourSelection();

            if (_shownTangent != target.PropagateTangent)
            {
                _propagateTangent.Checked = target.PropagateTangent;
                _shownTangent = target.PropagateTangent;
            }

            if (_shownAlongZ != target.PropagateAlongZ)
            {
                _propagateAlongZ.Checked = target.PropagateAlongZ;
                _shownAlongZ = target.PropagateAlongZ;
            }
        }

        /// <summary>
        /// Records a propagation modifier against the contour the checkboxes describe, and
        /// redraws what that changes.
        /// </summary>
        /// <remarks>
        /// With nothing selected the box keeps what the user set; the next pick takes the
        /// defaults and <see cref="ShowContourModifiers"/> brings the box back in step.
        /// </remarks>
        private void SetContourModifier(bool tangent, bool value)
        {
            int row;
            ContourSelection target = TargetContour(out row);

            if (tangent)
            {
                _shownTangent = value;
            }
            else
            {
                _shownAlongZ = value;
            }

            if (target == null)
            {
                return;
            }

            if (tangent)
            {
                target.PropagateTangent = value;
            }
            else
            {
                target.PropagateAlongZ = value;
            }

            // The chain this contour runs into is what changed, so the highlight and its
            // arrow are both out of date.
            ShowCutDirection();
        }

        /// <summary>
        /// Flips the direction of the contour highlighted in the selection box, which puts
        /// the cutter on its other side.
        /// </summary>
        /// <remarks>
        /// A button press does not deactivate the box (verified on 2025 SP3), so the
        /// highlighted row can be read here. With none highlighted the request is refused,
        /// rather than guessing or reversing them all.
        ///
        /// <b>The status line is written in place, not by a rebuild</b> - a label caption is
        /// safe from a button press (verified on 2025 SP3; see
        /// docs/solidworks-api/property-manager-pages.md). That also keeps the row
        /// highlighted, so the same contour can be reversed again.
        /// </remarks>
        private void ReverseHighlightedContour()
        {
            List<ContourSelection> contours = Settings().Contours;
            int row = _contours.CurrentSelection;

            if (row < 0 || row >= contours.Count || contours[row] == null)
            {
                throw new GCamUserException(
                    "Highlight a contour in the list first, then press Reverse.");
            }

            ContourSelection contour = contours[row];
            contour.Reversed = !contour.Reversed;

            Log.Info(
                "Contour {0} of '{1}' now cuts {2}.",
                row + 1, _working.Name, contour.Reversed ? "reversed" : "forwards");

            _contoursStatus.Caption = ContourStatus();

            ShowCutDirection();
        }

        // ---- The tool --------------------------------------------------------

        /// <summary>What the tool header reads for the working operation.</summary>
        private string ToolLabel() =>
            _currentTool == null ? NoToolCaption : ToolCaption(_currentTool);

        /// <summary>
        /// Puts the working operation's feeds, speed and coolant into their boxes.
        /// </summary>
        /// <remarks>
        /// The caller holds <c>_loading</c>: every assignment here fires a change
        /// callback that would write the value straight back.
        /// </remarks>
        private void ShowCuttingData()
        {
            _spindleRpm.Value = _working.Cutting.SpindleRpm;
            _cuttingFeed.Value = _working.Cutting.CuttingFeed;
            _plungeFeed.Value = _working.Cutting.PlungeFeed;
            _coolant.CurrentSelection =
                (short)Math.Max(0, Array.IndexOf(Coolants, _working.Cutting.Coolant));
        }

        /// <summary>
        /// Picks a tool from a library, puts it in the part, and selects it here.
        /// </summary>
        /// <remarks>
        /// **Nothing is written to the page after the browser closes; it is rebuilt
        /// instead** (<see cref="GCamPropertyPage.RebuildAfterHandlerReturns"/>). Bisected
        /// on 2025 SP3, <c>Combobox.Clear</c>, <c>Combobox.InsertItem</c> and even
        /// <c>Label.Caption</c> each killed SOLIDWORKS silently here on first use. The
        /// caption is safe elsewhere, so the modal WPF window is implicated and the exact
        /// cause is unmeasured; see property-manager-pages.md.
        ///
        /// Only a tool reachable through a library can be chosen; a part tool whose library
        /// has gone cannot. See docs/design/operations.md.
        ///
        /// **The tool stays in the part even on Cancel**, like any unused tool (see
        /// <see cref="JobDocument.RemoveTool"/>). Holding it on the clone would lose it
        /// whenever someone picked a cutter and then cancelled.
        ///
        /// <see cref="JobDocument.AddTool"/> is idempotent by <see cref="Tool.Id"/>, so
        /// re-picking a tool the part already has returns the part's copy.
        /// </remarks>
        private void BrowseForTool()
        {
            Tool partTool;

            // Nothing of ours may run while the browser is over the page - see OnIdle.
            _browsing = true;

            try
            {
                partTool = _pickToolIntoPart();
            }
            finally
            {
                _browsing = false;
            }

            if (partTool == null)
            {
                return;
            }

            _currentTool = partTool;
            _working.UseTool(partTool);

            // LoadControls fills the header and feeds on the rebuild - see the remarks above.
            RebuildAfterHandlerReturns();
        }

        // ---- Reacting --------------------------------------------------------

        /// <summary>
        /// Remembers which tab the user moved to, so a rebuild comes back to it.
        /// </summary>
        /// <remarks>
        /// Returning true lets the click through. The id is kept rather than the tab
        /// object, which belongs to a build about to be thrown away.
        ///
        /// <b>Only a note of the new tab; the planes and the focus follow at idle</b>
        /// (<see cref="ApplyTabChange"/>). Redrawing the planes from in here left the page
        /// stuck on the tab it was leaving, intermittently: the strip kept moving but the
        /// controls never changed again. See docs/solidworks-api/property-manager-pages.md.
        /// </remarks>
        protected override bool OnTabClicked(int id)
        {
            _activeTab = id;

            // Leaving the Heights tab with a box focused would otherwise leave that plane
            // filled the next time the tab came back.
            _focusedHeight = null;

            _tabChanged = true;
            return true;
        }

        /// <summary>
        /// Catches the planes and the active selection box up with the tab now in front,
        /// once SOLIDWORKS has finished switching to it.
        /// </summary>
        private void ApplyTabChange()
        {
            _tabChanged = false;

            ShowHeights();
            ActivateSelectionForTab();
        }

        /// <summary>
        /// Makes the selection box belonging to the tab in front the active one, so a
        /// click in the graphics area lands where the user is looking.
        /// </summary>
        /// <remarks>
        /// <b>A selection box stays active across a tab change</b>, because tabs hide
        /// controls rather than create them - so a face picked for a height would land in
        /// the contour box.
        ///
        /// Nothing deactivates a box (see <see cref="GCamPropertyPage.FocusControl"/>), so a
        /// tab without a box of its own parks the focus on an ordinary control, which makes
        /// SOLIDWORKS drop the box.
        /// </remarks>
        private void ActivateSelectionForTab()
        {
            if (_activeTab == TabGeometry)
            {
                _contours?.SetSelectionFocus();
                return;
            }

            if (_activeTab == TabHeights && ActivateHeightSelection())
            {
                return;
            }

            FocusControl(FirstControlOf(_activeTab));
        }

        /// <summary>
        /// Activates the box of the first height measured from geometry, if there is one.
        /// </summary>
        /// <remarks>
        /// In practice at most one is visible. With none, the caller moves the focus
        /// instead.
        /// </remarks>
        private bool ActivateHeightSelection()
        {
            foreach (HeightField field in _heights.Values)
            {
                if (SettingFor(field.Kind)?.Mode == HeightMode.FromSelection)
                {
                    field.Selection?.SetSelectionFocus();
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// A control on each tab that is safe to park the focus on - never a selection
        /// box, and never a height offset box, whose focus fills a plane.
        /// </summary>
        private static int FirstControlOf(int tab)
        {
            switch (tab)
            {
                case TabHeights: return IdClearanceMode;
                case TabPasses: return IdDirection;
                case TabLinking: return IdLeadInRadius;
                default: return IdSpindleRpm;
            }
        }

        /// <summary>
        /// Fills the plane of whichever height offset box the user is in.
        /// </summary>
        /// <remarks>
        /// The offset box only, not the mode drop-down: number boxes report focus most
        /// predictably, and the offset is what "editing this height" means.
        ///
        /// Moving between boxes raises a loss and a gain in no guaranteed order, so a loss
        /// clears the fill only if it is still that height's.
        /// </remarks>
        protected override void OnGainedFocus(int id)
        {
            HeightKind kind;

            if (HeightOffsetBox(id, out kind))
            {
                _focusedHeight = kind;
                ShowHeights();
            }
        }

        protected override void OnLostFocus(int id)
        {
            HeightKind kind;

            if (HeightOffsetBox(id, out kind) && _focusedHeight == kind)
            {
                _focusedHeight = null;
                ShowHeights();
            }
        }

        private static bool HeightOffsetBox(int id, out HeightKind kind)
        {
            switch (id)
            {
                case IdClearanceOffset: kind = HeightKind.Clearance; return true;
                case IdRetractOffset: kind = HeightKind.Retract; return true;
                case IdFeedOffset: kind = HeightKind.Feed; return true;
                case IdTopOffset: kind = HeightKind.Top; return true;
                case IdBottomOffset: kind = HeightKind.Bottom; return true;
            }

            kind = HeightKind.Clearance;
            return false;
        }

        protected override void OnButtonPress(int id)
        {
            if (id == IdToolBrowse && _pickToolIntoPart != null)
            {
                BrowseForTool();
            }
            else if (id == IdContoursReverse)
            {
                ReverseHighlightedContour();
            }
        }

        /// <remarks>
        /// Only the flag: the amounts are kept, and SOLIDWORKS collapses the group itself.
        /// </remarks>
        protected override void OnGroupCheck(int id, bool isChecked)
        {
            if (_loading)
            {
                return;
            }

            if (id == GroupStockToLeave)
            {
                Settings().StockToLeaveEnabled = isChecked;
            }
        }

        protected override void OnCheckboxCheck(int id, bool value)
        {
            if (_loading)
            {
                return;
            }

            Contour2dSettings settings = Settings();

            switch (id)
            {
                case IdMultipleDepths: settings.MultipleDepths.Enabled = value; break;
                case IdEvenStepdowns: settings.MultipleDepths.UseEvenStepdowns = value; break;
                case IdLeadIn: settings.LeadIn.Enabled = value; break;
                case IdLeadOutSame: settings.LeadOutMatchesLeadIn = value; break;

                case IdPropagateTangent: SetContourModifier(tangent: true, value: value); break;
                case IdPropagateAlongZ: SetContourModifier(tangent: false, value: value); break;
            }
        }

        /// <summary>
        /// The user has highlighted a different contour, so the controls beneath the box
        /// are about a different contour too.
        /// </summary>
        /// <remarks>
        /// Reaches a selection box only because it was created with
        /// <c>swPropMgrPageSelectionBoxStyle_WantListboxSelectionChanged</c>; without it
        /// nothing reports a row change.
        /// </remarks>
        protected override void OnListboxSelectionChanged(int id, int item)
        {
            if (_loading || !IsOpen || IsRebuilding || _closing || id != IdContours)
            {
                return;
            }

            ShowContourModifiers();
            _contoursStatus.Caption = ContourStatus();
        }

        protected override void OnComboboxSelectionChanged(int id, int item)
        {
            if (_loading)
            {
                return;
            }

            Contour2dSettings settings = Settings();

            switch (id)
            {
                case IdCoolant:
                    if (item >= 0 && item < Coolants.Length)
                    {
                        _working.Cutting.Coolant = Coolants[item];
                    }

                    break;

                case IdDirection:
                    settings.Direction = item == 0 ? CutDirection.Climb : CutDirection.Conventional;
                    ShowCutDirection();
                    break;

                case IdClearanceMode: SetMode(_working.Heights.Clearance, id, item); break;
                case IdRetractMode: SetMode(_working.Heights.Retract, id, item); break;
                case IdFeedMode: SetMode(_working.Heights.Feed, id, item); break;
                case IdTopMode: SetMode(_working.Heights.Top, id, item); break;
                case IdBottomMode: SetMode(_working.Heights.Bottom, id, item); break;
            }

            // A datum change moves a plane as surely as an offset does.
            ShowHeights();
        }

        protected override void OnNumberboxChanged(int id, double value)
        {
            if (_loading)
            {
                return;
            }

            Contour2dSettings settings = Settings();

            switch (id)
            {
                case IdSpindleRpm: _working.Cutting.SpindleRpm = value; break;
                case IdCuttingFeed: _working.Cutting.CuttingFeed = value; break;
                case IdPlungeFeed: _working.Cutting.PlungeFeed = value; break;

                case IdClearanceOffset:
                    _working.Heights.Clearance.Offset = FromBoxLength(value);
                    ShowHeights();
                    break;

                case IdRetractOffset:
                    _working.Heights.Retract.Offset = FromBoxLength(value);
                    ShowHeights();
                    break;

                case IdFeedOffset:
                    _working.Heights.Feed.Offset = FromBoxLength(value);
                    ShowHeights();
                    break;

                case IdTopOffset:
                    _working.Heights.Top.Offset = FromBoxLength(value);
                    ShowHeights();
                    break;

                case IdBottomOffset:
                    _working.Heights.Bottom.Offset = FromBoxLength(value);
                    ShowHeights();
                    break;

                case IdTangentialExtension:
                    settings.TangentialExtensionDistance = FromBoxLength(value);
                    break;

                case IdStockToLeave: settings.StockToLeave = FromBoxLength(value); break;
                case IdVerticalStockToLeave: settings.VerticalStockToLeave = FromBoxLength(value); break;
                case IdMaximumStepdown: settings.MultipleDepths.MaximumStepdown = FromBoxLength(value); break;
                case IdTolerance: _working.Tolerance = FromBoxLength(value); break;

                case IdLeadInRadius: settings.LeadIn.Radius = FromBoxLength(value); break;
                case IdLeadOutRadius: settings.LeadOut.Radius = FromBoxLength(value); break;
            }
        }

        /// <remarks>
        /// **Only the user's doing while the page is up and staying up.** SOLIDWORKS
        /// empties the boxes as it takes a page apart, rebuilds included, and those
        /// callbacks look exactly like the user clearing them. Taken at face value they
        /// would commit an empty list on OK, or leave <see cref="PageShown"/> nothing to
        /// restore. The clone already holds the selections, so ignoring them loses nothing.
        /// </remarks>
        protected override void OnSelectionboxListChanged(int id, int count)
        {
            if (_loading || !IsOpen || IsRebuilding || _closing)
            {
                return;
            }

            // **Only a note of which box changed.** The help says this arrives mid-way
            // through SOLIDWORKS' own selection processing and an add-in may only query,
            // never act - so the work happens at idle. See OnIdle.
            _pendingSelections.Add(id);
        }

        private void ApplyPendingSelections()
        {
            var ids = _pendingSelections.ToList();
            _pendingSelections.Clear();

            foreach (int id in ids)
            {
                SelectionChanged(id);
            }
        }

        /// <summary>
        /// Reads a selection box's contents onto the working operation, once SOLIDWORKS
        /// has finished with the selection that changed.
        /// </summary>
        private void SelectionChanged(int id)
        {
            if (_loading || !IsOpen || IsRebuilding || _closing)
            {
                return;
            }

            ModelDoc2 model = _activeDocument();

            if (model == null)
            {
                return;
            }

            HeightField height = _heights.Values.FirstOrDefault(h => h.SelectionId == id);

            if (height != null)
            {
                HeightReferencePicked(model, height);
                return;
            }

            if (id != IdContours)
            {
                return;
            }

            Contour2dSettings settings = Settings();

            // Replaced wholesale: the box is the truth about what is selected. The modifiers
            // are this page's own state, so they are carried across by entity - otherwise
            // picking one more edge, or a rebuild restoring the selection, would un-reverse
            // every contour.
            Dictionary<string, ContourSelection> before = settings.Contours
                .Where(c => !string.IsNullOrEmpty(c?.Entity?.PersistentId))
                .GroupBy(c => c.Entity.PersistentId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            settings.Contours.Clear();

            foreach (ContourSelection picked in
                     JobSelections.ContourSelectionsWithMark(model, MarkContours))
            {
                if (!string.IsNullOrEmpty(picked?.Entity?.PersistentId)
                    && before.TryGetValue(picked.Entity.PersistentId, out ContourSelection kept))
                {
                    picked.Reversed = kept.Reversed;
                    picked.PropagateTangent = kept.PropagateTangent;
                    picked.PropagateAlongZ = kept.PropagateAlongZ;
                }
                else if (picked != null)
                {
                    // A new pick takes what the checkboxes show, so a run of edges picked
                    // the same way needs no setting afterwards.
                    picked.PropagateTangent = _shownTangent ?? picked.PropagateTangent;
                    picked.PropagateAlongZ = _shownAlongZ ?? picked.PropagateAlongZ;
                }

                settings.Contours.Add(picked);
            }

            _syncedContours = JobSelections.CountWithMark(model, MarkContours);

            // **The 3D view now, the controls on a later turn of the pump.** Touching a
            // control in the same turn as an unreported deletion silently stops execution,
            // losing whatever follows - so the graphics go first and OnIdle does the rest.
            ShowCutDirection();

            _controlsOutOfStep = true;
        }

        /// <summary>
        /// Brings the Geometry tab's controls back in step with the contour list, on a
        /// turn of the message pump of its own.
        /// </summary>
        private void RefreshContourControls()
        {
            _controlsOutOfStep = false;

            ShowContourModifiers();
            _contoursStatus.Caption = ContourStatus();
        }

        /// <summary>
        /// Records what a height is now measured from, and redraws its plane.
        /// </summary>
        /// <remarks>
        /// An emptied box leaves the reference null, as for a height just switched to
        /// Selection: no plane appears and generation refuses the operation by name -
        /// better than keeping a pick the box no longer shows.
        /// </remarks>
        private void HeightReferencePicked(ModelDoc2 model, HeightField field)
        {
            HeightSetting height = SettingFor(field.Kind);

            if (height == null)
            {
                return;
            }

            height.Reference = JobSelections.RefWithMark(model, MarkFor(field.Kind), Log);

            ShowHeights();
        }

        private HeightSetting SettingFor(HeightKind kind) => _working.Heights.For(kind);

        /// <remarks>
        /// Commits first and clears the selection last, as <see cref="JobPropertyPage"/>
        /// does: clearing can fire a selection callback that would empty the clone
        /// (<c>_closing</c> guards that too).
        ///
        /// The selection is dropped because these are the page's picks, not the user's.
        /// </remarks>
        protected override void PageClosed(swPropertyManagerPageCloseReasons_e reason)
        {
            if (_closing)
            {
                return;
            }

            _closing = true;

            StopSelectionWatch();

            // Before the commit: the previews describe the clone about to be dropped. On OK
            // the tree reselects the operation and its toolpath comes up.
            _cutDirection?.Invoke()?.Clear();
            _heightsPreview?.Invoke()?.Clear();

            if (reason == swPropertyManagerPageCloseReasons_e.swPropertyManagerPageClose_Okay)
            {
                CommitToOperation();
                Committed?.Invoke(this, _target);
            }

            _activeDocument()?.ClearSelection2(true);
        }

        // ---- Committing ------------------------------------------------------

        /// <summary>
        /// Copies the edited clone onto the operation the caller handed in.
        /// </summary>
        /// <remarks>
        /// Field by field rather than replacing the object, because the tree, the preview
        /// and the job's operation list all hold the original reference.
        /// </remarks>
        private void CommitToOperation()
        {
            _target.Name = _working.Name;
            _target.Comment = _working.Comment;
            _target.Enabled = _working.Enabled;
            _target.ToolId = _working.ToolId;
            _target.Cutting = _working.Cutting;
            _target.Heights = _working.Heights;
            _target.Frame = _working.Frame;
            _target.Tolerance = _working.Tolerance;

            CopySettings((Contour2dSettings)_working.Settings, (Contour2dSettings)_target.Settings);
        }

        /// <summary>
        /// Copies the strategy's parameters across.
        /// </summary>
        /// <remarks>
        /// <see cref="Operation.Settings"/> cannot be swapped - that is what keeps the
        /// strategy fixed - so the values move rather than the object.
        /// </remarks>
        private static void CopySettings(Contour2dSettings from, Contour2dSettings to)
        {
            to.Contours.Clear();
            to.Contours.AddRange(from.Contours.Select(c => c.Clone()));

            to.Direction = from.Direction;
            to.TangentialExtensionDistance = from.TangentialExtensionDistance;
            to.StockToLeaveEnabled = from.StockToLeaveEnabled;
            to.StockToLeave = from.StockToLeave;
            to.VerticalStockToLeave = from.VerticalStockToLeave;
            to.LeadOutMatchesLeadIn = from.LeadOutMatchesLeadIn;

            to.MultipleDepths = from.MultipleDepths.Clone();
            to.LeadIn = from.LeadIn.Clone();
            to.LeadOut = from.LeadOut.Clone();
        }

        // ---- Plumbing --------------------------------------------------------

        private Contour2dSettings Settings() => (Contour2dSettings)_working.Settings;

        private void SetMode(HeightSetting height, int modeId, int item)
        {
            HeightField field;

            if (!_heights.TryGetValue(modeId, out field) || item < 0 || item >= field.Modes.Length)
            {
                return;
            }

            height.Mode = field.Modes[item];
            ShowSelectionBoxFor(modeId, height.Mode);
        }

        /// <summary>
        /// Shows or hides one height's selection box, to match the datum just chosen.
        /// </summary>
        /// <remarks>
        /// <b>Only reached from a live drop-down change</b>
        /// (<see cref="OnComboboxSelectionChanged"/> returns early while
        /// <see cref="_loading"/>), which is what makes <c>Visible</c> safe here - see
        /// <see cref="GCamPropertyPage.Show"/>. The same arrangement as
        /// <c>JobPropertyPage.ShowControlsFor</c>.
        ///
        /// The reference and the box's contents survive switching away from Selection and
        /// back, so changing your mind twice does not cost the pick.
        /// </remarks>
        private void ShowSelectionBoxFor(int modeId, HeightMode mode)
        {
            HeightField field;

            if (!_heights.TryGetValue(modeId, out field))
            {
                return;
            }

            bool wanted = mode == HeightMode.FromSelection;

            SetVisible(field.Selection, wanted);

            if (wanted)
            {
                // Choosing Selection is the ask to pick, so the new box takes the clicks
                // rather than whichever box was active before.
                field.Selection?.SetSelectionFocus();
            }
            else
            {
                // Off the hidden box, or the graphics area would keep picking into it.
                FocusControl(modeId);
            }
        }

        /// <summary>
        /// Millimetres into whatever a length number box wants, and back.
        /// </summary>
        /// <remarks>
        /// A swNumberBox_Length control exchanges metres whatever the document displays -
        /// measured; see <see cref="JobPropertyPage"/>, which has the same pair.
        /// </remarks>
        private static double ToBoxLength(double millimetres) => Units.MillimetresToMetres(millimetres);

        private static double FromBoxLength(double boxValue) => Units.MetresToMillimetres(boxValue);

        private static string ToolCaption(Tool tool) =>
            tool.Number > 0 ? "T" + tool.Number + " — " + tool.DisplayName : tool.DisplayName;
    }
}
