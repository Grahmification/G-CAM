# G-CAM architecture

The project structure for G-CAM and the rules that keep it intact. Decided and scaffolded 2026-09-12: the solution and all seven projects exist and build; no product code yet beyond the original add-in entry point.

## Decisions this rests on

| Area | Choice |
| --- | --- |
| Runtime | .NET Framework 4.8 — see [0001](decisions/0001-target-net-framework-48.md) |
| Layout | Core / Posts / SolidWorks / UI / AddIn + tests; Core has zero SolidWorks references |
| Geometry | Extract SW geometry once into Core's own kernel, then compute in managed memory |
| Machines | 3-axis mill only |
| Toolpath display | OpenGL overlay on `BufferSwapNotify`, via hand-rolled P/Invoke (no OpenTK) |
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
│   │   ├── Model/                     Job, Setup, Operation, Toolpath, Move, Stock
│   │   ├── Tooling/                   Tool, Holder, CuttingData, IToolLibrary
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
│   │   └── Abstractions/              interfaces the outer layers implement
│   │
│   ├── GCam.Posts/                    netstandard2.0 — consumes CLData, emits G-code
│   │   ├── Engine/                    formatting, modal state, word ordering
│   │   ├── TemplatePost.cs            ITemplatePost → XML-driven
│   │   └── definitions/               grbl.xml, haas.xml, …
│   │
│   ├── GCam.SolidWorks/               ★ ALL COM interop lives here
│   │   ├── Extraction/                SW BRep → GCam.Core.Geometry, units conversion
│   │   ├── Rendering/
│   │   │   ├── Interop/               [DllImport("opengl32.dll")] — ~20 entry points
│   │   │   ├── GlState.cs             save/restore around every draw
│   │   │   ├── ToolpathRenderer.cs    implements Core's IToolpathRenderer
│   │   │   └── ViewHooks.cs           BufferSwapNotify subscription
│   │   ├── PropertyPages/             PmpBuilder → IPropertyManagerPage2, per-op pages
│   │   ├── Persistence/               third-party storage read/write + storage events
│   │   ├── Hosting/                   COM-visible WinForms shell for the FeatureMgr tab
│   │   ├── Threading/                 SwDispatcher — marshal to SW's STA thread
│   │   ├── Events/                    document + view event subscriptions
│   │   └── Com/                       ComRelease helpers
│   │
│   ├── GCam.UI/                       WPF; references Core, never SolidWorks
│   │   ├── Views/                     JobTreeView.xaml, ToolLibraryWindow.xaml
│   │   ├── Diagnostics/               ErrorDialog.xaml, WpfErrorPresenter
│   │   ├── ViewModels/                testable, INotifyPropertyChanged
│   │   ├── Controls/
│   │   └── Hosting/                   ElementHost wrappers
│   │
│   └── GCam.AddIn/                    thin: entry point + wiring only
│       ├── GCamAddin.cs               ISwAddin — ConnectToSW / DisconnectFromSW
│       ├── GCamAddinRegistration.cs   COM registration (already written)
│       ├── Composition/               DI wiring + Serilog setup
│       └── Commands/                  CommandManager tabs, buttons, callbacks
│
├── tests/
│   ├── GCam.Core.Tests/               headless — no SOLIDWORKS, runs on any machine
│   └── GCam.Integration.Tests/        requires SOLIDWORKS
│
├── docs/
└── deploy/
    └── register.cmd                elevated regasm helper; installer comes later
```

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

**Units.** SOLIDWORKS works in metres — the help states it plainly, and it is the classic source of toolpaths wrong by 1000×. *Core works in millimetres*, because that is what tool definitions, G-code and CAM convention use. Conversion happens in exactly one place: `Extraction/`. Nothing downstream of extraction should ever multiply by 1000. (Assumption: mm internally with display/post conversion for inch users. Revisit before the tool library schema is frozen.)

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

**OpenGL state.** SolidWorks owns the context. Save and restore every piece of state you touch around each draw, or you will corrupt SW's own rendering in ways that look like SolidWorks bugs.

**Posts consume `CLData`, never `Toolpath`.** Keep a machine-neutral canonical layer between the strategies and the post engine. It is what makes the eventual switch from XML templates to a script engine a change in one project rather than everywhere.

## Build order — first vertical slice

One 2D contour operation, all the way through, before breadth:

1. `GCam.AddIn` skeleton — DI composition root, CommandManager tab with buttons
2. `GCam.Core.Model` — Job → Setup → Operation → Toolpath
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
- `Directory.Build.props` holds shared settings and `$(SolidWorksApiDir)`, so the interop `HintPath`s are no longer the brittle `..\..\..\..\..\..` relative paths. Override it on the command line if SOLIDWORKS lives elsewhere.
- Test stack is xUnit. FluentAssertions is deliberately absent: v8+ moved to a paid Xceed licence in January 2025. Pin `[7.0.0]` or use the AwesomeAssertions fork if you want it.

**Registration is now non-fatal.** The post-build `regasm` step uses `ContinueOnError`, so an ordinary unelevated build warns instead of failing and still produces a DLL. Run `deploy/register.cmd` from an elevated prompt to actually register. This matters more than it sounds: the old setup made every unelevated build look broken.

**Two assemblies get registered, not one.** `GCam.AddIn.dll` is the add-in SOLIDWORKS loads; `GCam.SolidWorks.dll` carries the ActiveX control that `CreateFeatureMgrControl4` activates by ProgID for the tree tab. Registering only the first gives a working toolbar and a silently missing tab — a confusing failure worth recognising.

## GUI shells — scaffolded 2026-09-12

The three custom UI surfaces exist and are wired, with no behaviour behind them.

| Surface | Built by | Notes |
| --- | --- | --- |
| CommandManager tab, toolbar and menu | `GCamAddin.CommandManager.cs` | Four buttons: New Job, Tool Library, Post Process, Simulate |
| FeatureManager tree tab | `GCamAddin.CreateJobTreeTab` → `JobTreeTabHost` → `JobTreeView` | ActiveX → WinForms → ElementHost → WPF |
| Tool library window | `ToolLibraryDialog.Show` → `ToolLibraryWindow` | Modal, parented to the SW frame |

Only the Tool Library button does anything — it opens its (empty) window, so the hosting chain can be verified. The rest are deliberate no-ops.

**Icons.** `ICommandGroup.IconList` wants a *strip* per size containing every button's icon side by side; `MainIconList` wants a single icon per size. Both need files at 20/32/40/64/96/128 px. Placeholders are generated by `tools/make-placeholder-icons.py` — the button order there must match the `GCamCommand` enum, since a command's value is its index into the strip. The files are copied next to the assembly because SOLIDWORKS reads them from disk by path, not as embedded resources.

**Callback strings are resolved by name at runtime**, so a typo fails silently rather than at compile time. `AddCommandItem2` is passed `nameof(OnCommand)` to keep them honest, and the callbacks must stay `public` on the add-in class registered via `SetAddinCallbackInfo2`.

## Known gaps and where this breaks

- **No installer.** Fine while it is yours alone, but this is an internal team tool, and `regasm` on a developer machine is not a rollout. `deploy/` exists for when that day comes; it should arrive before the second user does.
- **3-axis is baked in.** Operations carry no tool-orientation vector. Adding 3+2, let alone simultaneous 5-axis, means revisiting `Model/`, the Z-map simulator and every post — a genuine rewrite of the middle of the system, not an extension.
- **Z-map does not generalise.** It is the right call for 3-axis and the wrong one for anything with undercuts.
- **Out-of-process compute is the escape hatch**, not the plan. If toolpath calculation turns out to be runtime-bound, moving Core into a .NET 8 worker process is the sanctioned way to get a modern runtime without putting the add-in's COM registration at risk. Keeping Core pure is what preserves that option.
