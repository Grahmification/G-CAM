# G-CAM architecture

The project structure for G-CAM and the rules that keep it intact — what applies
everywhere. How one subsystem hangs together is a design note in
[design/](design/README.md), linked from the table below.

**The tree below is the target layout, not a description of the repository.** Much of it does not exist yet. What is built as of 2026-09-13:

**Follow the design note before reading code.** Where an area has one, it is the fastest
way to find out how that part hangs together and which projects it spans.

| Area | State | Design note |
| --- | --- | --- |
| Solution, seven projects, build and test | Done | |
| `Core/Diagnostics` — logging, error policy, user exceptions | Done | [error-handling.md](error-handling.md) |
| `Core/Settings` — XML settings store | Done | |
| `Core/Tooling` — tools, holders, cutter profiles, libraries, HSM import, edit sessions | Done | |
| `UI/Views` — error dialog, tool library browser and editor, profile preview | Done. The browser doubles as the tool picker the Operation page browses with | [UI shells](design/ui-shells.md) |
| `AddIn` — CommandManager, COM registration | Done | [UI shells](design/ui-shells.md) |
| `SolidWorks/Hosting` — Manager Pane tab, one per open part, kept in sync by document events | Done | [UI shells](design/ui-shells.md) |
| `Core/Model` — Job, Stock, JobDocument, the part's tool list | Done, and persisted | [Jobs](design/jobs.md) |
| `Core/Geometry/Primitives` — Vec3, Bounds, Matrix4, Polyline | Started — what stock, rendering and contouring need | |
| `Core/Geometry/Offset` — 2D offsetting behind an interface, via Clipper2 | Done. Closed contours by orientation; one side of an open path by extracting it from Clipper's ribbon | [Operations](design/operations.md) |
| `Core/Rendering` — scene, layers, batches, colour, BoxMesh, ConeMesh, AxisTriad, ToolpathMesh, PreviewSelection | Done for what exists to draw | [Jobs](design/jobs.md) |
| `Core/Selection` — `MultiSelection<T>`, the click / Ctrl-click / Shift-click rules | Done | [Jobs](design/jobs.md) |
| `UI` — job tree: rename in place, a context menu per node kind, double-click and Enter to edit. Operations delete, duplicate, rename, suppress and generate from it, and show their state as a badge on the icon. Several rows select at once, and what is selected is what the 3D view draws. Rows drag to reorder — operations within a job or into another one, jobs among themselves | Done; multiple selection verified on 2025 SP3, **drag and drop not yet** | [Jobs](design/jobs.md), [UI shells](design/ui-shells.md) |
| `SolidWorks/PropertyPages` — handler base, shared page base, Job and Operation pages | Done (2025 SP3). The Operation page is five tabs and rebuilds itself to show a change; its real gaps are tabulated under "the property page" in the design note | [Operations](design/operations.md) |
| `SolidWorks/Selection` — selection boxes to bodies, coordinate systems and contour edges, stored and restored | Done (2025 SP3) | |
| `SolidWorks/Rendering` — GL interop, state guard, scene renderer, view hooks, job preview (stock box, origin triad, toolpaths) | Done | [Jobs](design/jobs.md) |
| `SolidWorks/Extraction` — transforms, model extent, contour tessellation, generation context | Done; a contour generates from selected edges on a real part (2025 SP3). Open chains are cut, not discarded | [Operations](design/operations.md) |
| `Core/Model` — Operation, heights, geometry references, Toolpath | Done | [Operations](design/operations.md) |
| `Core/Strategies` — id, settings base, catalogue, context, Contour2d | Contour2d generates; face, adaptive and drill are designed only | [Operations](design/operations.md) |
| `Core/Generation` — queue, progress, staleness rules | Done; runs on the STA thread until an `SwDispatcher` exists | [Operations](design/operations.md) |
| `SolidWorks/Events` — `PartRebuildWatcher`, one per part | Done; **not yet verified on 2025 SP3** | [Rebuild notifications](solidworks-api/rebuild-notifications.md) |
| `Core/Persistence` — the stored document format and the toolpath bytes | Done, round-tripped headlessly | [Operations](design/operations.md) |
| `SolidWorks/Persistence` — getting those bytes into the part | Done; a job survives a close and reopen (2025 SP3). A part with no jobs is never written to — **not yet verified**. Toolpath streams still unexercised | [Storage](solidworks-api/third-party-storage.md) |
| `Core/Simulation`, `Commands`, `Posting` | Not started | |
| `Core/Geometry` — Brep, Faceting, Query | Not started; only what contouring needed exists | |
| `Posts` | Empty project | |

**A 2D contour toolpath generates from selected edges on a real part** as of 2026-09-13 — open profiles as well as closed — draws in the 3D view, and is saved with the document. A tool is chosen from a library on the operation's own property page. **Nothing has been posted** — of the vertical slice below, only step 6 is missing, and `GCam.Posts` is still an empty project.

## Decisions this rests on

| Area | Choice |
| --- | --- |
| Runtime | .NET Framework 4.8 — see [0001](decisions/0001-target-net-framework-48.md) |
| Layout | Core / Posts / SolidWorks / UI / AddIn + tests; Core has zero SolidWorks references |
| Geometry | Extract SW geometry once into Core's own kernel, then compute in managed memory |
| Machines | 3-axis mill only |
| Toolpath display | OpenGL overlay on `BufferSwapNotify`, via hand-rolled P/Invoke (no OpenTK); fixed-function vertex arrays — see [0005](decisions/0005-opengl-overlay-with-vertex-arrays.md) |
| Simulation | Z-map heightfield material removal |
| Persistence | Inside the SOLIDWORKS document, third-party storage; generated toolpaths stored with the operation — see [0009](decisions/0009-persist-toolpaths-in-the-document.md) |
| Operation editing | SOLIDWORKS-native PropertyManager pages |
| Operation parameters | Typed values, not HSM's expressions — see [0006](decisions/0006-operation-parameters-are-values.md); strategy parameters are typed classes — see [0007](decisions/0007-typed-strategy-settings.md) |
| Tooling in a part | One tool list per part, shared by operations; feeds per operation — see [0008](decisions/0008-document-tool-list.md) |
| Tree tab / tool library | WPF hosted in a COM-visible WinForms shell |
| Posts | Data-driven XML templates now, script engine later behind the same interface |
| Undo | Own command stack in Core, doubling as recompute dirty-tracking |
| Threading | Core off-thread; all SW access marshalled to the main STA thread |
| Errors | try/catch at every entry point, one `ErrorHandler` policy — see [error-handling.md](error-handling.md) |
| Audience | Internal team tool |

## Layout

Folders inside each project are created as code lands; the projects and their
references exist now.

```
G-CAM.sln
Directory.Build.props                  shared settings + $(SolidWorksApiDir)
│
├── src/
│   ├── GCam.Core/                     ★ netstandard2.0 — NO SolidWorks references. Ever.
│   │   ├── Model/                     Job, Operation, OperationState, OperationFrame,
│   │   │                              Stock, JobDocument (owns the part's tool list),
│   │   │                              ToolUsage (tools → the operations using them),
│   │   │                              WorkOffsets, GeometryRef,
│   │   │                              OperationStatus + OperationBadge (which mark a
│   │   │                              state earns in the tree, and its words),
│   │   │                              Toolpath, Move, MoveKind, ArcData
│   │   │                              (no Setup level — see decision 0004)
│   │   │   └── Heights/               HeightSetting, HeightMode, HeightContext
│   │   │                              — mode + offset, resolved against stock/model
│   │   ├── Tooling/                   Tool, Holder, CuttingData, MachineData,
│   │   │                              CutterProfile, ToolSearch, IToolLibrary,
│   │   │                              LibrarySession (open libraries + dirty state),
│   │   │                              FeedsAndSpeeds (rpm ↔ surface speed, feed ↔ chip
│   │   │                              load — both ends editable)
│   │   │   └── Import/                native XML + HSMWorks (.hsmlib) readers
│   │   ├── Rendering/                 RenderScene (named layers), RenderLayer,
│   │   │                              RenderBatch, PrimitiveKind, RenderColour,
│   │   │                              BoxMesh, ConeMesh, AxisTriad, ToolpathMesh,
│   │   │                              PreviewSelection (which of stock, origin and
│   │   │                              toolpath a selection asks for)
│   │   │                              — what to draw, never how
│   │   ├── Selection/                 MultiSelection<T> — click, Ctrl-click and
│   │   │                              Shift-click over an ordered list
│   │   ├── Geometry/
│   │   │   ├── Chaining.cs            loose curve pieces → contours, open or closed,
│   │   │   │                          each knowing which pieces it was built from
│   │   │   ├── Primitives/            Vec3, Bounds, Matrix4, Polyline
│   │   │   ├── Offset/                IContourOffsetter + Clipper2Offsetter
│   │   │   ├── Brep/                  own face/edge/loop model, SW-independent
│   │   │   ├── Faceting/              controlled-tolerance tessellation
│   │   │   └── Query/                 raycast, closest-point, containment
│   │   ├── Strategies/                StrategyId, StrategySettings, StrategyCatalog,
│   │   │                              IToolpathStrategy, GenerationContext
│   │   │   ├── Shared/                groups some strategies have and others do not —
│   │   │   │                          MultipleDepthsSettings, LeadSettings, CutDirection,
│   │   │   │                          ContourSelection (picked entity + its modifiers)
│   │   │   └── Contour2d/             + later Face/, Adaptive2d/, Drill/
│   │   │                              — see design/operations.md
│   │   ├── Generation/                GenerationQueue (explicit, cancellable; the
│   │   │                              caller owns the thread), GenerationProgress,
│   │   │                              IGenerationContextFactory,
│   │   │                              Staleness (what a change invalidates)
│   │   ├── Simulation/                ISimulator, ZMap/, Verification/
│   │   ├── Commands/                  ICommand, CommandStack, DirtyTracker
│   │   ├── Persistence/               GcamDocumentXml (the stored format, both ways),
│   │   │                              ToolpathBinary — what the bytes mean; the
│   │   │                              SolidWorks project owns getting them in and out
│   │   ├── Posting/                   CLData — machine-neutral canonical toolpath
│   │   ├── Diagnostics/               IGCamLog, IErrorPresenter, ErrorHandler
│   │   ├── Settings/                  IGCamSettings + XmlSettingsStore
│   │   ├── Units.cs / Precision.cs    shared constants — see the rule below
│   │   └── Abstractions/              interfaces the outer layers implement
│   │
│   ├── GCam.Posts/                    netstandard2.0 — consumes CLData, emits G-code
│   │   ├── Engine/                    formatting, modal state, word ordering
│   │   ├── TemplatePost.cs            ITemplatePost → XML-driven
│   │   └── definitions/               grbl.xml, haas.xml, …
│   │
│   ├── GCam.SolidWorks/               ★ ALL COM interop lives here
│   │   ├── Extraction/                SW geometry → GCam.Core, units conversion;
│   │   │                              CoordinateSystems, JobFrame, ModelExtent,
│   │   │                              ContourExtraction (edges → tessellated loops),
│   │   │                              GenerationContextFactory
│   │   │                              — later the BRep walk
│   │   ├── Rendering/
│   │   │   ├── Interop/Gl.cs          [DllImport("opengl32.dll")] — ~20 entry points
│   │   │   ├── GlState.cs             save/restore around every draw
│   │   │   ├── SceneRenderer.cs       RenderScene → GL, mm → m, cached per version
│   │   │   ├── ViewportRenderer.cs    implements Core's IViewportRenderer;
│   │   │   │                          BufferSwapNotify subscription per window
│   │   │   └── JobPreview.cs          implements Core's IJobPreview — a stock box,
│   │   │                              a coordinate system triad and a toolpath
│   │   │                              per selected row, a layer each
│   │   ├── PropertyPages/             PmpHandlerBase (all 37 callbacks, wrapped),
│   │   │                              GCamPropertyPage (build/show/tab restore),
│   │   │                              JobPropertyPage, OperationPropertyPage
│   │   ├── Persistence/               third-party storage read/write + storage events;
│   │   │                              model.xml plus one binary stream per toolpath
│   │   ├── Hosting/                   JobTreeTabHost — COM-visible WinForms shell;
│   │   │                              JobTreeTabs — one Manager Pane tab per part
│   │   ├── Threading/                 SwDispatcher — marshal to SW's STA thread
│   │   ├── Events/                    document + view event subscriptions.
│   │   │                              PartRebuildWatcher — one per part, marks its
│   │   │                              operations stale on RegenPostNotify2
│   │   └── Com/                       ComRelease helpers
│   │
│   ├── GCam.UI/                       WPF; references Core, never SolidWorks
│   │   ├── Views/                     JobTreeView, ToolLibraryWindow (+ .Editing),
│   │   │                              ToolEditorWindow, dialogs, converters
│   │   ├── Diagnostics/               ErrorDialog.xaml, WpfErrorPresenter
│   │   ├── ViewModels/                testable, INotifyPropertyChanged
│   │   ├── Controls/                  ToolProfileView — owner-drawn silhouette
│   │   └── Hosting/                   ElementHost wrappers
│   │
│   └── GCam.AddIn/                    thin: entry point + wiring only
│       ├── GCamAddin.cs               ISwAddin — ConnectToSW / DisconnectFromSW
│       ├── GCamAddin.CommandManager.cs   toolbar, menu and ribbon tab construction
│       ├── GCamAddin.Callbacks.cs     the methods those buttons resolve by name
│       ├── GCamAddin.Jobs.cs          job commands behind the tree and the buttons
│       ├── GCamAddin.Tools.cs         the library browser as a picker → the part's
│       │                              tool list; the join UI and SolidWorks cannot make
│       ├── GCamAddinRegistration.cs   COM registration (already written)
│       ├── Composition/               DI wiring, Serilog setup, AssemblyResolver
│       ├── Diagnostics/               Serilog adapter for Core's IGCamLog
│       └── Commands/                  GCamCommand — the command ids
│
├── tests/
│   ├── GCam.Core.Tests/               headless — no SOLIDWORKS, runs on any machine
│   └── GCam.Integration.Tests/        requires SOLIDWORKS
│
├── docs/
└── deploy/
    └── register.cmd                elevated regasm helper; installer comes later
```

**`GCamAddin` is one `partial` class split by concern**, not several classes. SOLIDWORKS
resolves toolbar callbacks by name against the single object registered with
`SetAddinCallbackInfo2`, so they have to be members of that one type — the split is how it
stays readable as lifetime, UI construction, callbacks and job commands accumulate. Keep
it: a new concern gets a new `GCamAddin.*.cs`, not a longer `GCamAddin.cs`.

## Dependency rules

```
        GCam.Core  ←────────────┐
            ↑                   │
    ┌───────┼───────┐           │
GCam.Posts  │   GCam.UI    GCam.SolidWorks
            │       ↑           ↑
            └───────┴─── GCam.AddIn ───┘
```

| Project | May reference |
| --- | --- |
| `GCam.Core` | nothing but the BCL and vetted algorithm libraries |
| `GCam.Posts` | Core |
| `GCam.UI` | Core |
| `GCam.SolidWorks` | Core, SolidWorks interop |
| `GCam.AddIn` | everything — it is the composition root, and the only project that knows all the others exist |

**The one rule that matters: `GCam.Core` never references SolidWorks.** Everything else is convention; this one is load-bearing. It is what lets the toolpath math be tested on a build agent with no SOLIDWORKS licence, what lets calculation run off-thread at all, and what preserves the out-of-process escape hatch. Core declares interfaces in `Abstractions/`; `GCam.SolidWorks` implements them; `GCam.AddIn` wires the two together. If you ever find yourself wanting a `using SolidWorks.Interop` inside Core, the answer is a new interface in `Abstractions/`.

This rule is **enforced by the build**, not left to discipline. `GCam.Core.csproj` carries an `EnsureCoreHasNoSolidWorksReference` target that inspects resolved references after RAR and fails with a pointer to this document. Verified 2026-09-12 by adding a SolidWorks reference and confirming the error fires.

The target is necessary because the framework targets alone do not stop it — **both tested, both disappointing**: a `ProjectReference` from netstandard2.0 to a net48 project only raises warning NU1702, and a raw `<Reference>` with a `HintPath` is accepted with no diagnostic at all. `Core` and `Posts` target `netstandard2.0` for a different reason: so they load unchanged in a .NET 8 worker process if that escape hatch is ever needed.

## Rules with teeth

**Units.** SOLIDWORKS works in metres — the help states it plainly, and it is the classic source of toolpaths wrong by 1000×. *Core works in millimetres*, because that is what tool definitions, G-code and CAM convention use. Conversion happens only at the edges: `Extraction/` for SOLIDWORKS geometry, and the library readers for files that declare inches. Nothing in between scales anything.

Every conversion factor lives in `GCam.Core.Units` (see the constants rule below). Never write a bare `25.4` or `1000`.

(Assumption: mm internally with display/post conversion for inch users. Revisit before the tool library schema is frozen.)

**Shared constants have exactly one home, named for what they mean.** A value that more than one file needs — a physical conversion, a comparison epsilon, a format version, a registry key, a well-known id — gets a named constant in a purpose-named static class, and every use refers to that.

Current homes:

| Class | Holds |
| --- | --- |
| `GCam.Core.Units` | `MillimetresPerInch`, `MillimetresPerMetre`, degree/radian helpers |
| `GCam.Core.Precision` | `Epsilon` — the floating-point comparison threshold |
| `GCam.Core.Tooling.Import.GcamXmlLibrary` | Native tool library format: root element, version, extension |

Three things that make this a rule rather than a preference:

- **It had already gone wrong twice before anyone looked.** `25.4` existed independently in `HsmLibraryReader` and `GcamXmlLibrary`; the float epsilon was parked on `ToolGeometry` and reached into from `CutterProfile`, which has nothing to do with tool geometry. Neither was noticed until a constant was questioned.
- **Duplicated physical constants eventually disagree.** Not because anyone mistypes 25.4, but because one copy gains a fix, a comment or a precision change and the other does not.
- **Names carry meaning that literals cannot.** `Units.MillimetresPerMetre` is greppable and self-explaining where `1000` is ambiguous — and that particular 1000 is the classic CAM integration bug.

**Name the class for its purpose, never `Constants` or `Globals`.** A junk-drawer class attracts unrelated values, and then everything depends on it for no reason. If a new constant does not fit an existing home, that is a signal it wants its own small, clearly-named class — `Precision` exists precisely because an epsilon is not a unit.

**Exception:** a constant used in exactly one file, meaningful only there, stays there — a private `const` next to its use is clearer than a distant shared one. Move it out when the second caller appears, not in anticipation.

**Watch the vocabulary.** `Precision.Epsilon` is deliberately not called "tolerance": in CAM that word means a *machining* tolerance, and those are passed explicitly per operation rather than kept as globals (see `CutterProfile.ChordTolerance`). A shared constant with an overloaded name is worse than a duplicated literal.

This is convention, not enforced by the build. A test scanning source for magic numbers was considered and rejected as brittle — it would flag legitimate literals like `2.0` in `radius = diameter / 2.0` and need constant suppression.

**Settings are a convenience, never a prerequisite.** User preferences live in `%LOCALAPPDATA%\G-CAM\settings.xml`, beside the logs — per-user, always writable, and trivially deleted when something goes wrong. `IGCamSettings` is declared in Core with `XmlSettingsStore` implementing it there too, because nothing about a preferences file touches SOLIDWORKS and keeping it in Core means the tests exercise it.

Nothing in the settings path throws. A missing, corrupt, or foreign file yields empty defaults and a log line; an unwritable location loses the save and logs it. Losing preferences is an annoyance — failing to load the add-in over one is not acceptable, and that is exactly the failure mode that already cost an afternoon once.

**Edits are held in memory and committed once; creating a library is not an edit.**
`LibrarySession` owns every tool library open for editing and which of them are dirty.
Changing a library's *contents* — adding, editing or deleting tools — waits for the
browser's OK, which saves them all; Cancel discards them all. That is what makes it safe
to edit one library, navigate to another, and still have both committed.

*Creating* a library is different and writes immediately. `CreateNew` and `SaveAsCopy`
put the file on disk before returning, and leave it clean. Two reasons: the user has just
chosen a path in a save dialog, which is an explicit act rather than an edit to be
weighed up; and a library existing only in memory cannot appear in the folder tree, which
is exactly where they will look for what they just made. Tools added to it afterwards are
ordinary edits and wait for the commit like any other.

Because creation writes at once, it can also fail at once — a read-only share, a path
without permission. `CreateNew` and `SaveAsCopy` throw `GCamUserException` rather than
returning a half-made library, and the browser reports it.

Two details that are easy to get wrong and are already handled: a save writes to a
temporary file and swaps, so a failure part-way through cannot leave a half-written
library where a good one was; and if one library in a batch fails, the ones that
succeeded stay saved and the failures are reported by name rather than the whole commit
being rolled back.

**Only G-CAM's own format is writable.** `ToolLibraryImporter.CanWrite` is the single
test, true only for `.gcamtools`. An imported `.hsmlib` is read-only, marked as such in
the browser, and converted by an explicit action — see
[0002](decisions/0002-imported-libraries-are-read-only.md). Nothing else should compare
file extensions to decide whether editing is allowed.

**Logic worth testing goes in Core, even when it looks like UI.** `ToolSearch` — which tools match what the user typed — lives in `Core/Tooling` rather than the browser's viewmodel. Search rules quietly stop matching what people expect, and Core is the only place a headless test can reach. The same reasoning puts tool-type display names there: the browser shows "Bull nose end mill", so search has to match that string, and having one source for it keeps the two from drifting.

The line to hold: Core owns *rules*, the viewmodel owns *presentation state* (what is selected, what is expanded, what the status strip says). If a viewmodel grows logic worth asserting, that logic belongs in Core — there is no `GCam.UI.Tests` project, and adding one is a poorer answer than moving the rule.

**No exception leaves G-CAM code.** SOLIDWORKS calls us through COM by method name; an exception thrown back across that boundary is discarded at best and destabilises the host at worst — and an exception out of `ConnectToSW` silently unloads the add-in. Every method SOLIDWORKS, WPF or the task scheduler can call gets a `try`/`catch` calling `ErrorHandler.Handle`. Interior code throws freely; only entry points catch. The full entry-point list and per-callback policy is in [error-handling.md](error-handling.md).

**Threading.** Core touches no COM, so it runs freely on background threads with `IProgress<T>` and `CancellationToken`. Any SolidWorks call goes through `SwDispatcher` back to the main STA thread. Calling SW from a worker thread appears to work and then corrupts state later.

**Persistence.** Verified in the 2025 help, and it constrains the design more than you'd expect:
- G-CAM uses `IModelDocExtension::IGet3rdPartyStorageStore` (an `IStorage`, so multiple named sub-streams) over the flat `IModelDoc2::IGet3rdPartyStorage` — versioned CAM data benefits from the structure.
- **That means the *store* notifications**, `LoadFromStorageStoreNotify` / `SaveToStorageStoreNotify`, not the stream ones whose names differ by a word. Never `FileSaveNotify` / `FileOpenNotify2`.
- **Writing is locked unless `SaveToStorageStoreNotify` has fired.** You cannot flush CAM data whenever you like; you mark the document dirty with `IModelDoc2::SetSaveFlag` and write when SolidWorks asks.
- Reading, by contrast, is safe at any time once a document is fully open — so loading need not race the notification.
- Every get must be matched by a release, *including when it returns null*, or the node stays locked for the session.
- Storage names are under 30 characters and global across add-ins; element names inside are capped near 31, which is why toolpath streams are numbered rather than named after a 36-character operation id.
- If the add-in loads mid-session, walk open documents with `EnumDocuments2` to pick up storage that was never notified.

Full detail, including the signatures and the one-subscriber-per-document rule, is in [third-party-storage.md](solidworks-api/third-party-storage.md).

**COM lifetime.** Release COM objects explicitly rather than leaving them to the GC; `DisconnectFromSW` already sets the pattern. This matters more as the extraction code starts walking thousands of faces.

**OpenGL state.** SolidWorks owns the context. Save and restore every piece of state you touch around each draw, or you will corrupt SW's own rendering in ways that look like SolidWorks bugs. `GlState` makes this mechanical — push both the server *and* the client attribute stacks, because vertex array pointers live on the second one and a pop of the first leaves SolidWorks reading through our buffer. Never write a bare `glEnable` outside a `using (new GlState())`.

**Core says what to draw; GCam.SolidWorks says how.** `Core/Rendering` describes graphics as a `RenderScene` of named layers of `RenderBatch`es — vertices in millimetres in part coordinates, a primitive kind, a colour. It contains no OpenGL, no matrices, no camera and no projection, which is what lets `BoxMesh` and everything after it be tested headlessly. `GCam.SolidWorks/Rendering` turns a scene into GL calls and nothing else.

The layer name is the coordination mechanism. A producer owns a name, re-states everything under it whenever its model changes, and never has to know what else is on screen; the job preview owns `"stock"` and `"job-origin"`, and a toolpath renderer will own its own beside them.

**Annotations set `RenderBatch.AlwaysOnTop`; objects do not.** An always-on-top batch is drawn last, with depth testing off so nothing can hide it, and with depth *writing* off as well — SOLIDWORKS renders Layer2 (active sketches, its own reference triad) after our notification, and would otherwise be depth-tested against geometry that was never depth-tested itself. The coordinate triad uses it because a job origin inside the stock is exactly the one worth seeing; the stock box does not, because it is a thing in the scene.

**Draw only from `BufferSwapNotify`.** That is the one moment SolidWorks has made its context current and set up the matrices so part coordinates land correctly — the help says so explicitly. Drawing from anywhere else means no context, or somebody else's. It also means G-CAM never computes a projection: vertices go straight out.

One notification per *window*, not per document, and a part can have two. See [opengl-overlay.md](solidworks-api/opengl-overlay.md), which also records that "Enhanced graphics performance" — widely reported to stop the notification reaching add-ins — does not, on 2025 SP3.

**Measure coordinate systems, do not decode them.** `IMathTransform::ArrayData` does not document whether its rotation is stored by row or by column, and the wrong choice is correct for every axis-aligned coordinate system and wrong only for rotated ones. `CoordinateSystems` asks SolidWorks where the origin and three unit axes land instead. Likewise `IBody2::GetBodyBox` is not a bounding box you may calculate with — its own Remarks say so — and `GetExtremePoint` along the job's axes is. Both in [coordinate-systems.md](solidworks-api/coordinate-systems.md).

**The cutter profile is INSCRIBED, so the modelled tool is slightly undersized.**
`CutterProfile` tessellates corner arcs to a chord tolerance with its vertices *on* the
arc, which means the polyline sits just inside the true cutter — by up to
`CutterProfile.ChordTolerance`. That is the *unsafe* direction for gouge checking and
collision detection: a tool modelled smaller than reality reports no gouge where there
is one. Code that must be conservative inflates by `ChordTolerance` rather than assuming
the profile is exact. A test pins the direction of the error so this stays true.

Two related facts, both learned the hard way: the tolerance bounds deviation measured
*perpendicular* to the arc, so radial error at a fixed height is larger wherever the
profile is steep; and the profile runs to the top of the tool **body** — the greater of
flute and shoulder length — not the flute length, because on a chamfer mill those differ
by 23mm. See [the HSM format notes](cam/hsm-tool-library-format.md).

**Posts consume `CLData`, never `Toolpath`.** Keep a machine-neutral canonical layer between the strategies and the post engine. It is what makes the eventual switch from XML templates to a script engine a change in one project rather than everywhere.

## Build order — first vertical slice

One 2D contour operation, all the way through, before breadth:

1. `GCam.AddIn` skeleton — DI composition root, CommandManager tab with buttons
2. `GCam.Core.Model` — Job → Operation → Toolpath (see [0004](decisions/0004-jobs-own-operations-directly.md))
3. `Extraction/` — one planar face out of SolidWorks into Core geometry
4. `Strategies/Contour2D` — offset with Clipper2, with headless tests
5. `Rendering/` — GL overlay drawing the path
6. `GCam.Posts` — template post to a G-code file
7. `Persistence/` — round-trip the job through document storage

Each step crosses a project boundary, so a wrong boundary shows up while it is still cheap to move.

## Build and tooling

How the solution is put together, and the choices that go with it. Day-to-day commands are
in the `build` skill; this is the reasoning underneath them.

- `G-CAM.sln` sits at the repo root, with projects under `src/` and `tests/`.
- Assemblies and namespaces are `GCam.*` — `G-CAM` as an assembly name forces the namespace `G_CAM`, which reads badly across seven projects. "G-CAM" remains the product name.
- All projects are SDK-style. **Verified:** `<UseWPF>true</UseWPF>` does work with `net48` under the SDK, XAML compilation included — this was flagged as an assumption and is now tested.
- `Directory.Build.props` holds shared settings and `$(SolidWorksApiDir)`, so the interop `HintPath`s are no longer the brittle `..\..\..\..\..\..` relative paths. Override it on the command line if SOLIDWORKS lives elsewhere. Every interop reference sets `EmbedInteropTypes=false`, as the SDK templates do. A consequence worth knowing before setting up a build agent: **the solution does not build on a machine without SOLIDWORKS installed**, because those `HintPath`s cannot resolve. `GCam.Core` and its tests build anywhere, which is the whole point of the Core-purity rule above.
- Test stack is xUnit. FluentAssertions is deliberately absent: v8+ moved to a paid Xceed licence in January 2025. Pin `[7.0.0]` or use the AwesomeAssertions fork if you want it.

**Registration is non-fatal.** The post-build `regasm` step uses `ContinueOnError`, so an ordinary unelevated build warns instead of failing and still produces a DLL. Run `deploy/register.cmd` from an elevated prompt to actually register. This matters more than it sounds: the old setup made every unelevated build look broken.

**Two assemblies get registered, not one.** `GCam.AddIn.dll` is the add-in SOLIDWORKS loads; `GCam.SolidWorks.dll` carries the ActiveX control that `CreateFeatureMgrControl4` activates by ProgID for the tree tab. Registering only the first gives a working toolbar and a silently missing tab — a confusing failure worth recognising.

## Known gaps and where this breaks

- **No installer.** Fine while it is yours alone, but this is an internal team tool, and `regasm` on a developer machine is not a rollout. `deploy/` exists for when that day comes; it should arrive before the second user does.
- **3-axis is baked in.** Operations carry no tool-orientation vector. Adding 3+2, let alone simultaneous 5-axis, means revisiting `Model/`, the Z-map simulator and every post — a genuine rewrite of the middle of the system, not an extension.
- **Z-map does not generalise.** It is the right call for 3-axis and the wrong one for anything with undercuts.
- **Out-of-process compute is the escape hatch**, not the plan. If toolpath calculation turns out to be runtime-bound, moving Core into a .NET 8 worker process is the sanctioned way to get a modern runtime without putting the add-in's COM registration at risk. Keeping Core pure is what preserves that option.
