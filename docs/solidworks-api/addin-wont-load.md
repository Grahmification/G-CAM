# The add-in checkbox un-ticks itself

**Symptom.** In Tools > Add-Ins, ticking G-CAM immediately unticks it. No error dialog, nothing in the status bar.

**What is actually happening.** SOLIDWORKS tried to `CoCreateInstance` the add-in's CLSID, the call failed, and SOLIDWORKS gave up without reporting anything. The failure is in COM activation, *before* a single line of `ConnectToSW` runs — so debugging the add-in's own code will tell you nothing.

Run `tools\diagnose-addin.ps1`. It reproduces the activation outside SOLIDWORKS and reports the real error.

## Cause: stale version subkeys (hit 2026-09-12)

**Verified.** `regasm` writes a version subkey under the CLSID for each assembly version it registers, and **leaves older ones in place**. mscoree activates the *highest version it finds* — not the most recently registered one.

Renaming the assembly during the `GCam.*` restructure left this behind:

```
HKLM\SOFTWARE\Classes\CLSID\{DF725BF7-...}\InprocServer32
    Assembly = GCam.AddIn, Version=0.0.0.0     <- current, correct
  \1.0.0.0
    Assembly = G-CAM, Version=1.0.0.0          <- stale
    CodeBase = ...\G-CAM\G-CAM\bin\Debug\G-CAM.DLL   (deleted)
```

`1.0.0.0` beat `0.0.0.0`, so the CLR tried to load a DLL that no longer existed and returned `0x80070002 ERROR_FILE_NOT_FOUND`.

The diagnosis that nailed it: install an `AssemblyResolve` handler in a test host and log what the CLR asks for. It asked for **`G-CAM`** — an assembly name that had not existed for hours. That is what pointed at the registry rather than at the code.

**Fixes applied.**
- `deploy\register.cmd` now deletes the whole CLSID key before calling `regasm`, so every registration is clean.
- `GCam.AddIn` had `GenerateAssemblyInfo=false`, which meant `$(Version)` was ignored and the assembly built as `0.0.0.0` — sorting below any stale entry. Now enabled, so it versions as `0.1.0.0` with the rest.

**Avoid it recurring:** run `deploy\register.cmd Debug /u` *before* renaming or deleting a registered assembly. Once the DLL is gone, `regasm /unregister` cannot clean up after it and the key must be deleted by hand.

## Other causes worth checking

**Never registered, or registered unelevated.** `regasm` writes to HKLM. Double-clicking `register.cmd` does not elevate — right-click, Run as administrator. The script warns if it is not elevated.

**Both assemblies registered?** `GCam.SolidWorks.dll` carries the ActiveX control for the FeatureManager tab. If only `GCam.AddIn.dll` is registered, the add-in loads but the tree tab is silently absent — a different symptom from this one.

**A dependency the CLR cannot find.** Less likely than it looks: the SOLIDWORKS interop assemblies resolve from the GAC, and `regasm /codebase` loads the add-in in a context that probes its own folder for the rest. Ruled out here — every assembly loaded individually without complaint.

## If activation succeeds but the add-in still fails

Then the failure *is* in `ConnectToSW`, and an exception thrown there makes SOLIDWORKS unload the add-in just as silently. Attach a debugger:

1. Visual Studio > Debug > Attach to Process > `SLDWORKS.exe`
2. Debug > Windows > Exception Settings, tick **Common Language Runtime Exceptions**
3. Tick the add-in in Tools > Add-Ins

The debugger breaks on the throw with the real stack. This is the only reliable way to see inside `ConnectToSW`, because SOLIDWORKS swallows whatever comes out of it.
