# Operations

What every operation has regardless of strategy, how a strategy adds the rest, and how
one gets generated, stored, drawn and edited. Read this before touching `Core/Model`,
`Core/Strategies`, `Core/Generation`, the Operation property page or toolpath rendering.

Nothing here is built yet. `Operation` is currently a placeholder carrying an id and a
name, and this is the design that replaces it.

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
the same rule that put `ToolSearch` in Core.

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

Modes for v1: `FromStockTop`, `FromStockBottom`, `FromModelTop`, `FromModelBottom`,
`FromJobOrigin`, `FromSelection`. The rest of HSM's list is deliberately absent until
something needs it; each costs a resolver and a test.

Resolution is a pure function of a `HeightContext` — the stock and model Z extents in the
operation's frame, supplied by `GCam.SolidWorks/Extraction` — so every mode is testable
headlessly. This is what makes the heights *follow the stock*: change the stock and every
height derived from it moves, which is the property that makes the HSM model worth
copying rather than storing five plain numbers.

`OperationHeights.Validate()` enforces `clearance ≥ retract ≥ feed ≥ top > bottom` after
resolution and reports which pair is inverted. Validation runs against resolved values,
not modes, because two different modes can resolve to the same plane.

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

Persistent references, not names, from the start. `Job.ModelBodyNames` uses names today
only because jobs are not persisted; operations are persisted from day one, and a renamed
face must not silently change what a proven operation cuts.

## Strategy settings

```csharp
public abstract class StrategySettings
{
    public abstract StrategyId Strategy { get; }
    public abstract StrategySettings Clone();
    public abstract IReadOnlyList<string> Validate(Operation owner);

    /// <summary>True when this strategy machines what earlier operations left.</summary>
    public virtual bool DependsOnPrecedingStock => false;
}
```

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
resolved geometry, the resolved heights, the stock, and the frame — so a strategy touches
no COM and no SOLIDWORKS, and runs on a worker thread. Strategies are pure: same context
in, same toolpath out.

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

Watching for the rebuild means a third subscriber to the document notifications, after
the tab sync and the renderer. That is the trigger `docs/design/ui-shells.md` names for
factoring `Events/` out of `JobTreeTabs` — do it then, not before.

## The toolpath

```csharp
public sealed class Move
{
    public MoveKind Kind { get; set; }     // Rapid | Lead | Link | Cutting | Plunge | Retract | Cycle
    public Vec3 End { get; set; }          // mm, in the operation's frame
    public double Feed { get; set; }       // mm/min; 0 for rapids
    public ArcData Arc { get; set; }       // null for linear moves
    public DrillCycle Cycle { get; set; }  // null except for Cycle moves
}
```

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

### Templates

A template is an operation **without geometry, tool identity or state**: the strategy, an
embedded tool definition, and the parameters. G-CAM writes its own versioned XML, laid
out to mirror the HSMWorks file so the concepts line up one-to-one, and a separate
importer reads `.hsmworks-template`, mapping the parameter names it recognises and
preserving the rest.

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
- Selecting an operation in the tree shows it, exactly as selecting a job shows its stock
  — the same signal, extended. Selecting a job shows all its generated operations.

Cutting moves are `LineStrip`; rapids are `Lines`. Both kinds already exist in
`PrimitiveKind`.

## The property page

`OperationPropertyPage` derives from `GCamPropertyPage` and is rebuilt for every show like
every other page. It builds the groups HSMWorks uses, in that order:

| Group | Built by | Contents |
| --- | --- | --- |
| Tool | The base page | Part-tool picker, feeds and speeds, coolant |
| Geometry | The strategy | Selection boxes for what that strategy takes |
| Heights | The base page | Five mode + offset rows |
| Passes | The strategy | Stepover, stepdown, stock to leave, … |
| Linking | The strategy | Lead-in/out, ramping, retracts — only for strategies that have them |

The base page builds Tool and Heights once for every strategy; the strategy contributes
the rest through a `BuildGroups(IPageBuilder)` call. `IPageBuilder` is a thin seam over
`AddControl2` so that control ids stay unique per page and nothing has to touch
`IPropertyManagerPageControl.Visible` — both of which are page-killing mistakes recorded
in `docs/solidworks-api/property-manager-pages.md`.

A live preview follows the edit, as the Job page already does with its clone: the page
edits a clone, the preview shows the clone, Cancel leaves nothing behind. Parameter edits
redraw the *stock and heights* preview immediately; they do not regenerate the toolpath.

## Validation and state

`Operation.Validate()` returns the problems a user can act on, in the same shape as
`Job.Validate()`: no tool chosen, heights inverted, empty selection, a reference that no
longer resolves, a tolerance of zero. The tree shows state per operation with the message
in the tooltip and the detail in the log.

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
| 2 | `Operation` rewrite + `StrategyId`/`StrategySettings`/`StrategyCatalog` + `Contour2dSettings` | The shape everything else binds to. Cheapest to change now, most expensive once persistence has written it into saved parts | **In progress** |
| 3 | `JobDocument.Tools` + `ToolUsage`, seeding `Operation.Cutting` from a tool | Pure Core, and it unblocks the part-tool list in the library browser | Not started |
| 4 | `Toolpath`/`Move` + `ToolpathMesh` | First visible payoff: a hand-built path drawn through the existing renderer, before any strategy exists | Not started |
| 5 | `GenerationQueue` + `Staleness` | Testable against a fake strategy; needs no real one | Not started |
| 6 | Persistence — `model.xml`, then the toolpath streams | Needs the model above it to be settled, and writing it into saved parts is what makes earlier slices expensive to revisit | Not started |
| 7 | `Contour2d` strategy + the geometry extraction it needs | The first real toolpath. Everything above exists to be plugged into here | Not started |
| 8 | The Operation property page | Last, because a page for a model that is still moving is written twice | Not started |

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
