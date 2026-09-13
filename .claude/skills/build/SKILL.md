---
name: build
description: Build G-CAM and run its tests, reporting real failures separately from SOLIDWORKS file locks and expected regasm warnings. Use instead of running dotnet build or dotnet test directly - raw output for this project is actively misleading in two ways that have already caused wrong conclusions.
---

# Building G-CAM

```bash
python tools/build.py                 # build, then test
python tools/build.py --no-test       # build only
python tools/build.py --test-only      # tests only, skips the solution build
python tools/build.py --release        # Release configuration
python tools/build.py --verbose        # also print raw dotnet output
```

Output is a verdict and a short list, not a wall:

```
OK
  2 output file(s) could not be copied - locked by SolidWorks (16348). The code
  compiled; the add-in folder was not refreshed. Close SOLIDWORKS and rebuild
  before loading the add-in.
  123/123 tests passed
```

Exit code is non-zero only for real failures: compile errors, build-rule violations,
or failing tests.

## Why not just run dotnet

Two things about this project's raw output lead to wrong conclusions, and both have
already done so.

**SOLIDWORKS holds the add-in DLLs open.** Building while it runs produces eight
MSB3021/MSB3027 copy errors and "Build FAILED". Compilation succeeded — only the copy
into the add-in folder was blocked. Treating that as a build failure wastes a cycle;
treating it as success without noticing means loading a stale DLL into SOLIDWORKS and
debugging code you did not just change. The script reports it as a note, names the
holding process, and still exits 0.

**The post-build regasm step fails on every unelevated build** and prints
`RegAsm : warning RA0000 : An error occurred while writing the registration
information…`. Grepping the output for `error` therefore reports failures on a
completely clean build — which happened here, reporting "4 errors" when there were
none. The script filters these.

It also **deduplicates**: MSBuild prints each diagnostic once per parallel project
pass, so a single mistake otherwise appears two or four times.

## What it distinguishes

| Reported as | Means |
| --- | --- |
| `compile error` | A genuine `CS####`. Fix it. |
| `build rule` | A guard target failed — most likely `EnsureCoreHasNoSolidWorksReference`, the Core-purity rule. The message names the fix. |
| `test failed` | Named failing tests. Re-run with `--verbose` for assertion detail. |
| *note:* locked files | SOLIDWORKS is open. Code compiled, output not refreshed. |

## Things it will not do

**It does not close SOLIDWORKS.** That risks discarding unsaved work in a document
you cannot see. It names the process and the PID; closing it is yours.

**It does not register the add-in.** Registration needs elevation — use
`deploy/register.cmd`, and see CLAUDE.md for when re-registering is actually required.

**`-t:Compile` is not a faster alternative**, if you are tempted to skip the copy step
to dodge the locks. The XAML markup pass does not run under that target, so every WPF
code-behind fails with bogus `InitializeComponent does not exist` errors. Tried; it
wastes more time than it saves.
