# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

G-CAM is a SOLIDWORKS 2025 add-in (C#, .NET Framework 4.8) that generates CAM toolpaths, in the spirit of HSMWorks or Fusion 360's CAM workspace. Internal team tool, 3-axis milling only.

Built so far: the add-in loads with a CommandManager tab and a FeatureManager tree tab; a tool library model with HSMWorks import; a tool library browser with create/edit/delete and a tabbed tool editor; and logging/error handling. **No toolpath has been computed and nothing has been posted** — the geometry kernel, strategies, simulation and posts do not exist yet. `docs/architecture.md` has the full status table and the planned layout.

## Layout

Solution at the repo root, seven projects:

```
src/GCam.Core          netstandard2.0 — domain, geometry, tooling. NO SolidWorks refs.
src/GCam.Posts         netstandard2.0 — CLData → G-code (empty)
src/GCam.SolidWorks    net48 — all COM interop
src/GCam.UI            net48 — WPF views and viewmodels
src/GCam.AddIn         net48 — ISwAddin entry point + composition root
tests/GCam.Core.Tests           headless, no SOLIDWORKS needed
tests/GCam.Integration.Tests    needs SOLIDWORKS (empty)
```

`GCamAddin` is a `partial` class split by concern — `.cs` (lifetime), `.CommandManager.cs` (toolbar/tab construction), `.Callbacks.cs` (toolbar callbacks), `GCamAddinRegistration.cs` (COM registration). Keep that split.

## Build, test, register

```bash
python tools/build.py          # build + test, with the noise filtered
```

Use that rather than `dotnet build` directly — the `build` skill explains why. Raw
output for this project misleads twice over: SOLIDWORKS file locks look like build
failures, and the unelevated regasm step prints warnings containing the word "error",
so grepping for "error" reports failures on a clean build. Both have already caused
wrong conclusions here.

Underneath it is `dotnet build G-CAM.sln` and
`dotnet test tests/GCam.Core.Tests/GCam.Core.Tests.csproj` (123 tests, headless).

**Close SOLIDWORKS before building** if you intend to load the add-in afterwards — it
holds the output DLLs open, so the code compiles but the add-in folder keeps the
previous build.

Registration writes to HKLM and needs elevation. The post-build `regasm` step uses `ContinueOnError`, so an unelevated build *warns* and still produces DLLs. To actually register, run `deploy/register.cmd` from an elevated prompt — or run Visual Studio as administrator and the post-build step keeps registration in sync automatically.

**Two assemblies get registered**, not one: `GCam.AddIn.dll` (the add-in) and `GCam.SolidWorks.dll` (the ActiveX control hosting the FeatureManager tab). Registering only the first gives a working toolbar and a silently missing tab.

Re-register when the assembly version, name, output path (including Debug↔Release), or COM GUID changes — not for ordinary code changes.

F5 launches SOLIDWORKS with the debugger attached; the launch target is in `GCam.AddIn.csproj`, not a gitignored `.user` file.

## When the add-in will not load

SOLIDWORKS reports nothing — the checkbox just un-ticks. Run `tools/diagnose-addin.ps1`; it checks registration, flags stale CLSID entries, and reproduces the activation outside SOLIDWORKS. Start-up failures also land in `%LOCALAPPDATA%\G-CAM\logs\startup-failure.log`. See `docs/solidworks-api/addin-wont-load.md`.

## API reference

Target is **SOLIDWORKS 2025 SP3**. The `solidworks-api` skill reads the API help offline from the local CHM files — use it to check any signature, enum value, or Remarks before writing a call, rather than guessing or fetching help.solidworks.com.

## Architecture rules

`docs/architecture.md` is authoritative. The ones that bite most often:

**`GCam.Core` never references SolidWorks.** Enforced by an MSBuild target in `GCam.Core.csproj`, not left to discipline. It keeps the toolpath math testable without a licence, lets calculation run off the STA thread, and preserves the out-of-process escape hatch. Core declares interfaces; `GCam.SolidWorks` implements them; `GCam.AddIn` wires them together.

**No exception leaves G-CAM code.** Every method SOLIDWORKS, WPF or the task scheduler can call wraps its body in try/catch and calls `ErrorHandler.Handle`. Interior code throws freely. An exception escaping `ConnectToSW` unloads the add-in silently. See `docs/error-handling.md` for the entry-point list.

**Units: Core works in millimetres**, SOLIDWORKS in metres. Convert only at the edges. Conversion factors live in `GCam.Core.Units`; never write a bare `25.4` or `1000`.

**Shared constants have one home**, named for their purpose — `GCam.Core.Units`, `GCam.Core.Precision.Epsilon`. Never a `Constants` junk drawer. A value used in one file stays private until a second caller appears.

**Logic worth testing goes in Core, even when it looks like UI** — `ToolSearch` is in `Core/Tooling`, not the browser viewmodel. Core owns rules; viewmodels own presentation state. There is no `GCam.UI.Tests` project, and moving the rule beats adding one.

**Settings** live in `%LOCALAPPDATA%\G-CAM\settings.xml`, beside the logs, via `IGCamSettings`/`XmlSettingsStore` in Core. Nothing on that path throws — a corrupt or unwritable file yields defaults and a log line, never a failed load.

**Tool library edits are held in memory until committed, but creating a library is not an edit.** `LibrarySession` owns the open libraries and their dirty state; adding, editing or deleting tools waits for the browser's OK, and Cancel discards it. `CreateNew` and `SaveAsCopy` write immediately and leave the library clean — the user chose a path, and it has to appear in the folder tree. Only `.gcamtools` is writable — ask `ToolLibraryImporter.CanWrite`, never compare extensions yourself. Imported `.hsmlib` files are read-only by decision, not by omission ([0002](docs/decisions/0002-imported-libraries-are-read-only.md)).

**NuGet packages need no extra work, but only because of `AssemblyResolver`** in `GCam.AddIn/Composition`. An add-in gets no app.config, so binding redirects do not exist and version unification breaks at runtime. See `docs/solidworks-api/addin-dependencies.md` before debugging any "could not load file or assembly".

## Keeping the docs true

**When you change the code, update the docs that describe it — in the same piece of work, not later.** Route by what changed:

| What changed | What to update |
| --- | --- |
| A project, folder, dependency rule, or anything in the layout | `docs/architecture.md` — the tree *and* the status table at the top |
| A choice with real alternatives, that you would otherwise re-argue in six months | A new ADR in `docs/decisions/`, indexed in its README |
| SOLIDWORKS API behaviour learned by experiment | `docs/solidworks-api/`, tagged Verified / From docs / Assumed |
| CAM domain or file-format knowledge | `docs/cam/` |
| A rule that governs every edit, or a fact needed to get started | Here as well — sparingly, since this file loads every session |

**A stale doc is worse than a missing one**, because it is believed. This has already gone wrong twice: `CLAUDE.md` described the add-in as "Hello World" and pointed at a solution path that no longer existed, long after neither was true; and the architecture tree read as a description of the repository when most of it did not exist. Both actively misled. The status table and any test counts are the first things to rot — check them whenever you touch this file.

If a change makes a documented statement false, fixing that statement is part of the change, not follow-up work.

## Knowledge base

`docs/` is the project's working notebook — `docs/solidworks-api/` for API behaviour, `docs/cam/` for CAM domain knowledge, `docs/decisions/` for architecture decision records. See `docs/README.md` for conventions.

Check it before researching a SOLIDWORKS API question; much of what is there was learned by experiment and is not in the official help. When you work something out that took real effort — an API quirk, a units convention, a snippet that finally worked — write it down there, tagged **Verified** (say on which SOLIDWORKS version), **From docs**, or **Assumed**.

## SOLIDWORKS API constraints

- Interop assemblies are referenced via `$(SolidWorksApiDir)` from `Directory.Build.props`, with `EmbedInteropTypes=false`. A machine without SOLIDWORKS cannot build.
- Release COM objects with `Marshal.ReleaseComObject` rather than relying on the GC.
- Use `IFrame.GetHWndx64`, not `GetHWnd` — SOLIDWORKS is 64-bit and the 32-bit variant truncates the handle.
- `SolidWorks.Interop.sldworks` declares its own `Environment` type, which collides with `System.Environment`. Alias it.
- The add-in's `Guid` in `GCamAddinRegistration.cs` (`df725bf7-…`) is SOLIDWORKS' identity for it. Unregister before changing it.
- `[assembly: ComVisible(false)]` — visibility is opted into per type.
