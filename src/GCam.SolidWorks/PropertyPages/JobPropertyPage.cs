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
        private const int GroupStock = 3;
        private const int GroupMachine = 4;

        // Controls. Ids are page-local and referenced nowhere else.
        private const int IdName = 10;
        private const int IdBodies = 20;
        private const int IdBodiesHint = 21;
        private const int IdCoordinateSystem = 22;
        private const int IdCoordinateSystemHint = 23;
        private const int IdStockMode = 30;
        private const int IdTop = 31;
        private const int IdBottom = 32;
        private const int IdSide = 33;
        private const int IdOffsetX = 34;
        private const int IdOffsetY = 35;
        private const int IdWidth = 36;
        private const int IdDepth = 37;
        private const int IdHeight = 38;
        private const int IdWorkOffset = 40;

        // Selection box marks. Each box needs its own so the selection manager can tell
        // a body picked into Model from a coordinate system picked into the box below.
        private const int MarkBodies = 1;
        private const int MarkCoordinateSystem = 2;

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
        private IPropertyManagerPageNumberbox _top;
        private IPropertyManagerPageNumberbox _bottom;
        private IPropertyManagerPageNumberbox _side;
        private IPropertyManagerPageNumberbox _offsetX;
        private IPropertyManagerPageNumberbox _offsetY;
        private IPropertyManagerPageNumberbox _width;
        private IPropertyManagerPageNumberbox _depth;
        private IPropertyManagerPageNumberbox _height;
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

            AddLabel(model, IdBodiesHint, "Leave empty to machine every solid body.");

            _coordinateSystem = AddSelectionbox(
                model, IdCoordinateSystem, MarkCoordinateSystem,
                new[] { swSelectType_e.swSelCOORDSYS },
                singleEntityOnly: true,
                tip: "Coordinate system feature defining program zero. Leave empty for the part origin.");

            AddLabel(model, IdCoordinateSystemHint, "Leave empty to use the part origin.");

            var stock = AddGroup(page, GroupStock, "Stock");
            _stockMode = AddCombobox(stock, IdStockMode, "Mode", StockModeNames, "How the stock is sized");

            // Each box is created already showing or already hidden, because the page
            // is rebuilt for every show. Nothing calls Visible on the way in - see the
            // remarks on ShowControlsFor.
            StockMode mode = _working?.Stock.Mode ?? StockMode.RelativeBox;
            bool relative = mode != StockMode.FixedSizeBox;

            _top = AddLengthbox(stock, IdTop, "Top", "Material above the top of the model", relative);
            _side = AddLengthbox(stock, IdSide, "Side", "Material on all four sides",
                mode == StockMode.RelativeBox);
            _offsetX = AddLengthbox(stock, IdOffsetX, "X", "Material left and right",
                mode == StockMode.RelativeBoxXY);
            _offsetY = AddLengthbox(stock, IdOffsetY, "Y", "Material front and back",
                mode == StockMode.RelativeBoxXY);
            _bottom = AddLengthbox(stock, IdBottom, "Bottom", "Material below the bottom of the model", relative);
            _width = AddLengthbox(stock, IdWidth, "Width", "Absolute stock size in X",
                mode == StockMode.FixedSizeBox);
            _depth = AddLengthbox(stock, IdDepth, "Depth", "Absolute stock size in Y",
                mode == StockMode.FixedSizeBox);
            _height = AddLengthbox(stock, IdHeight, "Height", "Absolute stock size in Z",
                mode == StockMode.FixedSizeBox);

            var machine = AddGroup(page, GroupMachine, "Machine");
            _workOffset = AddCombobox(
                machine, IdWorkOffset, "Work offset", WorkOffsets.Names,
                "Which work offset the toolpaths are output against");
        }

        /// <summary>
        /// Called before each show, while the page is closed. Everything that assigns to
        /// a control belongs here rather than in <see cref="PageShown"/>.
        /// </summary>
        protected override void LoadControls()
        {
            _loading = true;

            try
            {
                _name.Text = _working.Name ?? string.Empty;

                _stockMode.CurrentSelection = (short)(int)_working.Stock.Mode;

                _workOffset.CurrentSelection = (short)(_working.WorkOffset - WorkOffsets.First);

                _top.Value = ToBoxLength(_working.Stock.TopOffset);
                _bottom.Value = ToBoxLength(_working.Stock.BottomOffset);
                _side.Value = ToBoxLength(_working.Stock.SideOffset);
                _offsetX.Value = ToBoxLength(_working.Stock.OffsetX);
                _offsetY.Value = ToBoxLength(_working.Stock.OffsetY);
                _width.Value = ToBoxLength(_working.Stock.Width);
                _depth.Value = ToBoxLength(_working.Stock.Depth);
                _height.Value = ToBoxLength(_working.Stock.Height);
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
        protected override void PageShown()
        {
            // Selections only. Visibility was settled when the controls were created.
            RestoreSelections();
        }

        protected override void PageClosed(swPropertyManagerPageCloseReasons_e reason)
        {
            if (reason != swPropertyManagerPageCloseReasons_e.swPropertyManagerPageClose_Okay)
            {
                // Cancel, Escape, or the document closing underneath the page. The clone
                // is simply dropped.
                return;
            }

            CommitToJob();
            Committed?.Invoke(this, _target);
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

            SetVisible(_top, relative);
            SetVisible(_bottom, relative);
            SetVisible(_side, mode == StockMode.RelativeBox);
            SetVisible(_offsetX, mode == StockMode.RelativeBoxXY);
            SetVisible(_offsetY, mode == StockMode.RelativeBoxXY);

            SetVisible(_width, mode == StockMode.FixedSizeBox);
            SetVisible(_depth, mode == StockMode.FixedSizeBox);
            SetVisible(_height, mode == StockMode.FixedSizeBox);
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
