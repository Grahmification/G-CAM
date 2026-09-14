using System;
using System.Collections.Generic;
using System.Linq;
using GCam.Core;
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
    /// Groups in the order HSMWorks uses - Tool, Geometry, Heights, Passes, Linking - so
    /// anyone coming from it finds things where they expect. Tool and Heights are common
    /// to every strategy; Geometry, Passes and Linking are contour2d's own.
    ///
    /// **The strategy-specific halves are built by their own methods here rather than
    /// through a seam.** docs/design/operations.md sketches an `IPageBuilder` the strategy
    /// would contribute through; with one strategy that would be an abstraction with a
    /// single implementation, which the project's own rule says to wait on. The methods are
    /// named and grouped so extracting it when the second strategy lands is mechanical.
    ///
    /// Edits a clone and commits on OK, like <see cref="JobPropertyPage"/>: Cancel leaves
    /// nothing behind, and a new operation that is cancelled was never added to the job.
    /// </remarks>
    public sealed class OperationPropertyPage : GCamPropertyPage
    {
        // Groups.
        private const int GroupName = 1;
        private const int GroupTool = 2;
        private const int GroupGeometry = 3;
        private const int GroupHeights = 4;
        private const int GroupPasses = 5;
        private const int GroupLinking = 6;

        // Name.
        private const int IdName = 10;

        // Tool. Ids are spaced so a control can be added to a group without renumbering.
        private const int IdToolLabel = 20;
        private const int IdToolName = 21;
        private const int IdSpindleRpm = 22;
        private const int IdCuttingFeed = 23;
        private const int IdPlungeFeed = 24;
        private const int IdCoolantLabel = 25;
        private const int IdCoolant = 26;
        private const int IdToolBrowse = 27;

        // Geometry.
        private const int IdContours = 30;
        private const int IdContoursHint = 31;

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

        // Passes.
        private const int IdStockToLeave = 60;
        private const int IdVerticalStockToLeave = 61;
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

        /// <summary>Tells this page's selection box from every other box on the page.</summary>
        private const int MarkContours = 1;

        /// <summary>
        /// What the tool header reads when the operation has no tool.
        /// </summary>
        /// <remarks>
        /// An operation with no tool has to be shown as such: it is the state every new
        /// operation starts in, and on a part with no tools it is the only state until
        /// Browse is used. A control that cannot say "none" ends up displaying a tool the
        /// operation is not using, which is how the first version of this page came to
        /// show one while <see cref="Operation.ToolId"/> was still null.
        /// </remarks>
        private const string NoToolCaption = "No tool chosen";

        /// <summary>
        /// The height modes offered, in the order they appear. Kept beside the captions so
        /// the two cannot drift.
        /// </summary>
        private static readonly HeightMode[] HeightModes =
        {
            HeightMode.FromStockTop,
            HeightMode.FromStockBottom,
            HeightMode.FromModelTop,
            HeightMode.FromModelBottom,
            HeightMode.FromJobOrigin,
            HeightMode.FromSelection,
        };

        private static readonly string[] HeightModeCaptions =
        {
            "Stock top",
            "Stock bottom",
            "Model top",
            "Model bottom",
            "Job origin",
            "Selection",
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

        private IPropertyManagerPageTextbox _name;
        private IPropertyManagerPageLabel _toolName;
        private IPropertyManagerPageButton _toolBrowse;
        private IPropertyManagerPageNumberbox _spindleRpm;
        private IPropertyManagerPageNumberbox _cuttingFeed;
        private IPropertyManagerPageNumberbox _plungeFeed;
        private IPropertyManagerPageCombobox _coolant;
        private IPropertyManagerPageSelectionbox _contours;
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

        private Operation _target;
        private Operation _working;
        private Job _job;

        /// <summary>The part tool the working operation uses, or null. Drives the header.</summary>
        private Tool _currentTool;

        private bool _closing;

        // True while LoadControls is assigning. Setting a combobox or number box fires its
        // change callback, which would write the value straight back - and for the tool
        // combo would re-seed the feeds over the ones just loaded.
        private bool _loading;

        /// <param name="pickToolIntoPart">
        /// Chooses a tool from a library and puts it in the active part, returning the
        /// part's own copy or null if nothing was picked. A delegate rather than a call,
        /// because the browser is WPF in GCam.UI and this project cannot reference it -
        /// the add-in is the only place that sees both halves. Null simply means the page
        /// offers no Browse button.
        /// </param>
        public OperationPropertyPage(
            SldWorks swApp,
            ErrorHandler errors,
            IGCamLog log,
            Func<ModelDoc2> activeDocument,
            Func<JobDocument> jobsForActiveDocument,
            Func<Tool> pickToolIntoPart = null)
            : base(swApp, errors, log)
        {
            _activeDocument = activeDocument ?? throw new ArgumentNullException(nameof(activeDocument));
            _jobsForActiveDocument = jobsForActiveDocument
                                     ?? throw new ArgumentNullException(nameof(jobsForActiveDocument));
            _pickToolIntoPart = pickToolIntoPart;
        }

        /// <summary>Raised when the page is accepted, with the operation as edited.</summary>
        public event EventHandler<Operation> Committed;

        protected override string Title => "G-CAM Operation";

        protected override string Message =>
            "Choose the tool, the profile to follow, and how deep to cut.";

        /// <summary>
        /// Opens the page for an operation, which may or may not be in the job yet.
        /// </summary>
        public void Show(Job job, Operation operation)
        {
            _job = job ?? throw new ArgumentNullException(nameof(job));
            _target = operation ?? throw new ArgumentNullException(nameof(operation));
            _working = operation.Clone();
            _closing = false;

            JobDocument jobs = _jobsForActiveDocument();
            _currentTool = jobs?.FindTool(_working.ToolId);

            Show();
        }

        protected override void BuildControls(IPropertyManagerPage2 page)
        {
            IPropertyManagerPageGroup name = AddGroup(page, GroupName, "Name");
            _name = AddTextbox(name, IdName, "What this operation is called in the tree.");

            _heights.Clear();

            BuildToolGroup(page);
            BuildGeometryGroup(page);
            BuildHeightsGroup(page);
            BuildPassesGroup(page);
            BuildLinkingGroup(page);
        }

        // ---- Common to every strategy ----------------------------------------

        private void BuildToolGroup(IPropertyManagerPage2 page)
        {
            IPropertyManagerPageGroup group = AddGroup(page, GroupTool, "Tool");

            // A header reading the tool's name, with Browse under it - HSMWorks' shape,
            // and the only shape available: a combobox's item list cannot change while
            // the page is shown, and the list of part tools does change, because Browse
            // is what changes it. See the remarks on BrowseForTool.
            AddLabel(group, IdToolLabel, "Tool");

            // Not bolded: IPropertyManagerPageLabel.Bold takes a character range, so it
            // would have to be re-applied every time the caption changes length - more
            // calls on a live page, which is the thing that keeps killing SOLIDWORKS,
            // bought for nothing but weight.
            _toolName = AddLabel(group, IdToolName, NoToolCaption);

            if (_pickToolIntoPart != null)
            {
                _toolBrowse = AddButton(
                    group, IdToolBrowse, "Browse…",
                    "Choose a tool from a library and copy it into this part.");
            }

            _spindleRpm = AddNumberbox(
                group, IdSpindleRpm, "Spindle speed (rpm)",
                "This operation's spindle speed. Seeded from the tool, then its own.",
                maximum: 100000, increment: 100);

            _cuttingFeed = AddNumberbox(
                group, IdCuttingFeed, "Cutting feed (mm/min)",
                "Feed for cutting moves.", maximum: 100000, increment: 10);

            _plungeFeed = AddNumberbox(
                group, IdPlungeFeed, "Plunge feed (mm/min)",
                "Feed straight down. Usually a fraction of the cutting feed.",
                maximum: 100000, increment: 10);

            AddLabel(group, IdCoolantLabel, "Coolant");
            _coolant = AddCombobox(
                group, IdCoolant, Coolants.Select(c => c.ToString()), "Coolant to request.");
        }

        private void BuildHeightsGroup(IPropertyManagerPage2 page)
        {
            IPropertyManagerPageGroup group = AddGroup(page, GroupHeights, "Heights");

            AddHeight(group, "Clearance", IdClearanceLabel, IdClearanceMode, IdClearanceOffset,
                "Where rapids cross, above everything including clamps.");
            AddHeight(group, "Retract", IdRetractLabel, IdRetractMode, IdRetractOffset,
                "Where the tool goes between passes.");
            AddHeight(group, "Feed", IdFeedLabel, IdFeedMode, IdFeedOffset,
                "Where rapid becomes feed on the way down.");
            AddHeight(group, "Top", IdTopLabel, IdTopMode, IdTopOffset,
                "Where cutting starts.");
            AddHeight(group, "Bottom", IdBottomLabel, IdBottomMode, IdBottomOffset,
                "Where cutting stops.");
        }

        /// <summary>One height: a caption, what it is measured from, and how far off.</summary>
        private void AddHeight(
            IPropertyManagerPageGroup group, string caption, int labelId, int modeId, int offsetId,
            string tip)
        {
            AddLabel(group, labelId, caption + " — measured from");

            _heights[modeId] = new HeightField
            {
                Mode = AddCombobox(group, modeId, HeightModeCaptions, tip),
                Offset = AddLengthbox(
                    group, offsetId, caption + " offset",
                    "Distance above that datum. Negative goes below."),
                OffsetId = offsetId,
            };
        }

        /// <summary>A height's two controls, kept together so loading cannot mismatch them.</summary>
        private sealed class HeightField
        {
            public IPropertyManagerPageCombobox Mode { get; set; }

            public IPropertyManagerPageNumberbox Offset { get; set; }

            public int OffsetId { get; set; }
        }

        // ---- 2D contour's own ------------------------------------------------

        private void BuildGeometryGroup(IPropertyManagerPage2 page)
        {
            IPropertyManagerPageGroup group = AddGroup(page, GroupGeometry, "Geometry");

            _contours = AddSelectionbox(
                group,
                IdContours,
                MarkContours,
                new[] { swSelectType_e.swSelEDGES, swSelectType_e.swSelFACES },
                singleEntityOnly: false,
                tip: "Edges or faces forming a closed profile to follow.",
                height: 60);

            AddLabel(
                group, IdContoursHint,
                "Pick edges of a closed profile, or a face to follow its boundary.");
        }

        private void BuildPassesGroup(IPropertyManagerPage2 page)
        {
            IPropertyManagerPageGroup group = AddGroup(page, GroupPasses, "Passes");

            AddLabel(group, IdDirectionLabel, "Direction");
            _direction = AddCombobox(
                group, IdDirection, new[] { "Climb", "Conventional" },
                "Which way round the profile runs.");

            _stockToLeave = AddLengthbox(
                group, IdStockToLeave, "Stock to leave", "Material left on the wall.");
            _verticalStockToLeave = AddLengthbox(
                group, IdVerticalStockToLeave, "Vertical stock to leave",
                "Material left on the floor.");

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
        }

        private void BuildLinkingGroup(IPropertyManagerPage2 page)
        {
            IPropertyManagerPageGroup group = AddGroup(page, GroupLinking, "Linking");

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

                _name.Text = _working.Name ?? string.Empty;

                _toolName.Caption = ToolLabel();
                ShowCuttingData();

                LoadHeight(IdClearanceMode, _working.Heights.Clearance);
                LoadHeight(IdRetractMode, _working.Heights.Retract);
                LoadHeight(IdFeedMode, _working.Heights.Feed);
                LoadHeight(IdTopMode, _working.Heights.Top);
                LoadHeight(IdBottomMode, _working.Heights.Bottom);

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

            field.Mode.CurrentSelection = (short)Math.Max(0, Array.IndexOf(HeightModes, height.Mode));
            field.Offset.Value = ToBoxLength(height.Offset);
        }

        /// <summary>
        /// Runs once the page is on screen.
        /// </summary>
        /// <remarks>
        /// Selections need a live page: SelectByID2 routes by mark, and the marks belong to
        /// selection boxes on a page that actually exists. Values are safe to set while the
        /// page is closed, which is why LoadControls still runs before Show2.
        /// </remarks>
        protected override void PageShown()
        {
            RestoreContourSelection();
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

            foreach (ContourSelection contour in Settings().Contours)
            {
                if (!JobSelections.SelectContour(model, contour, MarkContours))
                {
                    missing.Add(contour.ToString());
                }
            }

            if (missing.Count > 0)
            {
                Log.Warn(
                    "{0} of this operation's contours are no longer in the model: {1}",
                    missing.Count, string.Join(", ", missing));
            }
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
        /// **Nothing here writes to a control, because a shown page does not accept it.**
        /// Three separate calls have each killed SOLIDWORKS outright on their first use -
        /// `Combobox.Clear`, `Combobox.InsertItem` and `Label.Caption` - with no
        /// exception, no log line and no crash report, exactly like
        /// `IPropertyManagerPageControl.Visible`. All verified on 2025 SP3 by bisecting
        /// this method with log lines; see property-manager-pages.md.
        ///
        /// So the tool is shown as a header label with a Browse button under it -
        /// HSMWorks' shape - and picking one **rebuilds the page** through
        /// <see cref="GCamPropertyPage.RebuildAfterHandlerReturns"/> rather than updating
        /// it in place. A drop-down would not have worked whatever the refresh mechanism:
        /// Browse is what adds a tool to the part, so the list of tools necessarily
        /// changes while the page is up.
        ///
        /// The cost is that only a tool reachable through a library can be chosen. Tools
        /// already in the part are re-picked from the library they came from, which
        /// works because <see cref="JobDocument.AddTool"/> is idempotent - but a part
        /// tool whose library has gone cannot be selected at all. The fix is the
        /// part-tool list the browser is meant to grow; see docs/design/operations.md.
        ///
        /// **The tool reaches the part as soon as it is picked, and Cancel does not take
        /// it back.** That is deliberate, and it is what the tool list already means: a
        /// tool is in the carousel whether or not an operation uses it, and an unused one
        /// stays until somebody removes it deliberately
        /// (<see cref="JobDocument.RemoveTool"/>). The same reasoning makes creating a
        /// tool library write immediately - see architecture.md. Holding the tool on the
        /// clone instead would throw it away whenever someone chose a cutter, thought
        /// better of the operation, and cancelled.
        ///
        /// The part's list is re-read rather than appended to, because
        /// <see cref="JobDocument.AddTool"/> is idempotent by <see cref="Tool.Id"/>:
        /// picking a tool the part already has selects the copy that is here rather than
        /// adding a second one, and the returned tool is that copy.
        /// </remarks>
        private void BrowseForTool()
        {
            Tool partTool = _pickToolIntoPart();

            if (partTool == null)
            {
                return;
            }

            _currentTool = partTool;
            _working.UseTool(partTool);

            // Nothing is written into the controls - the header caption and the feed
            // boxes are set by LoadControls when the page is built again. Writing them
            // now is what kills SOLIDWORKS; see the remarks above.
            RebuildAfterHandlerReturns();
        }

        // ---- Reacting --------------------------------------------------------

        protected override void OnButtonPress(int id)
        {
            if (id == IdToolBrowse && _pickToolIntoPart != null)
            {
                BrowseForTool();
            }
        }

        protected override void OnTextboxChanged(int id, string text)
        {
            if (_loading)
            {
                return;
            }

            if (id == IdName)
            {
                _working.Name = text;
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
            }
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
                    break;

                case IdClearanceMode: SetMode(_working.Heights.Clearance, item); break;
                case IdRetractMode: SetMode(_working.Heights.Retract, item); break;
                case IdFeedMode: SetMode(_working.Heights.Feed, item); break;
                case IdTopMode: SetMode(_working.Heights.Top, item); break;
                case IdBottomMode: SetMode(_working.Heights.Bottom, item); break;
            }
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

                case IdClearanceOffset: _working.Heights.Clearance.Offset = FromBoxLength(value); break;
                case IdRetractOffset: _working.Heights.Retract.Offset = FromBoxLength(value); break;
                case IdFeedOffset: _working.Heights.Feed.Offset = FromBoxLength(value); break;
                case IdTopOffset: _working.Heights.Top.Offset = FromBoxLength(value); break;
                case IdBottomOffset: _working.Heights.Bottom.Offset = FromBoxLength(value); break;

                case IdStockToLeave: settings.StockToLeave = FromBoxLength(value); break;
                case IdVerticalStockToLeave: settings.VerticalStockToLeave = FromBoxLength(value); break;
                case IdMaximumStepdown: settings.MultipleDepths.MaximumStepdown = FromBoxLength(value); break;
                case IdTolerance: _working.Tolerance = FromBoxLength(value); break;

                case IdLeadInRadius: settings.LeadIn.Radius = FromBoxLength(value); break;
                case IdLeadOutRadius: settings.LeadOut.Radius = FromBoxLength(value); break;
            }
        }

        protected override void OnSelectionboxListChanged(int id, int count)
        {
            if (_loading || id != IdContours)
            {
                return;
            }

            ModelDoc2 model = _activeDocument();

            if (model == null)
            {
                return;
            }

            Contour2dSettings settings = Settings();

            // Replaced wholesale rather than diffed: the box is the truth about what is
            // selected, and matching up what changed would only be a way to get it wrong.
            settings.Contours.Clear();
            settings.Contours.AddRange(
                JobSelections.ContourSelectionsWithMark(model, MarkContours));
        }

        protected override void PageClosed(swPropertyManagerPageCloseReasons_e reason)
        {
            if (_closing)
            {
                return;
            }

            _closing = true;

            if (reason == swPropertyManagerPageCloseReasons_e.swPropertyManagerPageClose_Okay)
            {
                CommitToOperation();
                Committed?.Invoke(this, _target);
            }
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
        /// <see cref="Operation.Settings"/> is fixed at construction and cannot be
        /// swapped, which is what stops an operation's strategy changing underneath its
        /// stored parameters - so the values move rather than the object.
        /// </remarks>
        private static void CopySettings(Contour2dSettings from, Contour2dSettings to)
        {
            to.Contours.Clear();
            to.Contours.AddRange(from.Contours.Select(c => c.Clone()));

            to.Direction = from.Direction;
            to.StockToLeave = from.StockToLeave;
            to.VerticalStockToLeave = from.VerticalStockToLeave;
            to.LeadOutMatchesLeadIn = from.LeadOutMatchesLeadIn;

            to.MultipleDepths = from.MultipleDepths.Clone();
            to.LeadIn = from.LeadIn.Clone();
            to.LeadOut = from.LeadOut.Clone();
        }

        // ---- Plumbing --------------------------------------------------------

        private Contour2dSettings Settings() => (Contour2dSettings)_working.Settings;

        private static void SetMode(HeightSetting height, int item)
        {
            if (item >= 0 && item < HeightModes.Length)
            {
                height.Mode = HeightModes[item];
            }
        }

        /// <summary>
        /// Millimetres into whatever a length number box wants, and back.
        /// </summary>
        /// <remarks>
        /// A swNumberBox_Length control exchanges metres, whatever the document displays.
        /// Measured, not assumed - see <see cref="JobPropertyPage"/>, where the same pair
        /// lives for the same reason.
        /// </remarks>
        private static double ToBoxLength(double millimetres) => Units.MillimetresToMetres(millimetres);

        private static double FromBoxLength(double boxValue) => Units.MetresToMillimetres(boxValue);

        private static string ToolCaption(Tool tool) =>
            tool.Number > 0 ? "T" + tool.Number + " — " + tool.DisplayName : tool.DisplayName;
    }
}
