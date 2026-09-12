# G-CAM architecture

The target file structure for G-CAM, and the rules that keep it intact. Decided 2026-09-12; nothing here is built yet beyond the Hello World add-in.

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
| Audience | Internal team tool |

## Layout

```
G-CAM.sln                              ← move to repo root; drop the current double nesting
│
├── src/
│   ├── GCam.Core/                     ★ NO SolidWorks references. Ever.
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
│   │   └── Abstractions/              interfaces the outer layers implement
│   │
│   ├── GCam.Posts/                    consumes CLData, emits G-code
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
│   │   ├── ViewModels/                testable, INotifyPropertyChanged
│   │   ├── Controls/
│   │   └── Hosting/                   ElementHost wrappers
│   │
│   └── GCam.AddIn/                    thin: entry point + wiring only
│       ├── GCamAddin.cs               ISwAddin — ConnectToSW / DisconnectFromSW
│       ├── GCamAddinRegistration.cs   COM registration (already written)
│       ├── Composition/               DI container: binds Core ↔ adapters
│       └── Commands/                  CommandManager tabs, buttons, callbacks
│
├── tests/
│   ├── GCam.Core.Tests/               headless — no SOLIDWORKS, runs on any machine
│   └── GCam.Integration.Tests/        requires SOLIDWORKS
│
├── docs/
└── deploy/                            empty until the team rollout needs an installer
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

**The one rule that matters: `GCam.Core` never references SolidWorks.** Everything else is convention; this one is load-bearing. It is what lets the toolpath math be tested on a build agent with no SOLIDWORKS licence, and it is what lets calculation run off-thread at all. Core declares interfaces in `Abstractions/`; `GCam.SolidWorks` implements them; `GCam.AddIn` wires the two together. If you ever find yourself wanting a `using SolidWorks.Interop` inside Core, the answer is a new interface in `Abstractions/`.

## Rules with teeth

**Units.** SOLIDWORKS works in metres — the help states it plainly, and it is the classic source of toolpaths wrong by 1000×. *Core works in millimetres*, because that is what tool definitions, G-code and CAM convention use. Conversion happens in exactly one place: `Extraction/`. Nothing downstream of extraction should ever multiply by 1000. (Assumption: mm internally with display/post conversion for inch users. Revisit before the tool library schema is frozen.)

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

## Housekeeping worth doing first

**Flatten the nesting.** The solution currently sits at `G-CAM/G-CAM.sln` with the project at `G-CAM/G-CAM/`. Put the `.sln` at the repo root with projects under `src/`.

**Namespaces.** `G-CAM` as an assembly name yields the namespace `G_CAM`, which gets ugly fast across six projects. Use `GCam.Core`, `GCam.SolidWorks` and so on for assemblies and namespaces; keep "G-CAM" as the product name users see.

**SDK-style project files.** Old-style `.csproj` means no `PackageReference`, which you will want for Clipper2 and the test stack. SDK-style projects support `net48` and are dramatically shorter. *Assumed:* `<UseWPF>true</UseWPF>` works with `net48` under SDK-style — verify when setting up `GCam.UI` rather than trusting it.

**Test stack.** xUnit or NUnit. Avoid FluentAssertions v8+, which moved to a paid Xceed licence in January 2025 — pin `[7.0.0]` or use the AwesomeAssertions fork. Clipper2 is Boost-licensed and targets netstandard2.0, so it is fine on net48.

## Known gaps and where this breaks

- **No installer.** Fine while it is yours alone, but this is an internal team tool, and `regasm` on a developer machine is not a rollout. `deploy/` exists for when that day comes; it should arrive before the second user does.
- **3-axis is baked in.** Operations carry no tool-orientation vector. Adding 3+2, let alone simultaneous 5-axis, means revisiting `Model/`, the Z-map simulator and every post — a genuine rewrite of the middle of the system, not an extension.
- **Z-map does not generalise.** It is the right call for 3-axis and the wrong one for anything with undercuts.
- **Out-of-process compute is the escape hatch**, not the plan. If toolpath calculation turns out to be runtime-bound, moving Core into a .NET 8 worker process is the sanctioned way to get a modern runtime without putting the add-in's COM registration at risk. Keeping Core pure is what preserves that option.
