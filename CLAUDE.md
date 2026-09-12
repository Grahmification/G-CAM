# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

G-CAM is a SolidWorks add-in (C# class library, .NET Framework 4.8, `AnyCPU`) that aims to generate CAM toolpaths from SolidWorks models, in the spirit of Fusion 360's CAM workspace. It is currently at the "Hello World" stage: the add-in loads, grabs a `CommandManager`, and shows a message box.

## Layout

The solution lives one level down from the repo root: `G-CAM/G-CAM.sln` → `G-CAM/G-CAM/G-CAM.csproj`. Source files are in `G-CAM/G-CAM/`.

`GCamAddin` is a `partial` class deliberately split by concern:
- `GCamAddin.cs` — the `ISwAddin` lifetime (`ConnectToSW` / `DisconnectFromSW`), holds `SldWorks` and `ICommandManager`.
- `GCamAddinRegistration.cs` — COM attributes and the `[ComRegisterFunction]` / `[ComUnregisterFunction]` registry plumbing.

Keep that split: add-in behaviour goes in new partial files or new types, not into the registration file.

## Knowledge base

`docs/` is the project's working notebook — `docs/solidworks-api/` for API behaviour, `docs/cam/` for CAM domain knowledge, `docs/decisions/` for architecture decision records. See `docs/README.md` for conventions.

Check it before researching a SolidWorks API question; much of what's there was learned by experiment and won't be in the official help. When you work something out that took real effort — an API quirk, a units or coordinate-frame convention, a snippet that finally worked — write it down there, and tag whether it was **Verified** (say on which SolidWorks version), **From docs**, or **Assumed**.

## Build

```bash
# from the repo root
"/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe" G-CAM/G-CAM.sln -p:Configuration=Debug
```

The project has a post-build event that runs `regasm /codebase` on the output DLL. This writes to `HKLM\SOFTWARE\SolidWorks\Addins\{...}` and `HKCU\Software\SolidWorks\AddInsStartup\{...}`, so **builds need an elevated shell** or the post-build step fails (the compile itself still succeeds). Registration is how SolidWorks discovers the add-in — there is no separate install step.

Because `regasm /codebase` points the registry at the build output path, SolidWorks loads the DLL straight out of `bin\Debug\`. Moving or renaming the repo invalidates the registration; rebuild to fix it.

There are no tests and no lint setup in the repo yet.

## Running and debugging

`G-CAM.csproj.user` sets the debug start program to `C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SLDWORKS.exe`, so F5 in Visual Studio launches SolidWorks with the debugger attached to it. SolidWorks holds the DLL open while running — close it before rebuilding or the build fails with a file lock.

## SolidWorks API constraints

- The SolidWorks interop assemblies are referenced by absolute-ish `HintPath` into `C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist\`, with `EmbedInteropTypes=False`. They are not on NuGet here; a machine without SolidWorks installed cannot build. Other interops (`swcommands`, `swdocumentmgr`, …) are available in that same folder if needed.
- Everything crossing into SolidWorks is COM. Release COM objects (`Marshal.ReleaseComObject`) rather than relying on the GC — `DisconnectFromSW` already does this and forces a collection, which is the required pattern for SolidWorks add-ins to unload cleanly.
- The add-in's `Guid` in `GCamAddinRegistration.cs` (`df725bf7-…`) is the identity SolidWorks keys off. Changing it orphans the existing registry entries — unregister first (`regasm /unregister`) if it ever has to change.
- `AssemblyInfo.cs` has `[assembly: ComVisible(false)]`; visibility is opted into per-type via `[ComVisible(true)]`. New types SolidWorks must see need that attribute.
