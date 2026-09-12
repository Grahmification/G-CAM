# NuGet dependencies in a SOLIDWORKS add-in

**Symptom.** The add-in fails to start with `Could not load file or assembly 'X, Version=a.b.c.d'` — and the DLL is sitting right there in the output folder, at a *different* version.

**Verified 2026-09-12.** Adding Serilog produced exactly this:

```
Could not load file or assembly 'Serilog, Version=4.2.0.0,
Culture=neutral, PublicKeyToken=24c2f752a8e58a10'
   at GCam.AddIn.Composition.LoggingSetup.Create()
   at GCam.AddIn.GCamAddin.ConnectToSW(Object ThisSW, Int32 Cookie)
```

`Serilog.dll` in the output folder was **4.4.0.0**.

## Why it happens

`Serilog.Sinks.File 7.0.0` is compiled against `Serilog 4.2.0.0`. NuGet unified the graph to Serilog 4.4.0 and copied that one DLL. The CLR still sees an assembly reference asking for 4.2.0.0.

On .NET Framework, that gap is bridged by a **binding redirect** in the application's `.config`. The SDK generates those automatically — into `YourApp.exe.config`.

**An add-in has no .config of its own.** The CLR reads the *host process's* configuration, which is `SLDWORKS.exe.config`, and that knows nothing about our dependencies. So the redirect never exists and the load fails.

This applies to every NuGet package with a transitive dependency, not just Serilog. Clipper2 and anything else added later will hit the same wall.

## The fix

`GCam.AddIn/Composition/AssemblyResolver.cs` hooks `AppDomain.CurrentDomain.AssemblyResolve` and loads any requested assembly that exists in the add-in's own folder, by path. `Assembly.LoadFrom(path)` ignores the requested version, so it behaves as a catch-all binding redirect.

Two rules keep it neighbourly, because `AssemblyResolve` is process-wide and shared with SOLIDWORKS and every other add-in:

- Only resolve files that exist in **our** directory.
- Return null for everything else, so other handlers still get their turn.

## The part that is easy to get wrong

**The handler must be installed before any method that mentions the dependency's types is called.**

`LoggingSetup.Create()` had a `try`/`catch` around the whole body that was supposed to degrade to `NullLog` if logging failed to start. It never ran. The stack trace above shows the exception thrown *at* `Create()`, not inside it:

> The JIT resolves every type a method references when that method is first entered. A missing assembly therefore throws **before the first IL instruction executes**, so a try/catch inside that method cannot help.

The fix has to sit a level above. `GCamAddin`'s **static constructor** calls `AssemblyResolver.Install()`, and a type initialiser runs when COM creates the instance — before `ConnectToSW` is ever called, let alone `Create()`.

The general lesson: defensive `try`/`catch` protects against *runtime* failures in a method, never against *load* failures of what the method references.

## Alternatives considered

- **Pin Serilog to exactly 4.2.0** so the versions match. Works until the next package update, and does nothing for the next dependency.
- **Add redirects to `SLDWORKS.exe.config`.** Editing a vendor file that upgrades will overwrite, and it would have to be repeated on every machine.
- **ILMerge dependencies into the add-in.** Removes the problem entirely at the cost of a build step and harder debugging. Worth reconsidering if the dependency list grows.
- **Put assemblies in the GAC.** Requires strong naming and an install step; overkill for a dev loop.

## Diagnosing the next one

The exception is written to `%LOCALAPPDATA%\G-CAM\logs\startup-failure.log` by `GCamAddin.ReportBootstrapFailure`, including the assembly name and version the CLR actually wanted. Compare that against the version on disk:

```powershell
[Reflection.AssemblyName]::GetAssemblyName("<output>\Serilog.dll").Version
```

A mismatch between "wanted" and "on disk" is this problem. Same version on both sides means look elsewhere — a genuinely missing file, or the wrong architecture.

For the full binding trace, enable Fusion logging: set `HKLM\Software\Microsoft\Fusion!EnableLog` (DWORD) to 1, reproduce, then turn it off again — it carries a performance cost.
