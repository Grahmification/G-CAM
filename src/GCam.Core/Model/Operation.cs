using System;
using System.Collections.Generic;
using GCam.Core.Model.Heights;
using GCam.Core.Strategies;
using GCam.Core.Tooling;

namespace GCam.Core.Model
{
    /// <summary>
    /// One machining operation inside a job: a strategy, a tool, a depth, and the
    /// toolpath that comes out.
    /// </summary>
    /// <remarks>
    /// This type carries what every operation has whatever its strategy - measured
    /// against the four strategies in the HSMWorks export, 47 of 231 parameters. The other
    /// four fifths live in <see cref="Settings"/>. See docs/design/operations.md.
    ///
    /// The strategy is fixed when the operation is created and never swapped. Changing
    /// approach means a new operation, which is what keeps persistence, the property page
    /// and undo from needing a rule for what survives a change between every pair of
    /// strategies.
    /// </remarks>
    public sealed class Operation
    {
        /// <summary>
        /// Default chord tolerance, mm. A sensible starting point for 2D work; the
        /// supplied HSMWorks templates use 0.001mm.
        /// </summary>
        public const double DefaultTolerance = 0.01;

        public Operation(StrategySettings settings)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public string Id { get; set; } = Guid.NewGuid().ToString("D");

        public string Name { get; set; }

        /// <summary>Free text, posted as a comment so it reaches whoever runs the job.</summary>
        public string Comment { get; set; }

        /// <summary>
        /// False suppresses the operation: it keeps its toolpath, is drawn as disabled,
        /// and posts nothing.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>The strategy-specific parameters. Never null, never replaced.</summary>
        public StrategySettings Settings { get; }

        /// <summary>
        /// Which strategy this is. Read from <see cref="Settings"/> rather than stored, so
        /// the two cannot disagree.
        /// </summary>
        public StrategyId Strategy => Settings.Strategy;

        /// <summary>
        /// The part tool this operation cuts with - an id into
        /// <see cref="JobDocument.Tools"/>, not a copy.
        /// </summary>
        /// <remarks>
        /// Several operations routinely share one cutter, and they must not be able to
        /// disagree about its shape. Geometry is shared; the feeds and speeds below are
        /// this operation's own. See docs/decisions/0008-document-tool-list.md.
        /// </remarks>
        public string ToolId { get; set; }

        /// <summary>
        /// This operation's spindle speed, feeds and coolant, seeded from the tool's
        /// defaults when the tool was chosen and free to diverge afterwards.
        /// </summary>
        public CuttingData Cutting { get; set; } = new CuttingData();

        public OperationHeights Heights { get; set; } = new OperationHeights();

        public OperationFrame Frame { get; set; } = new OperationFrame();

        /// <summary>How far the toolpath may deviate from the model, mm.</summary>
        public double Tolerance { get; set; } = DefaultTolerance;

        public OperationState State { get; set; } = OperationState.NotGenerated;

        /// <summary>Why the state is what it is. Null unless there is something to say.</summary>
        public string StateMessage { get; set; }

        /// <summary>
        /// What the strategy produced. Null until it has been generated.
        /// </summary>
        /// <remarks>
        /// Persisted alongside the operation, so a reopened part shows and posts what it
        /// last did without recomputing - see
        /// docs/decisions/0009-persist-toolpaths-in-the-document.md. Whether it still
        /// matches the inputs is <see cref="State"/>'s business, not this property's: a
        /// stale path is kept, drawn faded, and warned about at post time.
        /// </remarks>
        public Toolpath Toolpath { get; set; }

        /// <summary>
        /// Fields G-CAM has no property for, preserved rather than dropped. Same contract
        /// as <see cref="Tooling.Tool.Extra"/> and <see cref="Job.Extra"/>.
        /// </summary>
        public Dictionary<string, string> Extra { get; set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Cuts with a tool from the part's list, taking its feeds and speeds as a
        /// starting point.
        /// </summary>
        /// <remarks>
        /// The tool is referenced, not copied - <see cref="ToolId"/> points into
        /// <see cref="JobDocument.Tools"/>, so several operations share one cutter and
        /// cannot disagree about its shape.
        ///
        /// The cutting data *is* copied, because it is this operation's own from here on:
        /// roughing and finishing with one cutter want different numbers, and always have.
        ///
        /// **Changing tool re-seeds the feeds**, discarding whatever was tuned by hand. It
        /// is the safer default - carrying a 12mm cutter's feeds onto a 3mm one breaks the
        /// 3mm one - but it is a surprise worth warning about before calling this on an
        /// operation someone has already set up.
        /// </remarks>
        public void UseTool(Tool partTool)
        {
            if (partTool == null)
            {
                throw new ArgumentNullException(nameof(partTool));
            }

            ToolId = partTool.Id;
            Cutting = partTool.Cutting?.Clone() ?? new CuttingData();
        }

        /// <summary>True when a toolpath exists and can be believed.</summary>
        public bool HasUsableToolpath =>
            State == OperationState.Generated || State == OperationState.Warning;

        /// <summary>Deep copy, keeping the id. For duplicating a whole job.</summary>
        public Operation Clone()
        {
            return new Operation(Settings.Clone())
            {
                Id = Id,
                Name = Name,
                Comment = Comment,
                Enabled = Enabled,
                ToolId = ToolId,
                Cutting = Cutting?.Clone() ?? new CuttingData(),
                Heights = Heights?.Clone() ?? new OperationHeights(),
                Frame = Frame?.Clone() ?? new OperationFrame(),
                Tolerance = Tolerance,
                State = State,
                StateMessage = StateMessage,
                Toolpath = Toolpath?.Clone(),
                Extra = new Dictionary<string, string>(
                    Extra ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase),
            };
        }

        /// <summary>Deep copy with a fresh id, for a copy that stands on its own.</summary>
        /// <remarks>
        /// The copy starts ungenerated and without a toolpath, rather than claiming a
        /// result computed for something else. Keeping the path while resetting the state
        /// would be worse than either: a stored path nothing admits to having.
        /// </remarks>
        public Operation CloneAsNew()
        {
            Operation copy = Clone();
            copy.Id = Guid.NewGuid().ToString("D");
            copy.State = OperationState.NotGenerated;
            copy.StateMessage = null;
            copy.Toolpath = null;
            return copy;
        }

        /// <summary>
        /// Problems a user can act on. Empty when the operation is ready to generate.
        /// </summary>
        /// <param name="heightContext">
        /// The stock and model extents, when they are available. Null skips the height
        /// checks rather than inventing extents - a page being edited before the geometry
        /// has been resolved is a normal state, not a broken operation.
        /// </param>
        public IReadOnlyList<string> Validate(HeightContext heightContext = null)
        {
            var problems = new List<string>();

            if (string.IsNullOrWhiteSpace(Name))
            {
                problems.Add("An operation needs a name.");
            }

            if (string.IsNullOrWhiteSpace(ToolId))
            {
                problems.Add("Choose a tool for this operation.");
            }

            if (Tolerance <= Precision.Epsilon)
            {
                problems.Add("Tolerance must be greater than zero.");
            }

            if (heightContext != null)
            {
                problems.AddRange(Heights.Validate(heightContext));
            }

            problems.AddRange(Frame.Validate());
            problems.AddRange(Settings.Validate());

            return problems;
        }

        public override string ToString() => Name ?? "Operation";
    }
}
