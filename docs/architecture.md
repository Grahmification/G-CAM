# G-CAM architecture

The project structure for G-CAM and the rules that keep it intact.

**The tree below is the target layout, not a description of the repository.** Most of it does not exist yet. What is built as of 2026-09-12:

| Area | State |
| --- | --- |
| Solution, seven projects, build and test | Done |
| `Core/Diagnostics` — logging, error policy, user exceptions | Done |
| `Core/Settings` — XML settings store | Done |
| `Core/Tooling` — tools, holders, cutter profiles, libraries, HSM import, edit sessions | Done |
| `UI/Views` — error dialog, tool library browser and editor, profile preview | Done |
| `AddIn` — CommandManager, COM registration | Done |
| `SolidWorks/Hosting` — Manager Pane tab, one per open part, kept in sync by document events | Done |
| `Core/Model` — Job, Operation, Stock, JobDocument | Done, minus persistence |
| `Core/Geometry/Primitives` — Vec3, Bounds, Matrix4 | Started — only what stock and rendering need |
| `Core/Rendering` — scene, layers, batches, colour, BoxMesh, ConeMesh, AxisTriad | Done for what exists to draw |
| `UI` — job tree: rename in place, context menu, double-click and Enter to edit | Done |
| `SolidWorks/PropertyPages` — handler base, shared page base, Job page | Done; Operation page is still a shell |
| `SolidWorks/Selection` — selection boxes to body and coordinate-system names | Done |
| `SolidWorks/Rendering` — GL interop, state guard, scene renderer, view hooks, job preview (stock box + origin triad) | Done |
| `SolidWorks/Extraction` — coordinate system transforms, model extent | Started — bounding boxes only, no BRep |
| `Core/Strategies`, `Simulation`, `Commands`, `Posting` | Not started |
| `Core/Geometry` beyond the primitives | Not started |
| `SolidWorks/Persistence` | Not started |
| `Posts` | Empty project |

No toolpath has been computed and nothing has been posted. The vertical slice at the end of this document is still the plan — but step 5 of it, the OpenGL overlay, now exists and is drawing the stock box, so the toolpath work inherits a renderer rather than starting one.

## Decisions this rests on

| Area | Choice |
| --- | --- |
| Runtime | .NET Framework 4.8 — see [0001](decisions/0001-target-net-framework-48.md) |
| Layout | Core / Posts / SolidWorks / UI / AddIn + tests; Core has zero SolidWorks references |
| Geometry | Extract SW geometry once into Core's own kernel, then compute in managed memory |
| Machines | 3-axis mill only |
| Toolpath display | OpenGL overlay on `BufferSwapNotify`, via hand-rolled P/Invoke (no OpenTK); fixed-function vertex arrays — see [0005](decisions/0005-opengl-overlay-with-vertex-arrays.md) |
| Simulation | Z-map heightfield material removal |
| Persistence | Inside the SOLIDWORKS document, third-party storage |
| Operation editing | SOLIDWORKS-native PropertyManager pages |
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
│   │   ├── Model/                     Job, Operation, Stock, JobDocument,
│   │   │                              WorkOffsets — later Toolpath, Move
│   │   │                              (no Setup level — see decision 0004)
│   │   ├── Tooling/                   Tool, Holder, CuttingData, MachineData,
│   │   │                              CutterProfile, ToolSearch, IToolLibrary,
│   │   │                              LibrarySession (open libraries + dirty state)
│   │   │   └── Import/                native XML + HSMWorks (.hsmlib) readers
│   │   ├── Rendering/                 RenderScene (named layers), RenderLayer,
│   │   │                              RenderBatch, PrimitiveKind, RenderColour,
│   │   │                              BoxMesh, ConeMesh, AxisTriad
│   │   │                              — what to draw, never how
│   │   ├── Geometry/
│   │   │   ├── Primitives/            Vec3, Plane, Bounds, Matrix4, Polyline
│   │   │   ├── Brep/                  own face/edge/loop model, SW-independent
│   │   │   ├── Faceting/              controlled-tolerance tessellation
│   │   │   ├── Offset/                2D offsetting (Clipper2 behind an interface)
│   │   │   └── Query/                 raycast, closest-point, containment
│   │   ├── Strategies/                IToolpathStrategy + Contour2D, Pocket2D, Drill…
│   │   ├── Simulation/                ISimulator, ZMap/, Verification/
│   │   ├── Commands/                  ICommand, CommandStack, DirtyTracker
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
│   │   │                              CoordinateSystems, JobFrame, ModelExtent
│   │   │                              — later the BRep walk
│   │   ├── Rendering/
│   │   │   ├── Interop/Gl.cs          [DllImport("opengl32.dll")] — ~20 entry points
│   │   │   ├── GlState.cs             save/restore around every draw
│   │   │   ├── SceneRenderer.cs       RenderScene → GL, mm → m, cached per version
│   │   │   ├── ViewportRenderer.cs    implements Core's IViewportRenderer;
│   │   │   │                          BufferSwapNotify subscription per window
│   │   │   └── JobPreview.cs          implements Core's IJobPreview — stock box
│   │   │                              and coordinate system triad
│   │   ├── PropertyPages/             PmpHandlerBase (all 37 callbacks, wrapped),
│   │   │                              GCamPropertyPage (build/show/tab restore),
│   │   │                              JobPropertyPage, OperationPropertyPage
│   │   ├── Persistence/               third-party storage read/write + storage events
│   │   ├── Hosting/                   JobTreeTabHost — COM-visible WinForms shell;
│   │   │                              JobTreeTabs — one Manager Pane tab per part
│   │   ├── Threading/                 SwDispatcher — marshal to SW's STA thread
│   │   ├── Events/                    document + view event subscriptions
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
- Read and write only in reaction to `LoadFromStorageNotify` / `SaveToStorageNotify` — *not* `FileSaveNotify` / `FileOpenNotify2`.
- **Writing is locked unless `SaveToStorageNotify` has fired.** You cannot flush CAM data whenever you like; you mark the document dirty with `IModelDoc2::SetSaveFlag` and write when SolidWorks asks.
- Every `IGet3rdPartyStorage` must be matched by `IRelease3rdPartyStorage`, *including when it returns null*, or the node stays locked for the session.
- Stream names must be under 30 characters and globally unique across add-ins.
- If the add-in loads mid-session, walk open documents with `EnumDocuments2` to pick up storage that was never notified.

Use `IModelDocExtension::IGet3rdPartyStorageStore` (an `IStorage`, so multiple named sub-streams) over the flat `IModelDoc2::IGet3rdPartyStorage` stream — versioned CAM data benefits from the structure.

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

## Scaffolding — done 2026-09-12

The solution is set up and all seven projects build. No product code yet beyond the original add-in entry point.

- Solution flattened: `G-CAM.sln` at the repo root, projects under `src/` and `tests/`. The old `G-CAM/G-CAM/` nesting is gone.
- Renamed to `GCam.*` for assemblies and namespaces — `G-CAM` as an assembly name forces the namespace `G_CAM`, which reads badly across seven projects. "G-CAM" remains the product name.
- All projects are SDK-style. **Verified:** `<UseWPF>true</UseWPF>` does work with `net48` under the SDK, XAML compilation included — this was flagged as an assumption and is now tested.
- `Directory.Build.props` holds shared settings and `$(SolidWorksApiDir)`, so the interop `HintPath`s are no longer the brittle `..\..\..\..\..\..` relative paths. Override it on the command line if SOLIDWORKS lives elsewhere. Every interop reference sets `EmbedInteropTypes=false`, as the SDK templates do. A consequence worth knowing before setting up a build agent: **the solution does not build on a machine without SOLIDWORKS installed**, because those `HintPath`s cannot resolve. `GCam.Core` and its tests build anywhere, which is the whole point of the Core-purity rule above.
- Test stack is xUnit. FluentAssertions is deliberately absent: v8+ moved to a paid Xceed licence in January 2025. Pin `[7.0.0]` or use the AwesomeAssertions fork if you want it.

**Registration is now non-fatal.** The post-build `regasm` step uses `ContinueOnError`, so an ordinary unelevated build warns instead of failing and still produces a DLL. Run `deploy/register.cmd` from an elevated prompt to actually register. This matters more than it sounds: the old setup made every unelevated build look broken.

**Two assemblies get registered, not one.** `GCam.AddIn.dll` is the add-in SOLIDWORKS loads; `GCam.SolidWorks.dll` carries the ActiveX control that `CreateFeatureMgrControl4` activates by ProgID for the tree tab. Registering only the first gives a working toolbar and a silently missing tab — a confusing failure worth recognising.

## GUI shells — scaffolded 2026-09-12

The three custom UI surfaces exist and are wired, with no behaviour behind them.

| Surface | Built by | Notes |
| --- | --- | --- |
| CommandManager tab, toolbar and menu | `GCamAddin.CommandManager.cs` | Five buttons: New Job, New Operation, Tool Library, Post Process, Simulate |
| Manager Pane tab | `JobTreeTabs` → `JobTreeTabHost` → `JobTreeView` | ActiveX → WinForms → ElementHost → WPF; selecting it brings the G-CAM ribbon tab forward |
| Job and Operation PropertyManager pages | `GCamPropertyPage` → `JobPropertyPage` / `OperationPropertyPage` | SOLIDWORKS-native, built by the API rather than WPF. The Job page is real; the Operation page is still a shell |
| Tool library window | `ToolLibraryDialog.Show` → `ToolLibraryWindow` | Modal, parented to the SW frame |

Post Process and Simulate are deliberate no-ops. The rest open their empty surfaces so the hosting chain can be verified.

**Opening the pages from the toolbar is scaffolding.** The real trigger is selecting a node in the G-CAM tab, which needs a job model to select from; New Job and New Operation stand in until there is one.

**The Manager Pane tab is where jobs will live** — the icon strip beside the
FeatureManager design tree and the PropertyManager, the same place HSMWorks puts its CAM
tree. A tab belongs to a *document*, not to the application, so `JobTreeTabs` subscribes
to `ActiveModelDocChangeNotify` and `FileCloseNotify` and re-syncs the set of tabs
against the set of open parts on every notification; `EnumDocuments2` catches up with
documents that were already open when the add-in connected. One call at connect time is
the obvious implementation and produces no tab at all, because add-ins connect before any
document exists — see
[manager-pane-tabs.md](solidworks-api/manager-pane-tabs.md), which also covers which of
these COM objects may be released and which must not.

`Events/` does not exist yet; `JobTreeTabs` subscribes directly. Factor the subscriptions
out when persistence becomes a second subscriber, not before.

**Property pages are rebuilt for every show, not cached.** Reusing one means reshaping it
before each show, and reshaping means `IPropertyManagerPageControl.Visible`, which kills
SOLIDWORKS after a handful of uses. Building is a dozen API calls; do not "optimise" it
back. The job tree's context menu is WinForms for historical reasons only — it was swapped
during that investigation and WPF turned out to be innocent.

**Editing happens on a PropertyManager page, which cannot live inside the G-CAM tab.**
`CreatePropertyManagerPage` takes no parent and no pane — SOLIDWORKS always renders a
page on the PropertyManager tab. So editing is a round trip:
`G-CAM tab → PropertyManager tab → OK/Cancel → G-CAM tab`. Only the last hop is ours;
`GCamPropertyPage` makes it by recording `ActiveFeatureManagerTabIndex` before showing
the page and setting it back after it closes. Details and the alternatives that were
weighed are in
[property-manager-pages.md](solidworks-api/property-manager-pages.md).

## Jobs

A **job** is what HSMWorks calls a Setup: it owns the model selection, the stock, the
coordinate system and the work offset, and operations sit directly inside it. There is no
Setup level — see [0004](decisions/0004-jobs-own-operations-directly.md).

```
GCam.Core.Model
  JobDocument      the jobs of one part, and which is the default
    Job            model bodies, stock, coordinate system, work offset
      Operation    placeholder until the operation work
  Stock            three modes, computing a box from the model extent
  WorkOffsets      G54–G59
```

**`JobDocument` owns the rules, not the viewmodel.** Unique names, what the default is,
where the default goes when the job holding it is deleted, what a duplicate is called —
all of it is in Core, where headless tests reach it. The same reasoning that put
`ToolSearch` there. `JobTreeViewModel` turns that model into nodes and holds selection
and edit state, and nothing else.

**Three things meet at the job tree, and none of them can see the other two.** The tree
is WPF in `GCam.UI`, which never references SOLIDWORKS. The property pages are in
`GCam.SolidWorks`, which does not know the tree exists. So the tree states intent through
`Core/Abstractions/IJobEditor`, and `GCamAddin` — the only project that knows everything
— implements it.

**Getting the viewmodel into the tab is not constructor injection.** SOLIDWORKS activates
`JobTreeTabHost` through COM, so it has a parameterless constructor and no dependencies.
`JobTreeTabs` recovers the instance with `IFeatMgrView::GetControl` straight after
creating the tab and calls `Bind` on the view inside it. One `JobDocument` and one
viewmodel per open part, held together in `JobTreeTabs`.

**Selecting a job in the tree is what shows it.** Selection is the one signal that means
"this is the job I am looking at" — it covers clicking, arrowing through the tree, and the
reselection after a refresh, without any of them knowing a preview exists. An operation
node stands in for its job here as it does for the context menu, so drilling into a job
does not make its stock disappear.

What appears is the stock as a translucent yellow box and the job's coordinate system as a
red/green/blue triad at its origin — **two scene layers, not one**, so a stock box that
cannot be computed still leaves the origin on screen. That is the half a user is more
likely to be checking when the stock is wrong.

The Job property page previews its *clone* as it is edited, so both follow what is being
typed and Cancel leaves nothing behind. The page is built once for the session and finds
the preview for whichever part is in front, which is why it takes a `Func<IJobPreview>`
rather than one instance.

Three parts again, none of which can see the other two: `Core/Abstractions/IJobPreview`
is what the tree and the page both state intent through, and `JobPreview` in
GCam.SolidWorks is what answers it. The same arrangement as `IJobEditor`. Unlike
`IJobEditor` it is implemented in GCam.SolidWorks rather than in the add-in, because
everything it needs — the document, its bodies, its coordinate systems, its windows — is
COM, and `JobTreeTabs` already holds one per open part.

**The triad is sized proportionally, not screen-constant.** Its arms are 30% of the
largest dimension of the stock — or of the model, when the stock is not usable yet, since
a job with no stock set up is exactly when someone is checking where the origin is. That
keeps it legible on a 20mm part and on a two-metre one with nothing to configure, and it
costs nothing per frame because it is rebuilt only when the job changes.

The alternative, HSMWorks' constant apparent size, was considered and rejected *for now*:
it needs `IModelView.Scale2` and `FrameHeight` read per view, the geometry rebuilt on
every `ViewChangeNotify` — continuously, while rotating or zooming — and, because two
windows on one part can sit at different zooms, a scene per *view* rather than per
document. That last part is the real cost: it would change the shape of the renderer, not
just add a subscription. Revisit if the proportional triad turns out to be annoying in
practice.

**Rendering rides on the G-CAM tab's lifetime.** `JobTreeTabs.DocumentTab` owns the
document's `ViewportRenderer` and `JobPreview` alongside its viewmodel, and disposes
them in `Forget` *before* the tab's view goes — unsubscribing from a window needs the
window still to be there. The two cover exactly the same set of documents: a part with a
G-CAM tab is a part that can have jobs, and a job is the only thing there is to draw. This
does mean `JobTreeTabs` now has a second responsibility. When a third subscriber to the
document notifications appears — persistence, most likely — that is the moment to factor
`Events/` out of it.

**Jobs are not persisted yet.** They live for as long as the document is open. That is
what lets a job name its bodies and coordinate system as plain strings; when persistence
lands, those become persistent reference ids from `IModelDocExtension::GetPersistReference3`,
because renaming a body must not silently change what a proven job cuts. The comment at
`Job.ModelBodyNames` says so at the point it matters.

**Icons.** `ICommandGroup.IconList` wants a *strip* per size containing every button's icon side by side; `MainIconList` wants a single icon per size. Both need files at 20/32/40/64/96/128 px. Placeholders are generated by `tools/make-placeholder-icons.py` — the button order there must match the `GCamCommand` enum, since a command's value is its index into the strip. The files are copied next to the assembly because SOLIDWORKS reads them from disk by path, not as embedded resources.

**Callback strings are resolved by name at runtime**, so a typo fails silently rather than at compile time. `AddCommandItem2` is passed `nameof(OnCommand)` to keep them honest, and the callbacks must stay `public` on the add-in class registered via `SetAddinCallbackInfo2`.

## Known gaps and where this breaks

- **No installer.** Fine while it is yours alone, but this is an internal team tool, and `regasm` on a developer machine is not a rollout. `deploy/` exists for when that day comes; it should arrive before the second user does.
- **3-axis is baked in.** Operations carry no tool-orientation vector. Adding 3+2, let alone simultaneous 5-axis, means revisiting `Model/`, the Z-map simulator and every post — a genuine rewrite of the middle of the system, not an extension.
- **Z-map does not generalise.** It is the right call for 3-axis and the wrong one for anything with undercuts.
- **Out-of-process compute is the escape hatch**, not the plan. If toolpath calculation turns out to be runtime-bound, moving Core into a .NET 8 worker process is the sanctioned way to get a modern runtime without putting the add-in's COM registration at risk. Keeping Core pure is what preserves that option.
