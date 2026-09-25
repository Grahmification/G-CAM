# Operations

What every operation has regardless of strategy, how a strategy adds the rest, and how
one gets generated, stored, drawn and edited. Read this before touching `Core/Model`,
`Core/Strategies`, `Core/Generation`, the Operation property page or toolpath rendering.

Built as far as slice 15 of the build order at the end of this document, everything up to
slice 11 and slice 15 verified by hand on 2025 SP3: a 2D contour generates from edges
selected on a real part — open profiles as well as closed — draws, and persists; there is a
five-tab property page to set it up on, a tool can be picked out of a library into the
part, and each contour carries its own direction and how far it propagates from the edge
that was picked. The job tree deletes, duplicates, renames,
suppresses and generates one — see [In the job tree](#in-the-job-tree). Nothing has been
posted, and the page has real gaps — they are listed under
[the property page](#the-property-page) rather than left to be discovered.

The four decisions underneath this document are recorded separately:
[0006](../decisions/0006-operation-parameters-are-values.md) (values, not expressions),
[0007](../decisions/0007-typed-strategy-settings.md) (typed settings, not a parameter bag),
[0008](../decisions/0008-document-tool-list.md) (one tool list per part),
[0009](../decisions/0009-persist-toolpaths-in-the-document.md) (toolpaths saved in the part).

## What the split has to be

Measured from the HSMWorks export in
[`docs/example_files/example_operations.hsmworks-template`](../example_files/example_operations.hsmworks-template) —
11 templates across `face`, `adaptive2d`, `contour2d` and `drill`:

| | Count |
| --- | --- |
| Distinct parameters across four strategies | 231 |
| Common to all four | 47 |
| `contour2d` alone | 152 |

So roughly **a fifth of an operation is shared and four fifths belongs to its strategy**.
That ratio is the whole reason for the shape below: a base class carrying 47-ish
parameters and a strategy object carrying its own, rather than one type with 231 fields
of which most are meaningless at any moment.

Two things the export teaches that are easy to get wrong:

**Lead-in/out is not universal.** `doLeadIn`, `entry_radius`, `exit_sweep` appear in two
of the four strategies; `drill` and `face` have none. Neither do `doMultipleDepths` and
`maximumStepdown`, which `drill` lacks. Linking and multiple depths are *common groups
that some strategies opt into*, not base parameters. Putting them in the base would give
every drill operation a lead-in radius it can never use.

**Geometry is absent from templates entirely.** A template carries a strategy, a tool and
parameters, and no selections — which is what makes it portable between parts. Only
`holeMode='selection-faces'` survives, and that is a mode, not a selection. Our template
format has to preserve that property; see [Templates](#templates).

## The model

```
Core/Model/
  Operation.cs          the base — identity, state, tool, cutting, heights, frame, settings
  OperationState.cs     NotGenerated | Generating | Generated | Stale | Warning | Failed
  OperationFrame.cs     inherit the job's coordinate system, or override it
  GeometryRef.cs        a persistent reference to one model entity, resolved at the edge
  Heights/
    OperationHeights.cs the five heights as a set, with the ordering rule
    HeightSetting.cs    mode + offset + optional reference
    HeightMode.cs       FromStockTop, FromModelTop, FromJobOrigin, FromSelection, …
    HeightContext.cs    the resolved Z values a mode is measured from
  Toolpath.cs           the generated result: an ordered list of moves
  Move.cs / MoveKind.cs Rapid | Lead | Link | Cutting | Plunge | Retract | Cycle

Core/Strategies/
  IToolpathStrategy.cs  Generate(context, progress, cancellation) → Toolpath
  StrategySettings.cs   abstract base for the strategy-specific half
  StrategyId.cs         the stable string that names a strategy in files
  StrategyCatalog.cs    id → settings type, strategy implementation, display name
  Contour2d/ Face/ Adaptive2d/ Drill/

Core/Generation/
  GenerationQueue.cs    runs operations off the STA thread, one at a time, cancellable
  Staleness.cs          what a change invalidates, including downstream operations

Core/Rendering/
  ToolpathMesh.cs       Toolpath → RenderBatches, coloured by move kind
```

`Operation` itself:

```csharp
public sealed class Operation
{
    public string Id { get; set; }              // stable; survives rename and reorder
    public string Name { get; set; }            // "2D Contour1" by default
    public string Comment { get; set; }
    public bool Enabled { get; set; } = true;   // suppressed operations post nothing

    public StrategyId Strategy { get; }         // fixed at construction
    public StrategySettings Settings { get; }   // the strategy's own parameters

    public string ToolId { get; set; }          // into JobDocument.Tools
    public CuttingData Cutting { get; set; }    // this operation's feeds and speeds
    public OperationHeights Heights { get; set; }
    public OperationFrame Frame { get; set; }
    public double Tolerance { get; set; }       // chord tolerance for the whole operation

    public OperationState State { get; set; }
    public string StateMessage { get; set; }    // why it is Warning or Failed
    public Toolpath Toolpath { get; set; }      // null until generated
    public Dictionary<string, string> Extra { get; set; }
}
```

`Clone`/`CloneAsNew`/`Validate` follow the conventions `Job` and `Stock` already set,
including `Extra` as the forward-compatibility bag. `Settings` deep-copies through an
abstract `StrategySettings.Clone()`.

**Strategy is fixed at construction.** New Operation asks which strategy first, as
HSMWorks does, and the settings object is created with the operation and never swapped.
That keeps persistence, the property page and undo from needing a "what survives a
strategy change" mapping between every pair of strategies. Changing approach means a new
operation; duplicating the old one carries the tool, heights and frame across.

## Tool and cutting data

**The part owns a tool list; operations reference it.** `JobDocument.Tools` holds the
tools copied into this part — one list per part, shared by every job in it, because that
is what the machine looks like: a tool is in the carousel regardless of which setup is
running. An unused tool stays in the list, marked unused, until someone removes it.

Taking a tool from a library copies it into `JobDocument.Tools` once, keeping `Tool.Id`
and stamping `SourceLibraryId` exactly as [0003](../decisions/0003-jobs-embed-their-tools.md)
already specifies. A second operation wanting the same tool **selects the existing part
tool and shares it** — it does not copy again.

**Browse on the Operation page is the way a tool gets into a part.** It opens the tool
library browser as a picker ([UI shells](ui-shells.md)), checks the chosen tool out of its
library, and hands it to `JobDocument.AddTool`, which is idempotent by `Tool.Id` — so
picking a tool the part already holds selects the copy that is here. The page then
re-reads the part's list rather than appending to its own, because the tool it gets back
is the part's copy and not always the one picked.

**Picking a tool writes to the part immediately, and Cancel on the page does not take it
back.** Two reasons, and they are the ones already recorded elsewhere in this project: the
tool list is the carousel, so a tool belongs to the part whether or not an operation uses
it — an unused tool stays until `RemoveTool` takes it out — and the same logic makes
creating a tool library write at once rather than waiting for a commit (see
[architecture.md](../architecture.md)). Holding the tool on the page's clone instead would
throw it away whenever somebody chose a cutter, thought better of the operation and
cancelled. Adding a tool marks the document dirty for the same reason every other CAM edit
does: SOLIDWORKS only offers to write during a save it has already decided to do.

The split that makes sharing safe:

| Lives on | What | Why |
| --- | --- | --- |
| The part tool (`JobDocument.Tools`) | Identity, number, cutter geometry, holder | It is one physical cutter. Two operations must not disagree about its shape |
| The operation (`Operation.Cutting`) | Spindle speed, all feeds, coolant | Roughing and finishing with one cutter need different numbers, and always have |

`Operation.Cutting` is seeded from the tool's `CuttingData` when the tool is chosen and is
free to diverge afterwards. Editing the *tool's* geometry in the tool list changes every
operation using it and marks them all stale — correct, because the cutter really did
change. Editing feeds in an operation touches only that operation.

The tool library browser grows a second list beside the libraries: the tools in the open
part, each with the operations using it beneath. That view is a projection over
`JobDocument`, computed in Core (`ToolUsage`) rather than assembled in the viewmodel —
the same rule that put `ToolSearch` in Core. It lives in `Core/Model` rather than
`Core/Tooling` because it reads `JobDocument`, and Model already depends on Tooling;
the other way round would make the two namespaces depend on each other.

Three rules the model enforces rather than leaving to the picker:

- **`AddTool` is idempotent by `Tool.Id`**, so a second operation wanting the same library
  tool gets the copy that is already in the part. This is what makes a shared list mean
  anything, and it holds however the tool arrives.
- **A tool keeps the number it had in the library.** Renumbering silently would be wrong —
  the number is the machine's — so a clash is reported by `JobDocument.ValidateTools()`
  instead. Two tools in pocket 4 is a part that cannot be set up as written.
- **Removing a tool that is in use is refused**, naming the operations. `ToolUsage` also
  reports the opposite case, an operation pointing at a tool that has gone, which is
  distinct from an operation with no tool chosen yet.

`Operation.UseTool(tool)` sets the reference and copies the tool's cutting data in as a
starting point. **Changing tool re-seeds the feeds**, discarding hand-tuned numbers: the
safer default, since carrying a 12.7mm cutter's feeds onto a 3mm drill breaks the drill —
but a surprise the UI should warn about before calling it on an operation already set up.

### Entering either end of a derived pair

Surface speed and chip load are arithmetic over spindle speed, feed and the tool, and a
machinist thinks in whichever one their handbook uses. Both ends are editable and the
partner updates:

| Canonical, stored | Derived, editable | Relationship |
| --- | --- | --- |
| `SpindleRpm` | Surface speed | `v = π · d · rpm` |
| `CuttingFeed` | Feed per tooth | `fz = feed / (rpm · teeth)` |
| `CuttingFeed` | Feed per revolution | `fn = feed / rpm` |

**Only the left column is stored.** The derived values are recomputed for display every
time the page opens, which is what stops a stored pair drifting apart after someone edits
the tool diameter. HSMWorks does the same thing by storing them as expressions; storing
the evaluated numbers instead would leave `tool_surfaceSpeed` describing a diameter the
tool no longer has.

The conversions are pure functions in `Core/Tooling/FeedsAndSpeeds`, taking the tool
geometry they need explicitly, so they are testable without a tool, an operation or a
page. `CuttingData.FeedPerTooth(fluteCount)` already works this way and becomes one of
them. Each pair of boxes on the page writes through the same function in both directions;
a zero or missing input yields zero rather than throwing, because this is display
arithmetic and not a validation gate.

## Heights

Five heights, each a **mode plus an offset**, and a reference only where the mode needs
one — HSMWorks' shape, with the mode list trimmed to what 3-axis work uses:

```
clearance   the safe rapid plane above everything
retract     where the tool retracts to between passes
feed        where rapid becomes feed on the way down
top         where cutting starts
bottom      where cutting stops
```

```csharp
public sealed class HeightSetting
{
    public HeightMode Mode { get; set; }
    public double Offset { get; set; }        // mm, signed
    public GeometryRef Reference { get; set; } // only for FromSelection
}
```

Modes: `FromStockTop`, `FromStockBottom`, `FromModelTop`, `FromModelBottom`,
`FromJobOrigin`, `FromSelection`, and three that each only one or two heights may use:

| Mode | Measured from | Allowed on |
| --- | --- | --- |
| `FromContour` | The Z of the chain being cut | Top, Bottom |
| `FromTop` | The operation's resolved top height | Feed |
| `FromRetract` | The operation's resolved retract height | Clearance |

The rest of HSM's list is deliberately absent until something needs it; each costs a
resolver and a test. The page offers a mode only on the rows allowed to use it, and
`Validate()` refuses one anywhere else, for a file that says otherwise.

Resolution is a pure function of a `HeightContext` — the stock and model Z extents in the
operation's frame, supplied by `GCam.SolidWorks/Extraction` — so every mode is testable
headlessly. This is what makes the heights *follow the stock*: change the stock and every
height derived from it moves, which is the property that makes the HSM model worth
copying rather than storing five plain numbers.

**A height measured from another is one step deep.** Top and Retract may not themselves
be measured from another height, so `OperationHeights` resolves them first, into the
context, and then everything else — no ordering problem, no cycles. The consequence is
that a single height must be resolved through `OperationHeights.TryResolve(kind, …)`:
`HeightSetting.TryResolve` alone cannot answer for `FromTop` or `FromRetract`, and fails
rather than guessing.

**Defaults** are clearance 5 above the retract, retract 5 above the stock top, feed 2
above the top, top at the stock top, bottom at the model bottom. Chaining clearance and
feed to other heights keeps them from crossing when one is moved. A strategy may start from
its own — `StrategySettings.DefaultHeights()` — and 2D contour overrides the bottom to
`FromContour`. A file with a height missing or in a mode this build does not know gets the
strategy's default, not a failed load.

`OperationHeights.Validate()` enforces `clearance ≥ retract ≥ feed ≥ top > bottom` after
resolution and reports which pair is inverted. Validation runs against resolved values,
not modes, because two different modes can resolve to the same plane.

**Except retract below feed, which is corrected rather than refused.** The retract is
lifted to the feed height and the operation gets a warning (`ResolvedHeights.RetractLiftedFrom`
records it). The fix is obvious and only ever sends the tool higher, so refusing to
generate over it cost more than it saved. A clearance measured from the retract follows the
lifted value; a fixed clearance that ends up below it is still refused.

### Heights measured from the contour

`FromContour` is the one mode where heights are **not a property of the operation alone**:
one operation cuts each chain at its own depth. Extraction therefore flattens every 2D chain
to a single Z — see [How far a pick runs](#how-far-a-pick-runs) — and `ResolvedContour.Level`
is that Z.

`Core/Strategies/ContourHeights` resolves the heights once per contour and attaches them to
the `ResolvedContour`; `Contour2dStrategy` takes its depths from each contour's own.
`GenerationContext.Heights` stays the operation-level answer — the first contour's —
which is right for clearance and retract, the only ones read from it.

- **Clearance and retract are one plane for every contour**, because the tool crosses them
  between contours. Feed may vary per contour, but only through a top that does.
- **A retract lifted to the feed height is lifted to the highest feed of any contour cut**,
  so every contour still retracts to the same plane — more air-cutting on the lower ones,
  in exchange for a tool that always goes back to one place. A contour left out does not
  count.
- **One bad chain does not condemn the operation.** Heights in order for one contour can be
  out of order for another, so a contour that fails is reported through
  `GenerationContext.Warnings` and left uncut, the way a contour consumed by a negative
  tangential extension is. Only when every contour fails does generation fail.
- **The Heights tab draws no plane for a height in this mode**, which is what HSMWorks
  does: there is no one Z to draw.

On a `HeightContext` with no contour in it, a `FromContour` height does not resolve, so
`Operation.Validate(context)` skips contour-relative heights unless given a context for a
contour; generation checks them per contour.

## Geometry

**Selections live in the strategy settings, typed per strategy**, not on the base. A
contour operation selects edges or faces forming contours; a drill operation selects
cylindrical faces; facing takes an optional boundary. There is no meaningful
lowest-common-denominator selection, and a generic list on the base would let a drill
operation store a flat face and defer the complaint to generation time.

Core cannot name SOLIDWORKS entities, so a selection is a `GeometryRef`:

```csharp
public sealed class GeometryRef
{
    public string PersistentId { get; set; }  // GetPersistReference3, base64
    public GeometryRefKind Kind { get; set; } // Face | Edge | Vertex | Body | Sketch
    public string DisplayName { get; set; }   // for the UI and for error messages
}
```

`Core/Abstractions/IGeometryResolver` turns those into geometry at generation time and is
implemented in `GCam.SolidWorks/Extraction`. A reference that no longer resolves is a
`Warning` on the operation naming the missing entity — never a silent empty selection,
which would generate an empty toolpath that looks like success.

Persistent references, not names — which is now true of what a *job* points at as well:
`Job.ModelBodies`, `Job.CoordinateSystem` and `OperationFrame.CoordinateSystem` are all
`GeometryRef`s, with names kept as the display label and the migration fallback. See
[Jobs](jobs.md).

### Selection modifiers are stored as intent, not baked in

Picking geometry in SOLIDWORKS comes with modifiers — tangent propagation, propagate along
Z — that decide how far a selection runs from the entity actually clicked. There are two
ways to store the result, and they are not equivalent:

| | Stored |
| --- | --- |
| **Bake at pick time** | The forty edges SOLIDWORKS' selection expanded to |
| **Store as intent** | The one entity picked, plus the modifiers, re-chained every generate |

**G-CAM stores intent**, in `ContourSelection` — the entity, `PropagateTangent`,
`PropagateAlongZ` and `Reversed`. Three reasons:

- **HSMWorks does.** `chainingTolerance` is a parameter on contouring, facing and adaptive
  clearing in their own templates, and a tolerance for chaining only exists if the chaining
  happens in the CAM engine rather than in the CAD selection.
- One stored reference survives a model edit that would break forty.
- The property page can show what was chosen — "this edge, tangentially" — rather than a
  list of forty edges nobody picked individually.

The cost is real: **a model edit can silently change how far a chain runs**, by making two
edges tangent that were not. Staleness covers it — a SOLIDWORKS rebuild marks every
operation stale, so the path is regenerated and seen before it can post without a warning.

`Reversed` is **the direction of travel** — HSMWorks' per-contour arrow, and the Reverse
button on the Geometry tab. Which side of the line the cutter runs on is not stored at
all: it follows from the direction and the climb/conventional choice together, which is
what those words mean on a machine — see
[2D contouring](#2d-contouring-the-first-strategy).

All three flags are honoured. The two propagation flags are the checkboxes beside Reverse,
and what they do is [the walk](#how-far-a-pick-runs) below.

The flag is per *pick*, but a **chain has exactly one direction**, because it is cut as one
continuous move. Chaining pools every selected edge, so one chain is often built from
several picks — four edges of a rectangle are four picks and one loop. A chain is therefore
reversed if **any** of the picks that built it was, which is the only coherent answer.
`Chaining.ChainWithSources` is what makes that answerable at all: by the time the pieces
are a `Polyline` they have been reversed, reordered and merged past recognition, so the
mapping back to picks has to come out of the chaining itself.

The flag has to reach the **strategy** rather than being applied during extraction. For an
open path it could be applied early, by handing over an already-reversed chain — but for a
closed one it could not, because `Contour2dStrategy` forces the orientation from the
climb/conventional setting as the first thing it does, and that would quietly undo the
reversal. Hence `ResolvedContour`, which pairs the curve with the intent and lets the
strategy apply it last.

### How far a pick runs

`Core/Geometry/EdgePropagation` walks the model's edges from the one that was picked;
`GCam.SolidWorks/Extraction/ModelEdgeTopology` answers its questions from `IEdge` and
`IVertex`, so the walk itself is graph arithmetic with headless tests, the same split
`Chaining` makes. HSMWorks' two modifiers, as measured against it:

| | |
| --- | --- |
| **Tangential propagation** | Follows edges running smoothly on from this one, **forwards only** — the way the arrow points, so Reverse turns the walk round with it. It may climb or descend a 3D edge |
| **Propagate along Z** | Follows any joining edge lying flat at the picked edge's own height, **both ways** — and opening the backward end is what lets tangency run backwards too |

One walk, not two: at every junction an edge is crossed when *either* rule accepts what is
on the far side, which is why the two are commonly on together.

**Tangency wins where both rules match, and a branch is an ambiguity inside the rule that
won.** Two tangent continuations have no answer the user could have meant, and neither do
two level edges with nothing tangent — so the walk stops. But one tangent continuation
alongside an edge that merely happens to lie at the same height is not a branch: that is
the junction a fillet makes where its end edge crosses the face, and treating it as one
stopped the walk dead at the corners propagation exists to get round.

**Every chain comes out of 2D extraction at a single Z**, because it is cut at one depth
and a height [measured from the contour](#heights-measured-from-the-contour) needs that
depth to be one number. The blue highlight on the Geometry tab is drawn from the same
chains, so it shows that Z. Two cases:

- **A propagated pick is flattened before chaining**, onto the Z where the picked edge
  starts, because the walk may have climbed a riser that has to collapse — a piece that
  flattens to nothing is dropped.
- **Everything else is chained in 3D first**, and a chain that comes out not flat is then
  projected onto the level of the first pick in it; for a face, where its first edge
  starts. Flattening each such pick first instead would pull apart a profile built from
  separate picks at different heights, which chains as one in 3D.

A chain that is purely vertical flattens to nothing and is dropped with a log line. The
projection is `Core/Geometry/Flattening`. Flattening is a parameter of extraction rather
than a rule inside the walk, so a 3D strategy can take the same chains where they actually
lie.

**Tangential extension is a different thing, and is a strategy parameter.** It acts on an
already-fixed selection, so `TangentialExtensionDistance` lives in `Contour2dSettings` and
is applied by `Core/Geometry/TangentialExtension` — one distance for both ends of every
open contour, **before** the cutter offset, so the extension is offset with the rest of the
profile and is cut at every depth. Negative shortens; a contour it consumes entirely is
reported and skipped rather than cut as nothing. HSMWorks' second distance
(`tangentialExtensionDistanceEnd`, for asymmetry) and its `tangentialFragmentExtensionDistance`,
which stretches the computed motion instead of the profile, are both deliberately absent.

## Strategy settings

```csharp
public abstract class StrategySettings
{
    public abstract StrategyId Strategy { get; }
    public abstract StrategySettings Clone();
    public virtual IReadOnlyList<string> Validate();

    /// <summary>True when this strategy machines what earlier operations left.</summary>
    public virtual bool DependsOnPrecedingStock => false;
}
```

`Validate()` checks **self-consistency only** — a stepdown of zero while multiple depths
are on, a negative lead radius, an empty selection. An earlier draft of this document had
it take the owning `Operation`, which turned out to be worth nothing: the rules that want
more context want the *tool* (is this stepdown deeper than the flute length?) and the
*resolved heights* (is it deeper than the cut?), and neither is reachable from an
`Operation` — the tool is an id into the part's list, and heights need a `HeightContext`.
So the parameter bought a Model↔Strategies cycle and no information. Those cross-object
rules are checked when an operation is generated, where both are in hand.

Typed classes — `Contour2dSettings`, `DrillSettings` — with real properties, real enums
and real defaults, because strategy code, posting and tests all read them directly and a
magic-string bag gives none of that back. The cost, a hand-built page per strategy, is
paid once per strategy and is bounded; see
[0007](../decisions/0007-typed-strategy-settings.md) for the alternatives.

Shared optional groups — linking, multiple depths, lead-in/out — are **composed**, not
inherited: small classes (`LinkingSettings`, `MultipleDepthsSettings`, `LeadSettings`)
held as properties by the strategies that have them. That is how the export's own
structure reads, and it keeps a drill operation from carrying a lead-in radius.

`StrategyCatalog` maps a `StrategyId` to its settings type, its `IToolpathStrategy` and
its display name. It is the one place a new strategy registers itself, and it is what the
New Operation dialog, the persistence reader and the template importer all consult.

## Generation

```csharp
public interface IToolpathStrategy
{
    StrategyId Id { get; }
    Toolpath Generate(GenerationContext context,
                      IProgress<GenerationProgress> progress,
                      CancellationToken cancellation);
}
```

`GenerationContext` carries the resolved inputs — the operation, its part tool, the
resolved contours, the resolved heights and the stock — so a strategy touches no COM and no
SOLIDWORKS, and runs on a worker thread. Strategies are deterministic: same context in,
same toolpath and same warnings out. `IGenerationContextFactory` builds one, declared in Core and implemented in
`GCam.SolidWorks`, because resolving heights and geometry is the one part of generation
that needs the model.

`IToolpathStrategy` and `GenerationContext` live in `Core/Strategies` rather than
`Core/Generation`, so the dependency runs one way — Generation → Strategies → Model. Both
folders one way round is worth more than either name being perfect.

**Nothing in Core creates a thread.** The caller runs the queue on a worker; Core spawning
its own would hide the rule rather than honour it. For the same reason the queue forwards a
strategy's progress **synchronously** rather than through `System.Progress<T>`: that type
posts to whatever synchronisation context captured it, which here is the worker thread, so
reports would arrive late, out of order, or after the run they describe. Marshalling to the
UI thread is the caller's decision and belongs in the caller's own `IProgress<T>`.

**Generation is explicit and runs off the STA thread.** The user generates one operation,
a job, or everything; `GenerationQueue` runs them in tree order, reporting percentage
through `IProgress<T>` and honouring cancellation between moves. Editing never triggers
generation on its own — it marks the operation stale and leaves the previous toolpath on
screen, dimmed.

**Failures are state, not dialogs.** A strategy that cannot proceed throws
`GCamUserException`; the queue catches it, sets `State = Failed` with the message, and
carries on with the next operation. The previous toolpath is kept — a failed retry must
never lose a path that was already proven. A modal dialog per failure would stop a
whole-job generate dead, which is exactly when failures cluster.

Four outcomes that are not "generated" and not failures either:

- **A disabled operation is skipped**, keeping whatever toolpath and state it had.
- **An empty result is a `Warning`**, not a silent success. An operation can legitimately
  have nothing to cut, and saying nothing would look like success with an invisible result.
- **So is a path with something worth reading about it**, through
  `GenerationContext.Warnings` — the only thing a strategy says other than the path
  itself. A strategy that cannot proceed throws; without this, everything short of that
  would be lost, including a contour dropped for being shorter than its own negative
  tangential extension.
- **A strategy with no implementation yet fails with a readable message.** Settings, a
  property page and persistence all exist before an algorithm does, so this is a real state
  rather than a placeholder — and it beats a null reference from inside the queue.
- **Cancelling mid-regeneration leaves the operation `Stale`, not `NotGenerated`**, when it
  already had a path. There is still a toolpath; it is out of date, which is exactly what
  it was before the cancelled run started.

An exception that is *not* a `GCamUserException` is a bug in a strategy. The operation
fails with "see the log", and the stack trace goes to the log rather than to the user.

### 2D contouring, the first strategy

`Contour2dStrategy` turns a profile into passes, open or closed. One pass is: rapid across
at clearance, rapid down to the feed height, plunge to depth, lead in, cut the profile,
lead out, retract. Depths repeat that, and the tool retracts between them because a contour
is not guaranteed to be able to stay down — the profile may run outside the stock.

**Several fragments are several passes.** A selection that chains into three separate runs
is cut as three, each with its own lead-in, lead-out and retract, in the order the chains
came out. The tool does not stay down to link between them: HSMWorks' `stayDownDistance`
family decides when a link is short enough to keep the cutter in the material, and that is
a gouge-checking question rather than a linking one — a link that crosses stock cuts it.

**A lead must swing away from the wall, and which side that is has to be measured.** The
arc centre used to be hard-coded one radius to the *left* of travel, so on an outside
profile — the common case — every lead swung into the part and bit the finished wall on the
way in. It is now read off the geometry: the wall is wherever the profile lies relative to
the cutter path that was offset from it, and the lead turns the other way, with the arc
swept counter-clockwise for a centre on the left and clockwise for one on the right.

Deriving that side from the settings instead would mean restating three rules —
climb/conventional, reversed or not, and the explicit side an open path carries — and
keeping them in step with the offsetting code forever. Measuring is shorter and survives
the next rule anyone adds.

**A lead-in means the tool goes down off the profile.** The plunge lands at the start of
the lead arc — one radius back and one to the side, so r&#8730;2 from the wall — and the arc
brings it onto the profile tangentially. Plunging onto the profile and then arcing is the
bug that shipped first: a `Move` stores only its destination, so an arc from the profile
start *to* the profile start is zero-length, which the tessellator correctly reads as a
full circle. The cutter plunged onto the finished wall and looped right round it.

Three things in it are worth knowing before changing it:

- **Travel is `Reversed`'s alone; the side is climb/conventional's.** HSMWorks' split, and
  the one that keeps each control doing one thing: Reverse turns the arrow round, and
  climb/conventional moves the cutter across the line without touching the arrow. Both
  still change the side, because with travel fixed the side is exactly what climb means.
  Both rules live in `Contour2dOffsetting`, so the cut-direction arrows cannot disagree
  with the toolpath. *(This is the opposite way round from what the code did until
  2026-09-20, when `Reversed` chose the side and `Direction` reversed travel. Both are
  self-consistent; only this one matches HSMWorks, and only this one lets a user change
  the side without also changing which way the profile is cut.)*
- **A closed contour takes its side from the sign of the offset**, not from its
  orientation. Measured: Clipper grows the enclosed region for a positive distance
  *whichever way the path runs*, so orientation is free to carry travel instead. Outward
  for `climb != Reversed` — climb on the outside of a boss is a counter-clockwise run and
  climb on the inside of a pocket is a clockwise one — and inward otherwise, which is how
  a pocket is cut.
- **An open path has no inside, so its side is named outright** — right of travel for a
  climb cut, since a cutter turning clockwise seen from above then has its edge moving
  *with* the feed where it touches. Travel does not turn round with `Direction`, so the
  hand is what changes. A negative distance is not passed through: an open path has no
  area to shrink, so the side is flipped and the magnitude used.
- **A closed pass starts midway along its longest run, not at a corner.** A lead-in
  reaches back about r√2, which at a corner aims at the neighbouring wall. Cutting outside
  hid it; the first pocket put the touch-down 1mm off the wall it was about to finish.
- **The last pass lands exactly on the bottom**, not a float's width above it. The
  alternative leaves a witness ridge that no operator can explain.
- **A feed left at zero falls back to the cutting feed.** Zero means "not set", and
  emitting `G1 F0` stops the machine dead in the cut.

Offsetting is **Clipper2**, behind `IContourOffsetter` — the one bought-in algorithm in the
geometry kernel, and the one worth buying: an offset that removes its own
self-intersections is a solved problem with many edge cases, and getting it subtly wrong
produces a path that looks right and gouges the part. It is a NuGet reference in Core, so
it reaches the add-in through `AssemblyResolver` like everything else; see
`docs/solidworks-api/addin-dependencies.md`.

**Offsetting one side of an open path is not something Clipper2 does**, and neither does
any other general offsetting library: inflating an open path gives the closed *ribbon*
around it — the left offset, an end cap, the right offset, another cap. That is the right
answer to the question those libraries are asked and the wrong one for a cutter. So
`Clipper2Offsetter.OffsetOpen` cuts the ribbon open again: the two ends of the wanted side
are known exactly, being the path's own endpoints pushed along the side normal, so the
ribbon is walked between the vertices nearest them and the way round that lies on the
wanted side is kept. What that buys is the part worth buying — Clipper has already removed
the self-intersections a naive parallel curve produces wherever the offset exceeds the
local curvature, and those are what gouge a part. A test asserts the property directly: no
point of the result is closer to the path than the offset distance.

What the strategy deliberately does **not** do yet, left as gaps rather than as wrong
numbers: ramped entry, arbitrary lead sweeps and perpendicular approach (a quarter-turn arc
is what comes out), multiple finishing passes, tabs, chamfering, rest machining, and
staying down between fragments.

### What makes an operation stale

Conservatively, everything that feeds generation: the operation's own parameters, its
geometry selections, the part tool it uses, the job's stock, coordinate system or body
selection, and a SOLIDWORKS rebuild of the part. It will sometimes mark stale when the
result would not have changed. The opposite error posts a path that no longer matches the
model, so the asymmetry is deliberate.

**Staleness cascades downstream.** Operations run in tree order, and an operation whose
settings report `DependsOnPrecedingStock` depends on every enabled operation above it.
Editing, reordering, disabling or deleting an operation marks it and every dependent
operation below it stale. Reordering is therefore a real edit, not a view change.

**The rebuild is watched by `GCam.SolidWorks/Events/PartRebuildWatcher`**, one per open
part, on `PartDoc.RegenPostNotify2` — which covers a rollback as well as a rebuild. It
marks stale inside the notification and defers the redraw to the message pump; the
reasoning, and what is still unverified about it, is in
[rebuild-notifications.md](../solidworks-api/rebuild-notifications.md).

`Staleness.ModelRebuilt` returns how many operations it changed, and the watcher does
nothing for zero. Nearly every rebuild marks nothing — no operation generated yet, or
everything already stale — and the alternative is rebuilding the tree and the whole 3D
scene on every <kbd>Ctrl</kbd>+<kbd>B</kbd>.

This was modelled from the start and wired up only on 2026-09-16, which is worth
remembering: `Staleness.ModelRebuilt` existed, was tested, and had **no callers at all**,
so changing a dimension left every toolpath on screen at full strength with every
operation still calling itself Generated. A rule with no caller reads exactly like a rule
that works.

## The toolpath

```csharp
public sealed class Move                   // immutable
{
    public MoveKind Kind { get; }          // Rapid | Lead | Link | Cutting | Plunge | Retract | Cycle
    public Vec3 End { get; }               // mm, in the operation's frame
    public double Feed { get; }            // mm/min; 0 for rapids
    public ArcData Arc { get; }            // null for linear moves
}
```

**A move stores its destination, not its start** — the tool is wherever the previous move
left it, the way G-code and every CAM canonical form work. It also means a move cannot
disagree with the one before it about where the tool is. The consequence to remember:
**the first move only says where the tool starts**, and nothing is cut or drawn on the way
to it, so a one-move toolpath is empty.

Immutable, for the reason `RenderBatch` gives — a toolpath is cached and drawn, and
something editable underneath a cache has to be watched. That also makes `Toolpath.Clone`
a shallow list copy that is a genuine deep copy, rather than duplicating fifty thousand
moves to achieve nothing.

`MoveKind.Cycle` is **reserved, not implemented**. The cycle's own parameters — peck,
dwell, retract behaviour — arrive with the drilling strategy that produces them; inventing
them first would be guessing. The enum value exists now so the stored numbering does not
have to shift later.

The operation owns a `Toolpath`; **`CLData` is derived from it when posting**, not stored.
One persisted artifact, and `docs/architecture.md`'s rule that posts consume `CLData` and
never `Toolpath` stays intact — the conversion is a deterministic pass in `GCam.Posts`.

Drill cycles stay as cycles rather than being expanded into moves, because a post has to
emit `G81`/`G83` to get the machine's own pecking behaviour, and re-deriving a cycle from
expanded points is lossy. Simulation expands them; posting does not.

Millimetres, in the operation's frame. Conversion to metres stays at the SOLIDWORKS edge
as the units rule requires.

## Persistence

Inside the SOLIDWORKS document, third-party storage, following the constraints already
recorded under "Rules with teeth" in [architecture.md](../architecture.md) — read and
write only on `LoadFromStorageNotify`/`SaveToStorageNotify`, and match every
`IGet3rdPartyStorage` with a release even when it returns null.

`IGet3rdPartyStorageStore` gives an `IStorage`, so:

```
GCam                          (the third-party store, one name under 30 chars)
  model.xml                   jobs, operations, parameters, the tool list — versioned XML
  tp0001, tp0002, …           one binary stream per generated toolpath
```

The model is XML because it is small, diffable and readable when something goes wrong.
Toolpaths are binary because a few thousand moves as text adds megabytes to every part
file and to every save. A toolpath stream that cannot be read costs a regeneration, not
the job — so a corrupt path never takes the parameters down with it.

**Stream names must be short.** The third-party storage name is capped at 30 characters,
and structured-storage element names are capped around 31 — so a 36-character operation
GUID **cannot** be a stream name. Streams are therefore named `tp<n>` from a counter, and
`model.xml` records the operation-id → stream-name mapping. *(The 31-character element
limit is **Assumed** — it is the documented OLE structured storage limit, not something
G-CAM has tested. Confirm by writing a 40-character element name before relying on it.)*

Everything carries a schema version, and unknown elements round-trip through `Extra`
rather than being dropped, exactly as the tool library reader already does.

**The format lives in Core** (`Core/Persistence`), with `GCam.SolidWorks` responsible only
for getting bytes into and out of the document's storage. That is what lets a full
round-trip — tools, jobs, operations, settings, selections, heights — be a headless test.

**The part's tool list is written through the tool library writer**, as a
`gcamToolLibrary` element embedded in the document. Not tidiness: two tool serialisers
would eventually disagree, and the same tool would read back differently depending on
whether it came from a library or a part. Reading it back found a real gap —
`SourceLibraryId` was not in the library format at all, because a tool in a library *is*
from that library, but in a part it is the only record of where the tool came from and the
only route back to it.

**Reading never loses the part over one bad operation.** An operation whose strategy this
build does not have is skipped and reported by name; a parameter that will not parse falls
back to its default; a truncated toolpath stream keeps the moves that survived. Only two
things stop a load: XML that will not parse, and a document whose format version is newer
than this build — half-reading that could lose data silently.

Three states are corrected on load rather than trusted:

- An operation claiming `Generated` with **no stored toolpath stream** becomes
  `NotGenerated`. Otherwise it draws nothing while calling itself up to date.
- An operation saved mid-run comes back `NotGenerated`, never `Generating` — nothing is
  generating it now, and `Generating` is the one state the UI cannot clear by itself.
- `Stale` is kept, because the toolpath is kept.

### Templates

A template is an operation **without geometry, tool identity or state**: the strategy, an
embedded tool definition, and the parameters. G-CAM writes its own versioned XML, laid
out to mirror the HSMWorks file so the concepts line up one-to-one, and a separate
importer reads `.hsmworks-template`, mapping the parameter names it recognises and
preserving the rest.

This is why settings serialise through a **`ParameterBag`** of named values rather than
straight to XML: the same names have to serve both the part document and the template
files, and writing settings twice is how the two drift apart. `WriteParameters` and
`ReadParameters` are **abstract**, not virtual — a strategy that forgets to save a
parameter loses it silently on the next reopen, and the loss is invisible until someone
notices a toolpath came out different. The same reasoning that makes `PmpHandlerBase` wrap
all 37 callbacks.

Selections deliberately do not go through the bag, which is what keeps templates portable.
The document format writes them separately, finding them through
`IContourSelectionOwner` — a seam narrow enough that a serialiser never has to know
`Contour2dSettings` by name, and typed enough that a drill operation cannot store a
contour.

This is the same split that already works for tool libraries: our format is writable,
theirs is read-only ([0002](../decisions/0002-imported-libraries-are-read-only.md)).
Writing HSM's format back out would mean emitting their expression syntax — which
[0006](../decisions/0006-operation-parameters-are-values.md) deliberately does not
implement — and would be lossy in both directions.

The importer cannot evaluate expressions, so a parameter whose expression is not a
literal is imported as the strategy's default and listed in the import report. Most
template parameters *are* literals; the derived ones (`tool_feedPerTooth`,
`tool_surfaceSpeed`, `highFeedrate`) are exactly the values G-CAM computes itself anyway.

## Drawing

`ToolpathMesh` turns a `Toolpath` into `RenderBatch`es the same way `BoxMesh` turns stock
into one — Core says what to draw, `GCam.SolidWorks` says how.

- **One scene layer per operation**, named `toolpath:<operation id>`, so an operation can
  be shown or hidden without touching the others. Layer naming is already the
  coordination mechanism the renderer expects.
- **Colour by move kind** — cutting, lead, link, rapid, plunge — because the mistakes
  worth catching by eye are a rapid through the stock and a bad lead-in, and neither is
  visible when a whole operation is one colour.
- **Rapids toggle separately.** On a drilling job they dominate the screen.
- **A stale toolpath draws dimmed** rather than disappearing. Hiding it would lose the
  only picture of what the machine last did; drawing it at full strength would claim it
  matches the current parameters.
- Selecting an operation in the tree is what shows it, exactly as selecting a job is what
  shows that job's stock — the same signal, applied a row at a time. **Selecting a job
  shows no toolpaths**, and selecting an operation shows no stock; select several
  operations to compare their paths. See "The 3D preview" in [jobs.md](jobs.md).

Everything is `LineStrip`: consecutive moves of one kind become a single strip, and where
the kind changes the next strip **starts at the vertex the last one ended on**, so there is
no gap at the boundary. A gap there reads as a bug in the strategy rather than in the
drawing. `Lines` was the earlier plan for rapids, on the assumption that dashes would need
it — they would not, since `glLineStipple` works on a strip, and a strip is half the
vertices.

Arcs are tessellated in `ToolpathMesh`, not stored as points: a post emits G2/G3 and a
machine runs an arc better than a thousand chords, so the arc survives all the way to the
post and whatever needs points makes its own. The screen's tolerance (0.05mm) is far
coarser than a machining one, which is most of the saving. A degenerate arc — zero radius,
endpoints that do not lie on one — draws as a straight line rather than vanishing, because
nothing drawn looks like a gap and sends someone hunting in the wrong place.

**Vertices are carried into part coordinates before the batch is built**, by a required
`Matrix4` argument — the same shape `BoxMesh.Corners` and `AxisTriad.Build` already take.
`RenderBatch` promises part coordinates and a toolpath is computed in the operation's
frame, so on a job whose coordinate system is rotated, omitting it draws the whole path in
the wrong plane. That happened: the first generated toolpath came out perpendicular to the
face it was cut from. The parameter is required rather than optional because the symptom
looks like a broken toolpath rather than a broken transform, and sends you looking in the
wrong place.

`AlwaysOnTop` is not set. `RenderBatch`'s own remarks anticipate a toolpath buried in
material wanting it, and that is a judgement best made with something on screen to look at
— it lands when the preview is wired up.

## The property page

`OperationPropertyPage` derives from `GCamPropertyPage` and is rebuilt for every show like
every other page. It is **five tabs, in HSMWorks' order**, and nothing above them.

**The operation's name is the panel title, not a field.** Renaming belongs to the job tree,
which already does it in place, so a name box on the page would be a second way to do one
thing — and the panel has to say which operation is being edited anyway. The page also has
no message box: `GCamPropertyPage.Message` is optional, and a caption nobody reads twice
costs height on every show.

| Tab | Built by | Contents |
| --- | --- | --- |
| Tool | The base page | Two groups: the tool's name as a header with Browse…, then feed and speed — one physical cutter, but numbers that belong to this operation alone |
| Geometry | The strategy | Selection box, then the controls for the highlighted contour — two propagation checkboxes and Reverse — a line naming that contour and saying which are reversed, the tangential extension that applies to every open contour, and in the 3D view each contour highlighted with an arrow beside it — see below |
| Heights | The base page | Five mode + offset rows, and a plane in the 3D view for each — see below |
| Passes | The strategy | Stepover, stepdown, tolerance, and stock to leave in a group whose header checkbox turns it off without clearing the amounts. Either amount may be negative, which cuts past the profile rather than short of it — radial stock past the cutter radius carries it to the other side, leads and all |
| Linking | The strategy | Lead-in/out, ramping, retracts — only for strategies that have them |

Tool and Heights are common to every strategy; Geometry, Passes and Linking are
contour2d's own. **The seam between them is not built yet**, but the tabs now fall on it
exactly: a strategy would contribute three tabs and the base page two. With one strategy an
`IPageBuilder` would be an abstraction with a single implementation — the project's own
rule says to wait for the second caller — and `BuildToolTab`, `BuildGeometryTab` and the
rest are named so extracting it stays mechanical.

**Tabs are built, never rearranged.** `AddTab` and `IPropertyManagerPageTab.Activate` are
both build-time only, like every other part of a page's shape, so the page cannot switch
tabs while it is up. That is why `OnTabClicked` records the tab id and `BuildControls`
re-activates it: without that, picking a tool would rebuild the page and throw the user
back to the first tab.

Two page-killing mistakes the base class already guards, recorded in
`docs/solidworks-api/property-manager-pages.md`: control ids must be unique per page
(duplicates are accepted in silence), and `IPropertyManagerPageControl.Visible` must never
be touched on a page about to be shown. A third is a load-order rule rather than a crash —
**values are set in `LoadControls` before `Show2`, but selections are restored in
`PageShown`**, because `SelectByID2` routes by mark and the marks belong to boxes on a page
that actually exists.

**Restoring a selection begins by clearing the selection**, which is not tidiness.
`IEntity::Select4` *deselects* an entity that is already selected while a page with a
selection box is up, so restoring onto a live selection turns every contour back off and
leaves the box empty — with the references resolving perfectly the whole time, so the
toolpath still cuts the right geometry. An empty box beside a working toolpath is that
bug's signature; see
[coordinate-systems.md](../solidworks-api/coordinate-systems.md). For the same reason the
page drops its selections when it closes, so they cannot be stale next time.

**Selection callbacks are ignored unless the page is open and staying open.** SOLIDWORKS
empties a page's boxes as it takes the page apart — including the close a rebuild does on
its way to showing the page again — and those callbacks are indistinguishable from the user
clearing the box. Acting on them commits an empty contour list over the real one.

A live preview follows the edit, as the Job page already does with its clone: the page
edits a clone, the preview shows the clone, Cancel leaves nothing behind. **The
cut-direction arrows are the part of that which exists**; the stock-and-heights preview
and regenerating the toolpath as parameters change are not — see the gaps below.

**Which side of an edge will be cut is the question the Geometry tab could not answer.**
Until the toolpath exists there is nothing on screen saying whether the cutter runs inside
or outside a profile, and by the time there is, the page has been accepted. So each contour
is drawn along its own length, with one arrow beside it in the contour's own plane, on the
cutter's side and pointing the way it travels. Both redraw on every pick, on
Climb/Conventional and on Reverse, and go when the page closes.

**The Geometry tab's controls act on one contour — the highlighted row**, and the status
line names which, because the rows themselves cannot be annotated. Highlighting a
different row brings that contour's modifiers up, which needs a notification SOLIDWORKS
only sends when asked; see
[property-manager-pages.md](../solidworks-api/property-manager-pages.md). With no row
highlighted they act on the most recent pick, which is the state the box is in straight
after picking. A new pick inherits whatever the checkboxes show, so picking a run of edges
the same way does not mean setting each one afterwards — the defaults on
`ContourSelection` are therefore what a *new operation* starts from and nothing else.

**The highlight follows the chained contour, not the selection**, which is the half
SOLIDWORKS cannot show: it already lights up what was picked, and what gets cut is what
those picks chained into. The two differ wherever an edge is reached by tangent propagation
rather than by being clicked. A contour whose side cannot be measured is still highlighted;
only its arrow is missing.

Both are drawn without depth testing. For the arrow that is ordinary annotation behaviour;
for the highlight it is structural — the line lies exactly on a model edge, so testing it
against the part it sits on is z-fighting by construction, and it would break into stipple
as the view moved.

**Flat lines rather than HSMWorks' 3D tubes**, because the overlay draws unlit on purpose
(see [opengl-overlay.md](../solidworks-api/opengl-overlay.md)). An unlit tube renders as a
flat ribbon — all of the cost, none of the roundness — and its radius would be in
millimetres, so it would swell with zoom where a line width in pixels does not. Tubes
become worth revisiting only alongside lit rendering, which needs normals, a second vertex
array and material state inside SOLIDWORKS' own context.

**The arrow reads the side off a real offset rather than re-deriving it.**
`Contour2dCutSide` offsets the contour a probe distance through `Contour2dOffsetting` —
the same call `Contour2dStrategy` makes to place the cutter — and measures which way the
result moved. The two cannot disagree, which matters more here than anywhere else on the
page: an arrow pointing at the wrong side of an edge would be believed. It is also not a
rule worth holding twice — a closed contour takes its side from a sign and an open one
from a named hand, so there is no single perpendicular to write down. A contour whose side
cannot be measured gets no arrow rather than a guessed one.

The arrow is anchored to the midpoint of the contour's longest segment **as picked**, not
as walked. Halfway along lands exactly on a corner of a rectangle, where the offset is not
parallel to the contour and the side reads diagonally; and anchoring to the re-oriented
walk makes the arrow jump to the opposite edge when the direction changes instead of
turning round.

**The Heights tab draws a plane per height**, because a height is otherwise a number and a
datum, where what anybody wants to know is whether the tool clears the clamps and where the
cut stops. Clearance, Retract and Top are yellow — all three are somewhere the tool passes
through air — with green for Feed and blue for Bottom, the two that are about the cut
itself. Each is sized to the model and the stock together, whichever reaches further, plus
a margin: most heights are measured from the stock, so a plane stopping at the model would
not reach the thing it is measured from.

**Only while that tab is in front**, which is the page's decision rather than the preview's:
these planes span the part, and left up behind the Geometry tab they would bury the contours
that tab is about. `_activeTab` is tracked for the rebuild already, so there is nothing new
to watch.

**Redrawn at idle after a tab click, never from inside it.** `OnTabClicked` only records the
tab; the planes and the active selection box catch up on the next idle. Drawing from the
click itself intermittently left the page stuck on the previous tab — see
[property-manager-pages.md](../solidworks-api/property-manager-pages.md).

**Outlines always, and a fill on the one being edited** — which is also how you tell five
stacked outlines apart, so the fill is the labelling rather than decoration. The trigger is
focus on that height's offset box, through `OnGainedFocus`/`OnLostFocus`. Moving between two
boxes raises both a loss and a gain and SOLIDWORKS decides the order, so the loss only
clears the fill if it is still the height that lost it.

The outline ignores depth and the fill does not, which is not an inconsistency: the outline
says *where* the plane is and has to be readable behind the part — at the model's own top or
bottom face it would otherwise z-fight with the face it sits on, and Bottom defaults exactly
there — while the fill says *which* plane is being edited and is worth more for being
occluded. A plane under the part, seen from above, should read as an outline poking past the
silhouette rather than as a wash over the model it is beneath.

Heights resolve through `OperationHeights.TryResolve(kind, …)`, the same Core rule
generation follows, one at a time — so a plane cannot sit somewhere the cut will not,
including a retract that has been lifted to the feed height. A height that will not
resolve costs only its own plane, and that of any height measured from it — a
`FromSelection` height with nothing picked yet, most often, or a `FromContour` one, which
never has a plane.

**Choosing `Selection` shows a box under that row** taking a face, edge or vertex.
`EntityHeights` reads its Z, and accepts only geometry that lies at a single one: a flat
face — read from `ISurface.PlaneParams` and checked square to the job's Z, not measured —
a flat edge including a circle, or a vertex. A sloped or cylindrical face is refused,
because "the height of that" has no one answer and any of the plausible ones would be a
decision the user cannot see. Generation and the planes share the call, so a plane cannot
sit where the cut will not.

**Accepting the page generates the operation.** Committing is the ask for a toolpath, and a
new operation accepted without one shows nothing at all. Only that operation: an edit marks
the ones below it stale, and regenerating those stays the user's call. It may fail — no
tool, nothing selected — which is the honest answer to what was just accepted; the queue
records it on the operation and the tree shows it.

**The tool is a header label and a Browse button, not a drop-down**, which is HSMWorks'
shape and — it turns out — the only shape available. A shown page's controls cannot be
written to at all: `Combobox.Clear`, `Combobox.InsertItem` and `Label.Caption` have each
killed SOLIDWORKS on their first call, with nothing in any log (verified 2025 SP3, see
[property-manager-pages.md](../solidworks-api/property-manager-pages.md)). Browse is what
puts a tool in the part, so the set of tools necessarily changes while the page is up, and
no drop-down could survive that whatever the refresh mechanism.

**Picking a tool therefore rebuilds the page** rather than updating it, through
`GCamPropertyPage.RebuildAfterHandlerReturns`. The rebuild is invisible apart from a
flicker: the page is rebuilt for every show anyway, edits in progress live on the clone
and are reloaded, and the selections come back with `PageShown`.

It reads "No tool chosen" when there is none. That state has to be visible: it is where
every new operation starts, and on a part with no tools it is the only state until Browse
is used. The very first version of this page had no way to say it — a not-found tool id
was clamped to the first row — so the page displayed a tool while `Operation.ToolId` was
still null, and generation refused with "has no tool" against a page that plainly showed
one.

**Only a tool reachable through a library can be chosen.** Browse opens the library
browser, and a tool already in the part is re-picked from the library it came from, which
works because `AddTool` is idempotent by `Tool.Id`. The gap is a part tool whose library
has gone — from an old document, or a machine that never had that library — which cannot
be selected at all. The fix is the part-tool list the browser is meant to grow, described
above; it is the same feature, and it closes this at the same time.

### What the page does not have yet

Recorded here rather than left as an impression of completeness. None of these are
decisions to leave them out; they are unbuilt.

| Gap | Notes |
| --- | --- |
| `Comment` | Exists on `Operation` and is committed by the page, with no control to set it |
| `Enabled` | Not on the page. Set from the job tree's Suppress command instead — see [In the job tree](#in-the-job-tree) — which is where HSMWorks puts it too |
| Reading a height off a *sloped* face | `FromSelection` works for anything at a single Z — see the Heights tab above. A face that is not square to the job's Z is refused rather than guessed at, which is the right answer until somebody decides what it should mean |
| `Operation.Validate()` | Never called. OK commits an operation with no tool or no contour without saying so, and the complaint arrives at generation time instead |
| Lead `Distance`, `Sweep`, `VerticalRadius`, `Perpendicular` | Stored, round-tripped and read by the strategy; not editable |
| The derived feeds and speeds | Surface speed and feed per tooth are meant to be editable at both ends (see above); only the canonical values have boxes. `FeedsAndSpeeds` is already in Core |
| Conditional visibility | Maximum stepdown shows when multiple depths is off; the lead-out radius shows when "same as lead in" is ticked. `JobPropertyPage.ShowControlsFor` is the pattern to copy |
| The rest of the live preview | The cut-direction arrows are built. The stock and heights are not drawn while the page is up, and parameter edits do not regenerate the toolpath — only accepting the page does |
| Roughing a pocket out | The Reverse button cuts the inside of a closed contour, so a finishing pass round a pocket wall works. Clearing the material in the middle of one is a different strategy, not a setting on this one |

## In the job tree

An operation is created and edited on its property page, but everything you do *to* one —
delete it, copy it, rename it, suppress it, generate just that one — is the tree's context
menu. The menu itself is described in [UI shells](ui-shells.md); what matters here is what
each command means to the model:

| Command | Means |
| --- | --- |
| Rename | `Job.RenameOperation`. Unique within the job, blank refused. **This is the only way to rename an operation** — the page deliberately has none, using the name as its panel title |
| Generate | The same `GenerationQueue` as a job, holding one item, so a solo generate takes exactly the path it would inside a job — including how a failure is recorded |
| Suppress | `Operation.Enabled`. The operation keeps its toolpath and parameters, is skipped by generation, is not drawn, and posts nothing. Greyed out in the tree so the toolpath vanishing has a visible cause |
| Duplicate | `Job.DuplicateOperation`. Directly after the original, fresh id, no toolpath |
| Delete | `Job.RemoveOperation`, after asking. Final — there is no undo, and the toolpath goes with it |

**Suppressing does not make the operation stale.** Nothing about it changed, so its own
path is still exactly what its parameters describe. What changed is the stock reaching
everything below it, in both directions, which is what
`Staleness.OperationEnabledChanged` covers. Deleting and duplicating use
`OperationOrderChanged` for the same reason.

### The state badge

An operation's glyph carries a small disc in its corner saying whether its toolpath can be
believed. `OperationStatus` in `Core/Model` decides which — in Core, not the viewmodel,
for the reason architecture.md gives for `ToolSearch`: it is a rule, and Core is the only
place a headless test reaches it. The tree owns *where* the badge is drawn; Core owns
*whether* there is one.

| State | Badge | |
| --- | --- | --- |
| `Generated` | none | |
| `Warning` | amber **!** | There is a usable path; the message says what to read |
| `Stale`, `NotGenerated`, `Failed`, `Generating` | red **✕** | Nothing here to trust yet |

**Three badges for six states, deliberately.** The badge answers a narrower question than
the state does — *can I believe this toolpath?* — and an icon distinguishing all six would
be decoded rather than glanced at. Nothing is lost, because the state's own words are in
the tooltip. Splitting `Failed` out from the other three is one `case` if it turns out to
be wanted.

**`Generating` keeps the error badge rather than clearing it.** Blanking it for the length
of a run would read as "done" while the operation still has nothing anyone should believe.
It is unobservable today, but no longer because nothing repaints mid-run — see the note
below, which does: the rows are rebuilt from the model only once the run has finished, so
no badge changes while it is going on whatever gets painted.

### "(generating…)"

**The row the queue is working on says so, after its name** — `JobTreeNode.Note`, set from
the progress the queue already reports and cleared by the null report that ends a run.
One row at a time: two rows claiming to be generating would be a lie about what is running.

**Drawing it is the whole difficulty, and the reason there is no percentage.** Generation
runs on the SOLIDWORKS thread, which is the thread that paints the tree, so the note would
otherwise be set, never drawn, and cleared again while the window sat frozen.
`JobTreeViewModel.ShowGenerating` waits for the dispatcher to reach `Render` priority,
which forces the paint to happen there and then. **Render, never Input**: Input is the
lower priority, so queued clicks and keystrokes are not dispatched while it waits — pumping
at Input would let someone delete the operation being generated, which is the re-entrancy
that has already taken SOLIDWORKS down here once.

`GenerationProgress.OperationPercent` exists and is deliberately unused. A 2D contour runs
in milliseconds and reports once per contour, so a percentage would flash past unread; it
becomes worth showing when generation moves off the SOLIDWORKS thread, which is also when
this repaint trick stops being necessary.

**The words live beside the badge, in `OperationStatus.Label`.** The tooltip is their only
caller today; the posting warning and the generation report are meant to be the next two.
Six states described in three places is how `Stale` ends up with three different names in
one product. `Describe` puts `StateMessage` underneath, which for a failure is the only
place the reason appears outside the log, and says "Suppressed" first when it is — because
that operation's state will not change however often the job is generated.

Drawn as vector geometry (an `Ellipse` and a `Path`, switched by `DataTrigger` on the
node's `Badge`), matching `ToolTypeIconConverter`'s reasoning: crisp at any DPI, and the
colour is ours rather than the font's. The white ring around the disc is what keeps it
legible against the glyph behind it.

**A job's icon does not roll its operations up.** A job with twelve operations, one of them
stale, looks no different from a clean one until it is expanded. HSMWorks does roll up, and
it is what would make a collapsed tree worth reading before posting — deliberately left for
when someone wants it, since it needs a rule for what "worst" means and a refresh whenever
any operation's state changes.

**A deleted operation's toolpath leaves the screen through the refresh**, not through
anything that knows it was deleted: `JobPreview` tracks the layers it put up and rebuilds
them all from the job it is handed. Its stored stream is a different matter — see
[storage](../solidworks-api/third-party-storage.md#deleting-leaves-streams-behind).

## Validation and state

`Operation.Validate()` returns the problems a user can act on, in the same shape as
`Job.Validate()`: no tool chosen, heights inverted, empty selection, a reference that no
longer resolves, a tolerance of zero. The tree shows state per operation as a badge on its
icon with the message in the tooltip — see [The state badge](#the-state-badge) — and the
detail in the log.

| State | Means |
| --- | --- |
| `NotGenerated` | Never generated, or generation was cancelled |
| `Generating` | In the queue or running; carries the percentage |
| `Generated` | Toolpath matches the inputs as far as we know |
| `Stale` | Inputs changed since the toolpath was made |
| `Warning` | Generated, but something is worth reading — a dropped selection, a reference that moved |
| `Failed` | Generation could not proceed; the previous toolpath, if any, is kept |

## Build order

Not all at once. Each slice is a coherent piece that compiles, tests and can be looked at
before the next one starts. Ordered so the shapes everything else binds to settle first,
while they are still cheap to change.

| # | Slice | Why here | State |
| --- | --- | --- | --- |
| 1 | `Heights/` + `FeedsAndSpeeds` + `GeometryRef` | The only parts of the base that are *logic* rather than data, so the only parts a test can prove. Pure Core, no dependencies on anything unbuilt | **Done** — 2026-09-13, 49 tests |
| 2 | `Operation` rewrite + `StrategyId`/`StrategySettings`/`StrategyCatalog` + `Contour2dSettings` + `ContourSelection` | The shape everything else binds to. Cheapest to change now, most expensive once persistence has written it into saved parts | **Done** — 2026-09-13, 65 tests |
| 3 | `JobDocument.Tools` + `ToolUsage`, seeding `Operation.Cutting` from a tool | Pure Core, and it unblocks the part-tool list in the library browser | **Done** — 2026-09-13, 20 tests |
| 4 | `Toolpath`/`Move` + `ToolpathMesh` | First visible payoff: a hand-built path drawn through the existing renderer, before any strategy exists | **Done** — 2026-09-13, 32 tests |
| 5 | `GenerationQueue` + `Staleness` | Testable against a fake strategy; needs no real one | **Done** — 2026-09-13, 35 tests |
| 6a | The stored formats in Core — `GcamDocumentXml`, `ToolpathBinary`, `ParameterBag` | Needs the model above it to be settled. Pure Core, so a full round trip is a headless test | **Done** — 2026-09-13, 31 tests |
| 6b | The SOLIDWORKS storage plumbing — third-party storage, the load/save notifications, release discipline | The half that cannot be tested headlessly, and the first code in `GCam.SolidWorks` for operations | **Done** — 2026-09-13, verified by hand on 2025 SP3. See [third-party-storage.md](../solidworks-api/third-party-storage.md) |
| 7a | `Contour2dStrategy` + `Polyline` + offsetting via Clipper2 | The first real toolpath, and pure Core so the geometry can be asserted headlessly | **Done** — 2026-09-13, 24 tests |
| 7b | `SolidWorks/Extraction` — selections → tessellated contours, `GenerationContextFactory`, Generate in the tree menu, toolpaths drawn | The half that needs a real part, and what makes 7a visible | **Done** — 2026-09-13, verified by hand on 2025 SP3 after two fixes: a missing part-frame transform, and a lead-in that plunged onto the wall |
| 7c | A stopgap creation path: New Operation builds a contour operation from the current selection | **The build order had a hole**: the property page was last, and it is the only thing that can create an operation — so slices 5, 7a and 7b were all unverifiable. This unblocked them | **Superseded by 8** — the stand-in tool and fixed defaults are gone; New Operation still seeds from the selection, which is worth keeping |
| 8 | The Operation property page | The real way to create and edit one. It replaces the guessing in 7c, not the creation itself | **Done** — 2026-09-13, verified by hand on 2025 SP3. The gaps are listed above |
| 9 | Choosing a tool: Browse on the page → the library browser as a picker → `JobDocument.Tools` | 8 left no way to put a tool in a part, and an operation with no tool cannot generate | **Done** — 2026-09-13, verified by hand on 2025 SP3. Three crashes taught that a shown page's controls cannot be written to at all; picking a tool rebuilds the page instead |
| 10 | Open profiles: single-sided offset, open chains kept, per-contour Reverse | A partial selection was refused outright, which is most of what a 2D contour is used for. Only the offsetter was blocking — the strategy already cut several contours, and chaining already produced open ones | **Done** — 2026-09-13, 21 tests |
| 11 | Three bugs the slices above exposed: leads on the wrong side, no lead-out on an open profile, and selections that would not restore | Each was invisible until something else worked. None is a slice; they are here because the build order is the record of what was actually done | **Done** — 2026-09-13, 9 tests, verified by hand on 2025 SP3 |
| 12 | The tree's operation commands: rename, generate, suppress, duplicate, delete — and the dirty-marking every tree edit was missing | Slice 8 made operations creatable and editable and left no way to get rid of one. Two bugs came out with it: <kbd>Del</kbd> on an operation deleted its whole job, and no tree edit ever marked the part dirty | **Done** — 2026-09-15, 13 tests |
| 13 | `OperationStatus` and the state badge in the tree | `OperationState` had been modelled, persisted and corrected on load since slice 6a, and was invisible: a stale operation looked exactly like a generated one, which made staleness a rule nobody could act on | **Done** — 2026-09-16, 18 tests |
| 14 | `PartRebuildWatcher` — the rebuild that nothing was listening for | Slice 13 made staleness visible, which is what exposed it: `Staleness.ModelRebuilt` had been written and tested since slice 5 with no caller anywhere, so editing a dimension invalidated nothing | **Done** — 2026-09-16, 3 tests. **Not yet verified on 2025 SP3** |
| 15 | The contour modifiers: `EdgePropagation` + `ModelEdgeTopology`, the two checkboxes, and travel/side split the way HSMWorks does it | The modifiers had been stored since slice 2 and honoured by nothing, so a selection meant one edge however it was picked. Doing it properly forced the cut-side rules into HSMWorks' shape, because a Reverse that changes what is *in* the chain cannot also be the only way to change the side | **Done** — 2026-09-20, 8 tests, verified by hand on 2025 SP3 after three bugs it exposed: see below |
| 16 | Heights that follow other things: `FromContour` (and 2D contour's default bottom), `FromTop` for feed, `FromRetract` for clearance, and a retract below feed lifted rather than refused | Every height was a property of the operation alone, so a part with profiles at several levels needed an operation per level | **Done** — 2026-09-23, 43 tests; the contour mode verified by hand on 2025 SP3 |

**The hole this order had.** Putting the property page last assumed generation could be
verified some other way. It could not: the page is the only thing that can create an
operation, so everything above it was untestable until a stopgap creation path was added
as 7c. Worth remembering when ordering the next subsystem — "can this slice be exercised
at all?" is a different question from "does this slice depend on that one?".

**Each slice exposed the bugs in the one before it.** Everything in slice 11 had been
shipped and "working" for a while, and none of it could have been noticed sooner:

- The leads had always been on the wrong side, but nothing cut an outside profile and
  looked at it closely until there was a tool, a page and a picture on screen.
- Open profiles had no lead-out at all, because the point giving the exit direction was
  read as though every chain were closed — unreachable until slice 10 let an open chain
  through.
- Contours would not restore into the page, which needed a page that reopened often enough
  for anyone to care.

**Slice 15 found three faults of its own, and none of them was in the new code.** All
three had been shipped for a week and were invisible until a chain ran further than one
edge:

- **Arcs tessellated the long way round.** `ICurveParamData.Sense` was ignored, so a
  reversed-sense fillet came back as the complementary arc — a 1mm radius cut as the 4.4mm
  arc that is not there. It reached the toolpath, not only the preview. See
  [edge-tessellation.md](../solidworks-api/edge-tessellation.md).
- **Deleting a row from the selection box is often not reported**, so the page kept
  contours the user had removed and would have committed them on OK.
- A junction with one tangent continuation and one edge merely at the same height was
  read as a branch, which stopped the walk at exactly the fillets it exists to cross.

The pattern is worth naming: **a slice's real test is the slice after it.** Marking one
done because its own tests pass says nothing about whether it is right, and the three
above were all found by looking at the screen rather than by the suite. The suite's job
was to keep them fixed — which is why each of them got a test that was checked against the
broken code first.

**The build order also had the hole twice.** Slice 8 deleted 7c's stand-in tool —
correctly, it was a stopgap — but the page it put in its place only *selects* from
`JobDocument.Tools`, and nothing filled that list. `AddTool` had exactly two callers, both on the document-load
path, so a fresh part had an empty drop-down, no operation could name a tool, and
`GenerationContextFactory.ResolveTool` refused every generate. Slice 9 exists to close
that. The lesson is narrower than the first one and worth having on its own: **when a
slice removes a stopgap, the thing the stopgap stood in for is part of that slice**, not a
follow-up. It read as complete because the page was the visible half and the visible half
worked — on a part that already had tools stored in it.

Two orderings were considered and rejected. **Rendering first** (slice 4 before 2) would
show something in the 3D view on day one, but the renderer is already proven by the stock
box and the triad, so there is less risk to retire there than it looks — and a `Toolpath`
designed before `Operation` settles tends to get reshaped when it does. **Persistence
early** would close the "nothing is saved" gap sooner, at the cost of writing a format for
a model that is still changing shape.

## What this defers

- **Per-operation coordinate systems are modelled but restricted.** `OperationFrame`
  defaults to inheriting the job's, and may override origin and orientation, which is the
  groundwork for 3+2. Until a 3+2 post exists, `Validate()` reports an override whose Z
  axis is not parallel to the job's as not machinable on three axes — modelled, stored,
  drawn, and refused at post time rather than silently producing a part nobody can cut.
- **Simulation** consumes `Toolpath` and is not designed here.
- **Multi-select editing** of several operations at once.
- **The strategies themselves.** This document defines what every strategy plugs into;
  `Contour2dSettings` and the contour algorithm are the first to be written against it,
  with `face`, `adaptive2d` and `drill` designed for but not built.
