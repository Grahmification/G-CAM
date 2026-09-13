# Operations

What every operation has regardless of strategy, how a strategy adds the rest, and how
one gets generated, stored, drawn and edited. Read this before touching `Core/Model`,
`Core/Strategies`, `Core/Generation`, the Operation property page or toolpath rendering.

Built as far as slice 7 of the build order at the end of this document: a 2D contour
generates from edges selected on a real part, draws, and persists. Nothing has been
posted, and the Operation property page is still a shell — operations are created from
the current selection with stand-in defaults until it exists.

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

`Reversed` is one flag rather than an inside/outside setting, because which side the cutter
runs on follows from the direction the chain is walked and the climb/conventional choice.
It is the same thing HSMWorks' per-contour arrow toggles.

None of this is honoured yet — the flags are stored and round-tripped, and propagation
lands with the contour strategy. The shape exists now because persistence would otherwise
freeze the wrong one into saved parts.

**Tangential extension is a different thing and is not here.** `tangentialExtensionDistance`
and its family are strategy parameters that act on an already-fixed selection, so they
belong in `Contour2dSettings` and arrive with the algorithm that honours them. The supplied
templates set them to 0.5mm and 1mm, so they are wanted — just not yet.

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
SOLIDWORKS, and runs on a worker thread. Strategies are pure: same context in, same
toolpath out. `IGenerationContextFactory` builds one, declared in Core and implemented in
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
- **A strategy with no implementation yet fails with a readable message.** Settings, a
  property page and persistence all exist before an algorithm does, so this is a real state
  rather than a placeholder — and it beats a null reference from inside the queue.
- **Cancelling mid-regeneration leaves the operation `Stale`, not `NotGenerated`**, when it
  already had a path. There is still a toolpath; it is out of date, which is exactly what
  it was before the cancelled run started.

An exception that is *not* a `GCamUserException` is a bug in a strategy. The operation
fails with "see the log", and the stack trace goes to the log rather than to the user.

### 2D contouring, the first strategy

`Contour2dStrategy` turns a closed profile into passes. One pass is: rapid across at
clearance, rapid down to the feed height, plunge to depth, lead in, cut the profile, lead
out, retract. Depths repeat that, and the tool retracts between them because a contour is
not guaranteed to be able to stay down — the profile may run outside the stock.

**A lead-in means the tool goes down off the profile.** The plunge lands at the start of
the lead arc — one radius back and one to the side, so r&#8730;2 from the wall — and the arc
brings it onto the profile tangentially. Plunging onto the profile and then arcing is the
bug that shipped first: a `Move` stores only its destination, so an arc from the profile
start *to* the profile start is zero-length, which the tessellator correctly reads as a
full circle. The cutter plunged onto the finished wall and looped right round it.

Three things in it are worth knowing before changing it:

- **Direction decides which way round, not which side.** The contour is oriented
  counter-clockwise for a climb cut and clockwise otherwise, and then offset by a single
  positive distance — so the cutter lands on the correct side either way, without a sign
  to get backwards.
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

What the strategy deliberately does **not** do yet, left as gaps rather than as wrong
numbers: open contours, ramped entry, arbitrary lead sweeps and perpendicular approach
(a quarter-turn arc is what comes out), multiple finishing passes, tabs, chamfering and
rest machining.

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
- Selecting an operation in the tree shows it, exactly as selecting a job shows its stock
  — the same signal, extended. Selecting a job shows all its generated operations.

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
| 2 | `Operation` rewrite + `StrategyId`/`StrategySettings`/`StrategyCatalog` + `Contour2dSettings` + `ContourSelection` | The shape everything else binds to. Cheapest to change now, most expensive once persistence has written it into saved parts | **Done** — 2026-09-13, 65 tests |
| 3 | `JobDocument.Tools` + `ToolUsage`, seeding `Operation.Cutting` from a tool | Pure Core, and it unblocks the part-tool list in the library browser | **Done** — 2026-09-13, 20 tests |
| 4 | `Toolpath`/`Move` + `ToolpathMesh` | First visible payoff: a hand-built path drawn through the existing renderer, before any strategy exists | **Done** — 2026-09-13, 32 tests |
| 5 | `GenerationQueue` + `Staleness` | Testable against a fake strategy; needs no real one | **Done** — 2026-09-13, 35 tests |
| 6a | The stored formats in Core — `GcamDocumentXml`, `ToolpathBinary`, `ParameterBag` | Needs the model above it to be settled. Pure Core, so a full round trip is a headless test | **Done** — 2026-09-13, 31 tests |
| 6b | The SOLIDWORKS storage plumbing — third-party storage, the load/save notifications, release discipline | The half that cannot be tested headlessly, and the first code in `GCam.SolidWorks` for operations | **Done** — 2026-09-13, verified by hand on 2025 SP3. See [third-party-storage.md](../solidworks-api/third-party-storage.md) |
| 7a | `Contour2dStrategy` + `Polyline` + offsetting via Clipper2 | The first real toolpath, and pure Core so the geometry can be asserted headlessly | **Done** — 2026-09-13, 24 tests |
| 7b | `SolidWorks/Extraction` — selections → tessellated contours, `GenerationContextFactory`, Generate in the tree menu, toolpaths drawn | The half that needs a real part, and what makes 7a visible | **Done** — 2026-09-13, verified by hand on 2025 SP3 after two fixes: a missing part-frame transform, and a lead-in that plunged onto the wall |
| 7c | A stopgap creation path: New Operation builds a contour operation from the current selection | **The build order had a hole**: the property page was last, and it is the only thing that can create an operation — so slices 5, 7a and 7b were all unverifiable. This unblocks them | **Done** — 2026-09-13 |
| 8 | The Operation property page | The real way to create and edit one. It replaces the guessing in 7c, not the creation itself | Next |

**The hole this order had.** Putting the property page last assumed generation could be
verified some other way. It could not: the page is the only thing that can create an
operation, so everything above it was untestable until a stopgap creation path was added
as 7c. Worth remembering when ordering the next subsystem — "can this slice be exercised
at all?" is a different question from "does this slice depend on that one?".

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
