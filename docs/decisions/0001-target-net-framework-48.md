# 0001. Target .NET Framework 4.8 for the add-in

**Status:** Accepted
**Date:** 2026-09-12

## Context

G-CAM is an in-process COM add-in loaded by SOLIDWORKS 2025. The Hello World add-in already targets .NET Framework 4.8, and the question was whether to move to .NET 8 before the architecture solidifies.

The immediate trigger was rendering. Toolpaths will be drawn as an OpenGL overlay, and OpenTK — the obvious managed GL binding — dropped .NET Framework support: OpenTK 4.x targets .NET Core 3.1+, and only the effectively unmaintained OpenTK 3.x runs on net48.

The broader draw was the runtime itself. A Z-map simulator and a geometry kernel are exactly the workload that benefits from `Span<T>`, hardware intrinsics and a modern GC.

## Decision

**Target .NET Framework 4.8 for the add-in and all projects it loads.** Write the ~20 `opengl32.dll` P/Invoke declarations the overlay needs by hand rather than taking a GL binding dependency.

## Alternatives considered

**.NET 8 with COM hosting.** Technically supported: `<EnableComHosting>true</EnableComHosting>` emits a native `*.comhost.dll` registered with 64-bit `regsvr32` instead of `regasm`, and the add-in's own `ComRegisterFunction` still writes the SOLIDWORKS `Addins` keys. xCAD documents this path, so it has been walked.

Rejected because **only one .NET Core runtime can load into a process.** `hostfxr_initialize_for_runtime_config` fails outright if an incompatible runtime is already present, and roll-forward only works upward — a .NET 8 add-in can join a process already running .NET 9, but a .NET 10 add-in cannot join yours. SOLIDWORKS is a shared host for arbitrary third-party add-ins, so whether G-CAM loads at all would depend on what else the user installed and in what order.

This was [reported against dotnet/runtime](https://github.com/dotnet/runtime/issues/49686) for the directly analogous Outlook add-in case, with the conclusion that COM hosting "can only support loading one version of .NET at a time, which effectively makes COM hosting useless for a lot of scenarios since you cannot control what else happens to be loaded in the hosting process."

.NET Framework has [in-process side-by-side hosting](https://learn.microsoft.com/en-us/dotnet/framework/deployment/in-process-side-by-side-execution) specifically so that managed COM components run against the version they were built with. That capability did not carry forward to .NET 5+.

**Verified on this machine (2026-09-12):** SOLIDWORKS 2025 ships no `hostfxr.dll` or `coreclr.dll`, so it hosts no Core runtime itself, while .NET 8.0, 9.0 and 10.0 runtimes are all installed. A .NET 8 add-in would therefore work here today — which is precisely the trap. The failure mode is a machine with a different add-in set, where it fails to load for reasons invisible from our side.

**OpenTK 3.x on net48.** Works, MIT licensed, complete bindings. Rejected as unnecessary: SOLIDWORKS owns the GL context, so the overlay only issues draw calls. Context creation, windowing and input — most of what OpenTK provides — would never be called. Taking a dependency with its likely-final release behind it, for a fraction of its surface area, is a poor trade.

## Consequences

**Accepted costs.** No `System.Runtime.Intrinsics`; `Span<T>` and `Vector<T>` are available only via the `System.Memory` and `System.Numerics.Vectors` backports, and performance trails .NET 8 (no dynamic PGO, older GC). We write and verify the GL signatures ourselves. Dependencies must offer netstandard2.0 or net4x targets — Clipper2 does.

**Gained.** Add-in loading is deterministic regardless of what else is installed. `regasm` registration and the existing build step keep working. AppDomains and the well-documented SOLIDWORKS add-in path remain available, which matters when something breaks at a teammate's desk.

**Revisit when** profiling shows toolpath calculation is genuinely runtime-bound. The response then is to move `GCam.Core` into an out-of-process .NET 8 worker — modern runtime where the mathematics is, .NET Framework where COM is — *not* to retarget the add-in. Keeping Core free of SolidWorks references is what preserves that option, which is a second reason to treat that rule as load-bearing.
