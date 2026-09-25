using System;
using System.Linq;
using GCam.Core;
using GCam.Core.Abstractions;
using GCam.Core.Diagnostics;
using GCam.Core.Model;
using GCam.Core.Rendering;
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
    /// Edits a clone and commits it on OK, so Cancel leaves the tree's job untouched.
    ///
    /// SOLIDWORKS will not accept new controls on a shown page, so changing the stock
    /// mode hides and shows controls that already exist.
    /// </remarks>
    public sealed class JobPropertyPage : GCamPropertyPage
    {
        // Groups.
        private const int GroupName = 1;
        private const int GroupModel = 2;
        private const int GroupCoordinateSystem = 3;
        private const int GroupStock = 4;
        private const int GroupMachine = 5;

        // Controls. Unique across the whole page - duplicates are accepted in silence.
        // Gaps let a field gain a label without renumbering.
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

        // One mark per box, each a power of two - see AddSelectionbox.
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

        // True while LoadControls assigns: each assignment fires the change callback,
        // which would write the value back and, for the stock mode, toggle visibility
        // mid-load.
        private bool _loading;

        // Set as the page starts closing. ClearSelections fires the selection callback
        // on the way out, which would otherwise put a cancelled job's stock box back.
        private bool _closing;

        private readonly Func<IJobPreview> _preview;

        /// <param name="preview">
        /// Where to show the stock while it is edited. A function because the preview
        /// belongs to a document and this object outlives any one. Null means no preview.
        /// </param>
        public JobPropertyPage(
            SldWorks swApp, ErrorHandler errors, IGCamLog log, Func<IJobPreview> preview = null)
            : base(swApp, errors, log)
        {
            _preview = preview;
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
            _closing = false;

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

            // Its own group because a selection box has no caption; the header names it.
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
        /// Each field is created visible or hidden for the current mode, never toggled
        /// afterwards - see <see cref="GCamPropertyPage.Show"/>.
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
                "Material above the top of the model. Negative sits inside it", relative,
                allowNegative: true);

            _side = AddLengthField(
                stock, IdSideLabel, IdSide, "Side offset",
                "Material on all four sides. Negative sits inside the model",
                mode == StockMode.RelativeBox, allowNegative: true);

            _offsetX = AddLengthField(
                stock, IdOffsetXLabel, IdOffsetX, "X offset",
                "Material to the left and right. Negative sits inside the model",
                mode == StockMode.RelativeBoxXY, allowNegative: true);

            _offsetY = AddLengthField(
                stock, IdOffsetYLabel, IdOffsetY, "Y offset",
                "Material front and back. Negative sits inside the model",
                mode == StockMode.RelativeBoxXY, allowNegative: true);

            _bottom = AddLengthField(
                stock, IdBottomLabel, IdBottom, "Bottom offset",
                "Material below the bottom of the model. Negative sits inside it", relative,
                allowNegative: true);

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

        /// <param name="allowNegative">
        /// True for the offsets, which measure from the model and may legitimately sit
        /// inside it; false for the absolute sizes, where a negative is meaningless.
        /// </param>
        private static LengthField AddLengthField(
            IPropertyManagerPageGroup group,
            int labelId,
            int boxId,
            string label,
            string tip,
            bool visible,
            bool allowNegative = false)
        {
            return new LengthField
            {
                Label = AddLabel(group, labelId, label, visible),
                Box = AddLengthbox(group, boxId, label, tip, visible, allowNegative),
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

            // Visibility is deliberately NOT set here. See BuildStockGroup.
        }

        /// <summary>
        /// Vets a candidate before it is allowed into a selection box, and names it.
        /// </summary>
        /// <remarks>
        /// Clicking a coordinate system can land on one of its parts, so the box reads
        /// "CoordinateSystem1\Point" - only cosmetic, since the feature is still what is
        /// stored. Returning the feature's name as <paramref name="itemText"/>, which the
        /// help says the selection box then displays, fixes it.
        ///
        /// Fires on every pre-select hover, so it stays cheap and silent.
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

        /// <summary>
        /// Runs once the page is on screen. Only work that needs a live page belongs
        /// here.
        /// </summary>
        /// <remarks>
        /// Selections do, because SelectByID2 routes by mark and the marks belong to
        /// selection boxes on a page that actually exists. Visibility does not - it was
        /// settled when the controls were created; see <see cref="BuildStockGroup"/>.
        /// </remarks>
        protected override void PageShown()
        {
            RestoreSelections();

            UpdatePreview();
        }

        protected override void PageClosed(swPropertyManagerPageCloseReasons_e reason)
        {
            // The previewed clone is about to be dropped, so clear the preview first and
            // keep it off. On OK the tree reselects the job and the box comes back from it.
            _closing = true;
            _preview?.Invoke()?.Show(PreviewSelection.Empty);

            if (reason == swPropertyManagerPageCloseReasons_e.swPropertyManagerPageClose_Okay)
            {
                CommitToJob();
                Committed?.Invoke(this, _target);
            }

            // Last: clearing can fire a selection callback that would empty _working before
            // the commit read it.
            ClearSelections();
        }

        /// <summary>
        /// Drops what the page's selection boxes put on screen.
        /// </summary>
        /// <remarks>
        /// These are the page's selections, not the user's. Left behind, the part stays lit
        /// up and the coordinate system highlighted after the page has gone.
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

            var missing = JobSelections.SelectBodies(model, _working.ModelBodies, MarkBodies);
            if (missing.Count > 0)
            {
                // A log line, not a dialog: the user can see the box is short. See
                // Job.ModelBodies for how bodies are identified.
                Log.Warn(
                    "Job '{0}' refers to {1} body/bodies this part no longer has: {2}.",
                    _working.Name,
                    missing.Count,
                    string.Join(", ", missing));
            }

            if (!JobSelections.SelectCoordinateSystem(model, _working.CoordinateSystem, MarkCoordinateSystem))
            {
                Log.Warn(
                    "Job '{0}' refers to coordinate system '{1}', which this part no longer has.",
                    _working.Name,
                    _working.CoordinateSystemDisplayName);
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
                UpdatePreview();
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

            UpdatePreview();
        }

        protected override void OnSelectionboxListChanged(int id, int count)
        {
            // Read now, not at OK: by then the selection manager has been cleared.
            var model = SwApp.ActiveDoc as ModelDoc2;
            if (model == null)
            {
                return;
            }

            if (id == IdBodies)
            {
                _working.ModelBodies = JobSelections.RefsWithMark(
                    model, MarkBodies, GeometryRefKind.Body);
                UpdatePreview();
                return;
            }

            if (id == IdCoordinateSystem)
            {
                _working.CoordinateSystem = JobSelections
                    .RefsWithMark(model, MarkCoordinateSystem, GeometryRefKind.CoordinateSystem)
                    .FirstOrDefault();
                UpdatePreview();
            }
        }

        // ---- Preview ---------------------------------------------------------

        /// <summary>
        /// Redraws the stock box from the edits made so far.
        /// </summary>
        /// <remarks>
        /// Previews the clone, so the tree's job is untouched until OK.
        ///
        /// Runs on every keystroke in a stock field. That is cheap - six
        /// IBody2::GetExtremePoint calls per body and a dozen triangles - but it is why the
        /// preview path is quiet on failure and caches nothing a repaint must rebuild.
        /// </remarks>
        private void UpdatePreview()
        {
            if (_closing)
            {
                return;
            }

            // Stock and origin, not toolpaths: the job is being set up.
            _preview?.Invoke()?.Show(PreviewSelection.ForJob(_working));
        }

        // ---- Stock mode ------------------------------------------------------

        /// <summary>
        /// Shows the stock fields that belong to a mode and hides the rest.
        /// </summary>
        /// <remarks>
        /// <b>Only called when the user changes the mode on a live page</b>, never on load
        /// or show: <c>Visible</c> on a page about to be shown kills SOLIDWORKS - see
        /// <see cref="GCamPropertyPage.Show"/>.
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
            _target.ModelBodies = _working.ModelBodies;
            _target.CoordinateSystem = _working.CoordinateSystem;
            _target.Stock = _working.Stock;
            _target.WorkOffset = _working.WorkOffset;
        }

        // ---- Units -----------------------------------------------------------

        /// <summary>
        /// Millimetres into whatever a length number box wants.
        /// </summary>
        /// <remarks>
        /// A swNumberBox_Length control exchanges **metres**, not the document's display
        /// units, even though it shows mm - measured: typing 1 mm read back as 0.001. The
        /// help is silent, so this pair is where that knowledge lives.
        /// </remarks>
        private static double ToBoxLength(double millimetres) => Units.MillimetresToMetres(millimetres);

        private static double FromBoxLength(double boxValue) => Units.MetresToMillimetres(boxValue);
    }
}
