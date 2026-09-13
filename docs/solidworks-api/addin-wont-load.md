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

## When re-registration is actually needed

Registration records four things about the assembly: its CLSID, its name, its version, and
the path `regasm /codebase` wrote. Ordinary code changes touch none of them, so the vast
majority of builds need no re-registration at all. Re-register when one of these changes:

| Change | Why it breaks |
| --- | --- |
| Assembly version (`$(Version)` in `Directory.Build.props`) | `regasm` writes a version subkey and leaves the old one; the highest wins — the failure above |
| Assembly name | The CLSID points at a name that no longer exists |
| Output path, **including Debug↔Release** | `CodeBase` points at the DLL you are no longer building |
| The `Guid` in `GCamAddinRegistration.cs` | That GUID *is* SOLIDWORKS' identity for the add-in |

The GUID case has a trap: unregister **before** changing it. Once the assembly registers
under a new GUID, nothing knows about the old key, and SOLIDWORKS keeps offering the stale
entry in Tools > Add-Ins until it is deleted by hand.

Running Visual Studio as administrator sidesteps the whole question — the post-build
`regasm` step then succeeds on every build and registration never drifts.

## Other causes worth checking

**Never registered, or registered unelevated.** `regasm` writes to HKLM. Double-clicking `register.cmd` does not elevate — right-click, Run as administrator. The script warns if it is not elevated.

**Both assemblies registered?** `GCam.SolidWorks.dll` carries the ActiveX control for the FeatureManager tab. If only `GCam.AddIn.dll` is registered, the add-in loads but the tree tab is silently absent — a different symptom from this one.

**A dependency the CLR cannot find.** Less likely than it looks: the SOLIDWORKS interop assemblies resolve from the GAC, and `regasm /codebase` loads the add-in in a context that probes its own folder for the rest. Ruled out here — every assembly loaded individually without complaint.

## If activation succeeds but the add-in still fails

Then the failure *is* in `ConnectToSW`, and an exception thrown there makes SOLIDWORKS unload the add-in just as silently.

F5 on `GCam.AddIn` launches SOLIDWORKS with the debugger already attached — the launch target lives in `GCam.AddIn.csproj` rather than the `.user` file Visual Studio would normally write it to, because `.user` files are gitignored and the setting was silently lost once already. Otherwise, attach by hand:

1. Visual Studio > Debug > Attach to Process > `SLDWORKS.exe`
2. Debug > Windows > Exception Settings, tick **Common Language Runtime Exceptions**
3. Tick the add-in in Tools > Add-Ins

The debugger breaks on the throw with the real stack. This is the only reliable way to see inside `ConnectToSW`, because SOLIDWORKS swallows whatever comes out of it.
