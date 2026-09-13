# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

G-CAM is a SOLIDWORKS 2025 add-in (C#, .NET Framework 4.8) that generates CAM toolpaths, in the spirit of HSMWorks or Fusion 360's CAM workspace. Internal team tool, 3-axis milling only.

**No toolpath has been computed and nothing has been posted** — no strategy computes one yet. Jobs and operations *are* saved inside the part and survive a reopen. `docs/architecture.md` opens with the status table — trust it over any impression of progress, including the one this file gives.

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

`GCamAddin` is one `partial` class split by concern — `.cs` (lifetime), `.CommandManager.cs` (toolbar/tab construction), `.Callbacks.cs` (toolbar callbacks), `.Jobs.cs` (job commands), `GCamAddinRegistration.cs` (COM registration). A new concern gets a new `GCamAddin.*.cs`.

## Build, test, register

```bash
python tools/build.py          # build + test, with the noise filtered
```

Use that rather than `dotnet build` directly — SOLIDWORKS file locks look like build failures, and the unelevated `regasm` step warns in words containing "error". Both have already caused wrong conclusions here; the `build` skill has the detail.

**Close SOLIDWORKS before building** if you intend to load the add-in afterwards; it holds the output DLLs open, so the code compiles but the add-in folder keeps the previous build.

Registration writes to HKLM and needs elevation: run `deploy/register.cmd` from an elevated prompt, or run Visual Studio as administrator and the post-build step keeps it in sync. **Two assemblies get registered**, not one — registering only `GCam.AddIn.dll` gives a working toolbar and a silently missing tab. Ordinary code changes need no re-registration; `docs/solidworks-api/addin-wont-load.md` lists the changes that do.

**When the add-in will not load**, SOLIDWORKS reports nothing — the checkbox just un-ticks. Run `tools/diagnose-addin.ps1`; start-up failures also land in `%LOCALAPPDATA%\G-CAM\logs\startup-failure.log`. Same doc.

## Architecture rules

`docs/architecture.md` is authoritative and argues each of these in full under "Rules with teeth". The ones that bite most often:

- **`GCam.Core` never references SolidWorks.** Core declares the interfaces, `GCam.SolidWorks` implements them, `GCam.AddIn` wires them together. An MSBuild target enforces it, so a violation fails the build with the fix in the message.
- **No exception leaves G-CAM code.** Every method SOLIDWORKS, WPF or the task scheduler can call wraps its body in try/catch and calls `ErrorHandler.Handle`; interior code throws freely. An exception escaping `ConnectToSW` unloads the add-in silently. Entry-point list in `docs/error-handling.md`.
- **Job and operation editing happens on SOLIDWORKS-native PropertyManager pages, not WPF.** Derive from `GCamPropertyPage`; it seals the lifecycle callbacks on purpose. See `docs/solidworks-api/property-manager-pages.md`.
- **Never set `IPropertyManagerPageControl.Visible` on a page you are about to show.** It kills SOLIDWORKS outright — silently, with nothing in any log, and only after about the fourth show, which makes it look like anything but what it is. Pages are therefore rebuilt for every show, with controls created at the visibility they need via `AddControl2`'s options. Populate from `LoadControls` before `Show2`, never `AfterActivation`, and keep control ids unique per page — duplicates are accepted in silence.
- **Every OpenGL draw sits inside `using (new GlState())`**, which pushes the client attribute stack as well as the server one — pop only the server stack and SOLIDWORKS ends up reading through our vertex buffer. Draw only inside `BufferSwapNotify`; `Core/Rendering` says what to draw and `GCam.SolidWorks` says how. See `docs/solidworks-api/opengl-overlay.md`.
- **Core works in millimetres**, SOLIDWORKS in metres. Convert only at the edges, using `GCam.Core.Units` — never a bare `25.4` or `1000`.
- **Shared constants have one home, named for their purpose** — `GCam.Core.Units`, `GCam.Core.Precision.Epsilon`. Never a `Constants` junk drawer. A value used in one file stays private until a second caller appears.
- **Logic worth testing goes in Core, even when it looks like UI** — `ToolSearch` is in `Core/Tooling`, not the browser viewmodel. Core owns rules; viewmodels own presentation state. There is no `GCam.UI.Tests`, and moving the rule beats adding one.
- **Tool library edits are held in memory until the browser's OK, but creating a library is not an edit** — `CreateNew` and `SaveAsCopy` write immediately. Only `.gcamtools` is writable: ask `ToolLibraryImporter.CanWrite`, never compare extensions yourself.
- **Settings** (`%LOCALAPPDATA%\G-CAM\settings.xml`, via `IGCamSettings`) never throw. A corrupt or unwritable file yields defaults and a log line, never a failed load.
- **NuGet packages work only because of `AssemblyResolver`** in `GCam.AddIn/Composition` — an add-in gets no app.config, so binding redirects do not exist. Read `docs/solidworks-api/addin-dependencies.md` before debugging any "could not load file or assembly".

## SOLIDWORKS API

Target is **2025 SP3**. The `solidworks-api` skill reads the API help offline from the local CHM files — check any signature, enum value or Remarks there before writing a call, rather than guessing or fetching help.solidworks.com.

Two traps that give no useful error when hit: use `IFrame.GetHWndx64`, not `GetHWnd`, because the 32-bit variant truncates the handle; and `SolidWorks.Interop.sldworks` declares its own `Environment` type, which collides with `System.Environment` — alias it.

## Docs

`docs/` is the project's working notebook — `design/` for how one subsystem hangs together, `solidworks-api/` for API behaviour, `cam/` for CAM domain knowledge, `decisions/` for ADRs. Check it before researching a SOLIDWORKS question; much of what is there was learned by experiment and is not in the official help. Conventions are in `docs/README.md`; tag what you add **Verified** (say on which SOLIDWORKS version), **From docs**, or **Assumed**.

**Before working on an area, read its design note** — the status table at the top of `docs/architecture.md` links each area to one. It is faster than reading the code, and it names the projects the area spans, which is rarely just the obvious one.

**When you change the code, update the docs that describe it — in the same piece of work, not later.** A stale doc is worse than a missing one, because it is believed; this has already actively misled here twice. The status table and any test counts rot first.

| What changed | What to update |
| --- | --- |
| A project, folder, dependency rule, or anything in the layout | `docs/architecture.md` — the tree *and* the status table at the top |
| How one subsystem works, or a new one | Its note in `docs/design/`, indexed in that README and linked from the status table |
| A choice with real alternatives, that you would otherwise re-argue in six months | A new ADR in `docs/decisions/`, indexed in its README |
| SOLIDWORKS API behaviour learned by experiment | `docs/solidworks-api/`, tagged Verified / From docs / Assumed |
| CAM domain or file-format knowledge | `docs/cam/` |
| A rule that governs every edit, or a fact needed to get started | Here as well — sparingly, since this file loads every session |

If a change makes a documented statement false, fixing that statement is part of the change, not follow-up work.
