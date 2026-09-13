using System;
using System.Linq;
using GCam.Core;
using GCam.Core.Diagnostics;
using GCam.Core.Model;
using GCam.SolidWorks.Selection;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace GCam.SolidWorks.PropertyPages
{
    /// <summary>
    /// Editing surface for a job: what is machined, out of what, and in which coordinate
    /// system.
    /// </summary>
    /// <remarks>
    /// Edits a clone and commits it on OK, the same arrangement as the tool editor. That
    /// is what makes Cancel free: the job the tree is showing has not been touched.
    ///
    /// Every control is created once in <see cref="BuildControls"/> because SOLIDWORKS
    /// will not accept new controls on a page that is already showing. Changing the
    /// stock mode therefore hides and shows controls that already exist.
    /// </remarks>
    public sealed class JobPropertyPage : GCamPropertyPage
    {
        // Groups.
        private const int GroupName = 1;
        private const int GroupModel = 2;
        private const int GroupCoordinateSystem = 3;
        private const int GroupStock = 4;
        private const int GroupMachine = 5;

        // Controls. Ids are page-local, and must be unique across the whole page -
        // duplicates are accepted in silence and the page then misbehaves. Left in
        // blocks with gaps so a field can gain a label without renumbering.
        private const int IdName = 10;
        private const int IdBodies = 20;
        private const int IdBodiesHint = 21;
        private const int IdCoordinateSystem = 30;
        private const int IdCoordinateSystemHint = 31;
        private const int IdStockModeLabel = 40;
        private const int IdStockMode = 41;
        private const int IdTopLabel = 42;
        private const int IdTop = 43;
        private const int IdSideLabel = 44;
        private const int IdSide = 45;
        private const int IdOffsetXLabel = 46;
        private const int IdOffsetX = 47;
        private const int IdOffsetYLabel = 48;
        private const int IdOffsetY = 49;
        private const int IdBottomLabel = 50;
        private const int IdBottom = 51;
        private const int IdWidthLabel = 52;
        private const int IdWidth = 53;
        private const int IdDepthLabel = 54;
        private const int IdDepth = 55;
        private const int IdHeightLabel = 56;
        private const int IdHeight = 57;
        private const int IdWorkOffsetLabel = 60;
        private const int IdWorkOffset = 61;

        // Selection box marks. Each box needs its own so the selection manager can tell
        // a body picked into Model from a coordinate system picked into the box below.
        private const int MarkBodies = 1;
        private const int MarkCoordinateSystem = 2;

        /// <summary>
        /// What IFeature::GetTypeName2 returns for a coordinate system feature.
        /// </summary>
        private const string CoordinateSystemTypeName = "CoordSys";

        private static readonly string[] StockModeNames =
        {
            "Relative box",
            "Relative box (X and Y)",
            "Fixed size box",
        };

        private IPropertyManagerPageTextbox _name;
        private IPropertyManagerPageSelectionbox _bodies;
        private IPropertyManagerPageSelectionbox _coordinateSystem;
        private IPropertyManagerPageCombobox _stockMode;
        private LengthField _top;
        private LengthField _bottom;
        private LengthField _side;
        private LengthField _offsetX;
        private LengthField _offsetY;
        private LengthField _width;
        private LengthField _depth;
        private LengthField _height;
        private IPropertyManagerPageCombobox _workOffset;

        private Job _target;
        private Job _working;

        // True while LoadControls is assigning. Assigning to a combobox or number box
        // fires its change callback, which would write the value straight back and, in
        // the stock mode's case, re-toggle control visibility mid-load.
        private bool _loading;

        public JobPropertyPage(SldWorks swApp, ErrorHandler errors, IGCamLog log)
            : base(swApp, errors, log)
        {
        }

        /// <summary>
        /// Raised when the user clicks OK, after the edits have been written back.
        /// </summary>
        public event EventHandler<Job> Committed;

        protected override string Title => "G-CAM Job";

        protected override string Message =>
            "Choose what to machine, the stock around it, and the coordinate system the " +
            "toolpaths are output in.";

        /// <summary>
        /// Opens the page on a job. The job is not modified unless OK is clicked.
        /// </summary>
        public void Show(Job job)
        {
            _target = job ?? throw new ArgumentNullException(nameof(job));
            _working = job.Clone();

            Show();
        }

        protected override void BuildControls(IPropertyManagerPage2 page)
        {
            var name = AddGroup(page, GroupName, "Name");
            _name = AddTextbox(name, IdName, "What this job is called in the G-CAM tree");

            var model = AddGroup(page, GroupModel, "Model");
            _bodies = AddSelectionbox(
                model, IdBodies, MarkBodies,
                new[] { swSelectType_e.swSelSOLIDBODIES },
                singleEntityOnly: false,
                tip: "Solid bodies to machine. Leave empty to machine every body.");

            AddLabel(model, IdBodiesHint, "Empty machines every solid body.");

            // Its own group rather than a second box under Model: a selection box has no
            // caption of its own, so without a group header there is nothing on screen
            // saying what it is for.
            var csys = AddGroup(page, GroupCoordinateSystem, "Coordinate system");
            _coordinateSystem = AddSelectionbox(
                csys, IdCoordinateSystem, MarkCoordinateSystem,
                new[] { swSelectType_e.swSelCOORDSYS },
                singleEntityOnly: true,
                tip: "Coordinate system feature defining program zero. Leave empty for the part origin.");

            AddLabel(csys, IdCoordinateSystemHint, "Empty uses the part origin.");

            BuildStockGroup(page);

            var machine = AddGroup(page, GroupMachine, "Machine");
            AddLabel(machine, IdWorkOffsetLabel, "Work offset");
            _workOffset = AddCombobox(
                machine, IdWorkOffset, WorkOffsets.Names,
                "Which work offset the toolpaths are output against");
        }

        /// <summary>
        /// The stock fields, with only the current mode's showing.
        /// </summary>
        /// <remarks>
        /// Each field is created already visible or already hidden. The page is rebuilt
        /// for every show, so the right ones are chosen here rather than by toggling
        /// Visible afterwards - which is the call that kills SOLIDWORKS.
        /// </remarks>
        private void BuildStockGroup(IPropertyManagerPage2 page)
        {
            var stock = AddGroup(page, GroupStock, "Stock");

            AddLabel(stock, IdStockModeLabel, "Mode");
            _stockMode = AddCombobox(stock, IdStockMode, StockModeNames, "How the stock is sized");

            StockMode mode = _working?.Stock.Mode ?? StockMode.RelativeBox;
            bool relative = mode != StockMode.FixedSizeBox;

            _top = AddLengthField(
                stock, IdTopLabel, IdTop, "Top offset",
                "Material above the top of the model", relative);

            _side = AddLengthField(
                stock, IdSideLabel, IdSide, "Side offset",
                "Material on all four sides", mode == StockMode.RelativeBox);

            _offsetX = AddLengthField(
                stock, IdOffsetXLabel, IdOffsetX, "X offset",
                "Material to the left and right", mode == StockMode.RelativeBoxXY);

            _offsetY = AddLengthField(
                stock, IdOffsetYLabel, IdOffsetY, "Y offset",
                "Material front and back", mode == StockMode.RelativeBoxXY);

            _bottom = AddLengthField(
                stock, IdBottomLabel, IdBottom, "Bottom offset",
                "Material below the bottom of the model", relative);

            _width = AddLengthField(
                stock, IdWidthLabel, IdWidth, "Width (X)",
                "Absolute stock size in X", mode == StockMode.FixedSizeBox);

            _depth = AddLengthField(
                stock, IdDepthLabel, IdDepth, "Depth (Y)",
                "Absolute stock size in Y", mode == StockMode.FixedSizeBox);

            _height = AddLengthField(
                stock, IdHeightLabel, IdHeight, "Height (Z)",
                "Absolute stock size in Z", mode == StockMode.FixedSizeBox);
        }

        private static LengthField AddLengthField(
            IPropertyManagerPageGroup group,
            int labelId,
            int boxId,
            string label,
            string tip,
            bool visible)
        {
            return new LengthField
            {
                Label = AddLabel(group, labelId, label, visible),
                Box = AddLengthbox(group, boxId, label, tip, visible),
            };
        }

        /// <summary>A number box and the label naming it, shown and hidden together.</summary>
        private sealed class LengthField
        {
            public IPropertyManagerPageLabel Label { get; set; }

            public IPropertyManagerPageNumberbox Box { get; set; }
        }

        protected override void LoadControls()
        {
            _loading = true;

            try
            {
                _name.Text = _working.Name ?? string.Empty;

                _stockMode.CurrentSelection = (short)(int)_working.Stock.Mode;

                _workOffset.CurrentSelection = (short)(_working.WorkOffset - WorkOffsets.First);

                _top.Box.Value = ToBoxLength(_working.Stock.TopOffset);
                _bottom.Box.Value = ToBoxLength(_working.Stock.BottomOffset);
                _side.Box.Value = ToBoxLength(_working.Stock.SideOffset);
                _offsetX.Box.Value = ToBoxLength(_working.Stock.OffsetX);
                _offsetY.Box.Value = ToBoxLength(_working.Stock.OffsetY);
                _width.Box.Value = ToBoxLength(_working.Stock.Width);
                _depth.Box.Value = ToBoxLength(_working.Stock.Depth);
                _height.Box.Value = ToBoxLength(_working.Stock.Height);
            }
            finally
            {
                _loading = false;
            }

            // Visibility is deliberately NOT set here. See PageShown.
        }

        /// <summary>
        /// Runs once the page is on screen. Only work that needs a live page belongs
        /// here.
        /// </summary>
        /// <remarks>
        /// Two things do, for different reasons.
        ///
        /// <b>Visibility must be set on a live page.</b> Setting
        /// IPropertyManagerPageControl.Visible on a page that has been shown and then
        /// closed terminates SOLIDWORKS - no exception, no log, the process simply goes.
        /// The help documents no such restriction; this was found by logging each
        /// statement until one of them stopped coming back. Values are safe to set while
        /// closed, which is why LoadControls still runs before Show2; only the show/hide
        /// waits.
        ///
        /// <b>Selections need the page up too</b>, because SelectByID2 routes by mark and
        /// the marks belong to selection boxes on a page that actually exists.
        /// </remarks>
        /// <summary>
        /// Vets a candidate before it is allowed into a selection box, and names it.
        /// </summary>
        /// <remarks>
        /// Clicking a coordinate system in the graphics area can land on one of its
        /// parts, and the box then reads "CoordinateSystem1\Point". That is only ever
        /// cosmetic here - the object behind it is the coordinate system feature, which
        /// is why the job stores the right name regardless - but it reads like the wrong
        /// thing was picked.
        ///
        /// <paramref name="itemText"/> is the cure. The help buries it: "ItemText is
        /// returned to SOLIDWORKS and stored on the selected object and can be used by
        /// your PropertyManager page selection list boxes for the life of that
        /// selection." Returning the feature's own name makes the box show
        /// "CoordinateSystem1" whichever part of it was clicked.
        ///
        /// Fires on every pre-select hover, so it stays cheap, takes no action and says
        /// nothing.
        /// </remarks>
        protected override bool OnSubmitSelection(
            int id, object selection, int selectionType, out string itemText)
        {
            itemText = null;

            if (id != IdCoordinateSystem)
            {
                return true;
            }

            var feature = selection as Feature;

            if (feature == null
                || !string.Equals(
                    feature.GetTypeName2(), CoordinateSystemTypeName, StringComparison.Ordinal))
            {
                return false;
            }

            itemText = feature.Name;
            return true;
        }

        protected override void PageShown()
        {
            // Selections only. Visibility was settled when the controls were created.
            RestoreSelections();
        }

        protected override void PageClosed(swPropertyManagerPageCloseReasons_e reason)
        {
            if (reason == swPropertyManagerPageCloseReasons_e.swPropertyManagerPageClose_Okay)
            {
                CommitToJob();
                Committed?.Invoke(this, _target);
            }

            // Cancel, Escape, or the document closing underneath the page all land here
            // too; the clone is simply dropped.
            //
            // Clearing last, not first: the edits are read from _working, which the
            // selection callbacks have already filled in, and clearing the selection
            // could otherwise fire one of those callbacks and empty it again.
            ClearSelections();
        }

        /// <summary>
        /// Drops what the page's selection boxes put on screen.
        /// </summary>
        /// <remarks>
        /// The page selects the job's bodies and coordinate system so they are visible
        /// while editing. Those are the page's selections, not the user's, and leaving
        /// them behind means a part still lit up in the graphics area and a coordinate
        /// system still highlighted in the feature tree after the page has gone.
        /// </remarks>
        private void ClearSelections()
        {
            var model = SwApp.ActiveDoc as ModelDoc2;
            model?.ClearSelection2(true);
        }

        // ---- Loading ---------------------------------------------------------

        /// <summary>
        /// Puts the job's saved bodies and coordinate system back into the selection
        /// boxes, reporting anything the part no longer has.
        /// </summary>
        private void RestoreSelections()
        {
            var model = SwApp.ActiveDoc as ModelDoc2;
            if (model == null)
            {
                return;
            }

            model.ClearSelection2(true);

            var missing = JobSelections.SelectBodies(model, _working.ModelBodyNames, MarkBodies);
            if (missing.Count > 0)
            {
                // Not an error dialog: the user is looking at the page and will see the
                // box is short. Selecting by name is why this can happen at all, and the
                // note at Job.ModelBodyNames says what replaces it.
                Log.Warn(
                    "Job '{0}' refers to {1} body/bodies this part no longer has: {2}.",
                    _working.Name,
                    missing.Count,
                    string.Join(", ", missing));
            }

            if (!JobSelections.SelectCoordinateSystem(model, _working.CoordinateSystemName, MarkCoordinateSystem))
            {
                Log.Warn(
                    "Job '{0}' refers to coordinate system '{1}', which this part no longer has.",
                    _working.Name,
                    _working.CoordinateSystemName);
            }
        }

        // ---- Callbacks -------------------------------------------------------

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

        protected override void OnComboboxSelectionChanged(int id, int item)
        {
            if (_loading)
            {
                return;
            }

            if (id == IdStockMode)
            {
                _working.Stock.Mode = (StockMode)item;
                ShowControlsFor(_working.Stock.Mode);
                return;
            }

            if (id == IdWorkOffset)
            {
                _working.WorkOffset = item + WorkOffsets.First;
            }
        }

        protected override void OnNumberboxChanged(int id, double value)
        {
            if (_loading)
            {
                return;
            }

            double mm = FromBoxLength(value);

            switch (id)
            {
                case IdTop: _working.Stock.TopOffset = mm; break;
                case IdBottom: _working.Stock.BottomOffset = mm; break;
                case IdSide: _working.Stock.SideOffset = mm; break;
                case IdOffsetX: _working.Stock.OffsetX = mm; break;
                case IdOffsetY: _working.Stock.OffsetY = mm; break;
                case IdWidth: _working.Stock.Width = mm; break;
                case IdDepth: _working.Stock.Depth = mm; break;
                case IdHeight: _working.Stock.Height = mm; break;
            }
        }

        protected override void OnSelectionboxListChanged(int id, int count)
        {
            // Read as the selection changes rather than at OK. By the time the page is
            // closing the selection manager has been cleared, and the callback is the
            // only moment the contents are reliably there.
            var model = SwApp.ActiveDoc as ModelDoc2;
            if (model == null)
            {
                return;
            }

            if (id == IdBodies)
            {
                _working.ModelBodyNames = JobSelections.NamesWithMark(model, MarkBodies);
                return;
            }

            if (id == IdCoordinateSystem)
            {
                _working.CoordinateSystemName =
                    JobSelections.NamesWithMark(model, MarkCoordinateSystem).FirstOrDefault();
            }
        }

        // ---- Stock mode ------------------------------------------------------

        /// <summary>
        /// Shows the stock fields that belong to a mode and hides the rest.
        /// </summary>
        /// <remarks>
        /// <b>Only ever called when the user changes the mode on a live page.</b> It is
        /// deliberately not called when a page is loaded or shown.
        ///
        /// IPropertyManagerPageControl.Visible is the single most dangerous call in this
        /// file. Setting it on each show killed SOLIDWORKS outright - reproducibly on
        /// the fourth show, from any trigger, with no exception and nothing in the log.
        /// Something accumulates; four was the limit. The page is now rebuilt for every
        /// show and each control is created with the visibility it needs, so the normal
        /// path never touches this property at all.
        /// </remarks>
        private void ShowControlsFor(StockMode mode)
        {
            bool relative = mode != StockMode.FixedSizeBox;

            Show(_top, relative);
            Show(_bottom, relative);
            Show(_side, mode == StockMode.RelativeBox);
            Show(_offsetX, mode == StockMode.RelativeBoxXY);
            Show(_offsetY, mode == StockMode.RelativeBoxXY);

            Show(_width, mode == StockMode.FixedSizeBox);
            Show(_depth, mode == StockMode.FixedSizeBox);
            Show(_height, mode == StockMode.FixedSizeBox);
        }

        private static void Show(LengthField field, bool visible)
        {
            SetVisible(field.Label, visible);
            SetVisible(field.Box, visible);
        }

        // ---- Committing ------------------------------------------------------

        private void CommitToJob()
        {
            _target.Name = _working.Name;
            _target.ModelBodyNames = _working.ModelBodyNames;
            _target.CoordinateSystemName = _working.CoordinateSystemName;
            _target.Stock = _working.Stock;
            _target.WorkOffset = _working.WorkOffset;
        }

        // ---- Units -----------------------------------------------------------

        /// <summary>
        /// Millimetres into whatever a length number box wants.
        /// </summary>
        /// <remarks>
        /// A swNumberBox_Length control exchanges **metres** - SOLIDWORKS' system units,
        /// not the document's display units, even though the box shows and accepts mm.
        /// Measured, not assumed: typing 1 mm read back as 0.001. The help says nothing
        /// either way, so this pair of methods is the one place that knowledge lives.
        ///
        /// This is the conversion the units rule in docs/architecture.md is about. Core
        /// is millimetres throughout; only the edge converts.
        /// </remarks>
        private static double ToBoxLength(double millimetres) => Units.MillimetresToMetres(millimetres);

        private static double FromBoxLength(double boxValue) => Units.MetresToMillimetres(boxValue);
    }
}
