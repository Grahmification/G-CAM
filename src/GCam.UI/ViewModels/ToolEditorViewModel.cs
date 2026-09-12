using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using GCam.Core.Tooling;

namespace GCam.UI.ViewModels
{
    /// <summary>
    /// Backs the tool editor dialog.
    /// </summary>
    /// <remarks>
    /// Edits a clone, not the library's tool. The dialog's OK copies the clone back;
    /// Cancel simply drops it, so there is no undo to write and no way for a half-made
    /// edit to reach the library.
    ///
    /// Every setter routes through <see cref="Edited"/>, which re-runs validation and
    /// republishes the preview. Properties are hand-written rather than bound straight
    /// to the domain objects because Tool and ToolGeometry are plain data with no change
    /// notification - and adding it to them would push a UI concern into Core.
    /// </remarks>
    public sealed class ToolEditorViewModel : ViewModelBase
    {
        private readonly Tool _tool;
        private readonly ToolLibrary _library;

        public ToolEditorViewModel(Tool tool, ToolLibrary library, bool isNew)
        {
            _tool = (tool ?? throw new ArgumentNullException(nameof(tool))).Clone();
            _library = library;
            IsNew = isNew;

            Holders = new List<Holder>(library?.Holders ?? new List<Holder>());
            Validate();
        }

        public bool IsNew { get; }

        public string Title => IsNew ? "New tool" : "Edit tool — " + _tool.DisplayName;

        /// <summary>Holders offered in the Holder tab, from the library being edited.</summary>
        public IReadOnlyList<Holder> Holders { get; }

        public IEnumerable<ToolType> ToolTypes => Enum.GetValues(typeof(ToolType)).Cast<ToolType>();

        /// <summary>
        /// A fresh copy for the live preview.
        /// </summary>
        /// <remarks>
        /// A copy each time deliberately: ToolProfileView redraws when its Tool property
        /// changes, and handing back the same instance would not register as a change.
        /// </remarks>
        public Tool PreviewTool => _tool.Clone();

        /// <summary>Problems that block OK, in language suitable for the dialog.</summary>
        public IReadOnlyList<string> Problems { get; private set; } = new List<string>();

        /// <summary>
        /// Things worth saying but not worth refusing over - a duplicate tool number is
        /// legal, and a shop may genuinely keep alternatives on one number.
        /// </summary>
        public IReadOnlyList<string> Warnings { get; private set; } = new List<string>();

        public bool IsValid => Problems.Count == 0;

        public bool HasMessages => Problems.Count > 0 || Warnings.Count > 0;

        public string MessageText =>
            string.Join(Environment.NewLine, Problems.Concat(Warnings.Select(w => "Warning: " + w)));

        // ── General ──────────────────────────────────────────────────────────

        public ToolType Type
        {
            get => _tool.Type;
            set
            {
                if (_tool.Type != value)
                {
                    _tool.Type = value;
                    ApplyTypeDefaults(value);
                    Edited();
                }
            }
        }

        public string Name { get => _tool.Name; set { _tool.Name = value; Edited(); } }

        public int Number { get => _tool.Number; set { _tool.Number = value; Edited(); } }

        public string Comment { get => _tool.Comment; set { _tool.Comment = value; Edited(); } }

        public string Manufacturer { get => _tool.Manufacturer; set { _tool.Manufacturer = value; Edited(); } }

        public string ProductId { get => _tool.ProductId; set { _tool.ProductId = value; Edited(); } }

        public string Material { get => _tool.Material; set { _tool.Material = value; Edited(); } }

        // ── Cutter ───────────────────────────────────────────────────────────

        public double Diameter { get => G.Diameter; set { G.Diameter = value; Edited(); } }

        public double CornerRadius { get => G.CornerRadius; set { G.CornerRadius = value; Edited(); } }

        public double TipAngle { get => G.TipAngle; set { G.TipAngle = value; Edited(); } }

        public double SecondTipAngle { get => G.SecondTipAngle; set { G.SecondTipAngle = value; Edited(); } }

        public double TipDiameter { get => G.TipDiameter; set { G.TipDiameter = value; Edited(); } }

        public double FluteLength { get => G.FluteLength; set { G.FluteLength = value; Edited(); } }

        public int FluteCount { get => G.FluteCount; set { G.FluteCount = value; Edited(); } }

        public double ThreadPitch { get => G.ThreadPitch; set { G.ThreadPitch = value; Edited(); } }

        public double ThreadProfileAngle
        {
            get => G.ThreadProfileAngle;
            set { G.ThreadProfileAngle = value; Edited(); }
        }

        // ── Shank ────────────────────────────────────────────────────────────

        public double ShoulderLength { get => G.ShoulderLength; set { G.ShoulderLength = value; Edited(); } }

        public double ShankDiameter { get => G.ShankDiameter; set { G.ShankDiameter = value; Edited(); } }

        public double BodyLength { get => G.BodyLength; set { G.BodyLength = value; Edited(); } }

        public double OverallLength { get => G.OverallLength; set { G.OverallLength = value; Edited(); } }

        // ── Holder ───────────────────────────────────────────────────────────

        /// <summary>
        /// Holder chosen from the library. Geometry is not editable here - holders are
        /// shared between tools, so editing one would silently change others.
        /// </summary>
        public Holder Holder { get => _tool.Holder; set { _tool.Holder = value; Edited(); } }

        public string HolderSummary =>
            _tool.Holder == null
                ? "No holder assigned."
                : $"{_tool.Holder.Segments.Count} sections, {_tool.Holder.Height:0.##} mm tall, " +
                  $"max Ø{_tool.Holder.MaxDiameter:0.##} mm";

        // ── Feeds and speeds ─────────────────────────────────────────────────

        public double SpindleRpm { get => C.SpindleRpm; set { C.SpindleRpm = value; Edited(); } }

        public double RampSpindleRpm { get => C.RampSpindleRpm; set { C.RampSpindleRpm = value; Edited(); } }

        public bool SpindleClockwise { get => C.SpindleClockwise; set { C.SpindleClockwise = value; Edited(); } }

        public FeedMode FeedMode { get => C.FeedMode; set { C.FeedMode = value; Edited(); } }

        public IEnumerable<FeedMode> FeedModes => Enum.GetValues(typeof(FeedMode)).Cast<FeedMode>();

        public CoolantMode Coolant { get => C.Coolant; set { C.Coolant = value; Edited(); } }

        public IEnumerable<CoolantMode> CoolantModes => Enum.GetValues(typeof(CoolantMode)).Cast<CoolantMode>();

        public double CuttingFeed { get => C.CuttingFeed; set { C.CuttingFeed = value; Edited(); } }

        public double PlungeFeed { get => C.PlungeFeed; set { C.PlungeFeed = value; Edited(); } }

        public double EntryFeed { get => C.EntryFeed; set { C.EntryFeed = value; Edited(); } }

        public double ExitFeed { get => C.ExitFeed; set { C.ExitFeed = value; Edited(); } }

        public double RampFeed { get => C.RampFeed; set { C.RampFeed = value; Edited(); } }

        public double RetractFeed { get => C.RetractFeed; set { C.RetractFeed = value; Edited(); } }

        public double Stepover { get => C.Stepover; set { C.Stepover = value; Edited(); } }

        public double Stepdown { get => C.Stepdown; set { C.Stepdown = value; Edited(); } }

        /// <summary>Derived, shown read-only - the number a machinist sanity-checks against.</summary>
        public string FeedPerTooth
        {
            get
            {
                double value = C.FeedPerTooth(G.FluteCount);
                return value > 0 ? value.ToString("0.####") + " mm" : "—";
            }
        }

        // ── Machine ──────────────────────────────────────────────────────────

        public int DiameterOffset { get => M.DiameterOffset; set { M.DiameterOffset = value; Edited(); } }

        public int LengthOffset { get => M.LengthOffset; set { M.LengthOffset = value; Edited(); } }

        public bool BreakControl { get => M.BreakControl; set { M.BreakControl = value; Edited(); } }

        public bool ManualToolChange { get => M.ManualToolChange; set { M.ManualToolChange = value; Edited(); } }

        /// <summary>The edited tool, for the caller to copy back into the library.</summary>
        public Tool Result => _tool;

        private ToolGeometry G => _tool.Geometry ?? (_tool.Geometry = new ToolGeometry());

        private CuttingData C => _tool.Cutting ?? (_tool.Cutting = new CuttingData());

        private MachineData M => _tool.Machine ?? (_tool.Machine = new MachineData());

        /// <summary>
        /// Keeps geometry consistent when the type changes, so switching to a ball nose
        /// does not leave an impossible corner radius behind.
        /// </summary>
        private void ApplyTypeDefaults(ToolType type)
        {
            switch (type)
            {
                case ToolType.FlatEndMill:
                    G.CornerRadius = 0;
                    break;

                case ToolType.BallEndMill:
                    G.CornerRadius = G.Diameter / 2.0;
                    break;

                case ToolType.BullNoseEndMill:
                    if (G.CornerRadius <= 0 || G.CornerRadius >= G.Diameter / 2.0)
                    {
                        G.CornerRadius = Math.Round(G.Diameter / 8.0, 3);
                    }

                    break;

                case ToolType.Drill:
                    if (G.TipAngle <= 0)
                    {
                        G.TipAngle = 118;
                    }

                    break;

                case ToolType.SpotDrill:
                case ToolType.ChamferMill:
                    if (G.TipAngle <= 0)
                    {
                        G.TipAngle = 90;
                    }

                    break;

                case ToolType.Tap:
                    if (G.ThreadPitch <= 0)
                    {
                        G.ThreadPitch = 0.5;
                    }

                    break;
            }
        }

        private void Edited([CallerMemberName] string propertyName = null)
        {
            Validate();

            Raise(propertyName);
            Raise(nameof(PreviewTool));
            Raise(nameof(Title));
            Raise(nameof(HolderSummary));
            Raise(nameof(FeedPerTooth));

            // Type defaults can change several fields at once, so refresh the lot.
            Raise(nameof(CornerRadius));
            Raise(nameof(TipAngle));
            Raise(nameof(ThreadPitch));
        }

        private void Validate()
        {
            Problems = _tool.Validate().ToList();

            var warnings = new List<string>();
            if (_library != null && _library.Tools.Any(t => t.Number == _tool.Number && t.Id != _tool.Id))
            {
                warnings.Add($"tool number {_tool.Number} is already used in this library.");
            }

            Warnings = warnings;

            Raise(nameof(Problems));
            Raise(nameof(Warnings));
            Raise(nameof(IsValid));
            Raise(nameof(HasMessages));
            Raise(nameof(MessageText));
        }
    }
}
