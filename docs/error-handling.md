# Error handling

Policy for catching, logging and reporting exceptions. Decided 2026-09-12.

`ErrorHandler`, the Serilog log, the WPF error dialog and entry points 1–8 and 10 are
built. Entry points 9, 11 and 12 arrive with the subsystems they belong to, which do not
exist yet.

## Why an add-in needs this more than an ordinary app

SOLIDWORKS is the host, and it is unhelpful about failures in two specific ways:

- **It swallows exceptions out of `ConnectToSW` and unloads the add-in**, with no message. That is precisely the failure that cost us an afternoon — see [addin-wont-load](solidworks-api/addin-wont-load.md).
- **Exceptions crossing a COM boundary become HRESULTs.** SOLIDWORKS calls most of our code through IDispatch by method name. A .NET exception thrown back across that boundary is, at best, discarded; at worst it destabilises the host.

So the rule is: **no exception ever leaves G-CAM code.** Every method SOLIDWORKS, WPF or the task scheduler can call is a boundary, and every boundary catches.

## Principles

1. **Catch at entry points, not everywhere.** Interior code throws freely. Only boundaries catch, so a stack trace survives intact to the point where it is logged.
2. **Expected failures are not bugs.** "Tool Ø12 will not fit a Ø8 pocket" is a message; a `NullReferenceException` is a defect. They get different presentation.
3. **A failure in one subsystem must not take down the others.** A broken tree tab should still leave a working toolbar.
4. **Developer builds break into the debugger; release builds report and continue.**

## Components

### Core — abstractions only (netstandard2.0, no SolidWorks, no Serilog)

```
Core/Diagnostics/
├── IGCamLog.cs            Debug / Info / Warn / Error(ex, message, args)
├── NullLog.cs             no-op default, used by tests
├── IErrorPresenter.cs     ShowError(ex, context) / ShowUserError(message)
├── GCamUserException.cs   expected failure; its Message IS the user-facing text
└── ErrorHandler.cs        one method — the entire policy
```

`ErrorHandler` has a single public method:

```csharp
public sealed class ErrorHandler
{
    public ErrorHandler(IGCamLog log, IErrorPresenter presenter) { ... }

    /// <param name="quiet">Log only, never show UI. For high-frequency callbacks.</param>
    public void Handle(Exception ex, string context, bool quiet = false)
    {
        if (ex is GCamUserException user)
        {
            _log.Info("User error in {0}: {1}", context, user.Message);
            if (!quiet) _presenter.ShowUserError(user.Message);
            return;                                  // expected; never rethrown
        }

        bool firstTime = _seen.Add(context + "|" + ex.GetType().FullName);
        if (firstTime) _log.Error(ex, "Unhandled exception in {0}", context);
        else           _log.Debug("Repeat of {0} in {1}", ex.GetType().Name, context);

#if DEBUG
        // NOT a bare `throw;` — that is only legal inside a catch block, and this
        // method is called *from* one. Capture/Throw rethrows with the original
        // stack trace intact; `throw ex;` would reset it to this line.
        ExceptionDispatchInfo.Capture(ex).Throw();
#else
        if (!quiet && firstTime) _presenter.ShowError(ex, context);
#endif
    }
}
```

**A caveat on the Debug rethrow.** It breaks in the debugger at the rethrow inside `Handle`, not at the original throw site — the stack trace on `ex` is intact, but execution has already unwound. The better debugging tool is Visual Studio's **Exception Settings > Common Language Runtime Exceptions**, which breaks at the original `throw` before any catch runs. Treat the rethrow as a backstop that makes failures impossible to ignore, not as the primary diagnostic.

Call sites are plain `try`/`catch`:

```csharp
public void OnCommand(int id)
{
    try { ... }
    catch (Exception ex) { _errors.Handle(ex, nameof(OnCommand)); }
}

public int OnCommandEnable(int id)
{
    try { return ...; }
    catch (Exception ex) { _errors.Handle(ex, nameof(OnCommandEnable), quiet: true); return 0; }
}
```

### Why plain try/catch rather than a lambda wrapper

An earlier draft of this document had a `Boundary.Run(context, () => ...)` wrapper with overloads for void, `T`, async and quiet. It was rejected:

- **It does not compile on some entry points.** `IPropertyManagerPage2Handler9.OnSubmitSelection` takes `ref string ItemText` (the help says `out`; the interop disagrees — see [property-manager-pages.md](solidworks-api/property-manager-pages.md)), and C# cannot capture `ref`/`out` parameters in a lambda. Several SOLIDWORKS callbacks are shaped this way, so the wrapper would cover most entry points and force plain try/catch on the rest — two styles to maintain instead of one.
- **It needs an overload per signature shape.** try/catch needs none: one `Handle` serves every return type, `out` parameter and async method in the codebase.
- **It allocates a closure per call.** Irrelevant almost everywhere, but `BufferSwapNotify` runs on every redraw.
- **It puts lambda frames in the stack trace**, directly above the thing you are trying to read.

What the wrapper bought was that you cannot forget the catch. That is a real benefit, and the mitigation is the entry-point table below plus a `PmpHandlerBase` that implements each interface method with the try/catch once, so individual pages never write it.

The trade accepted here: **roughly two lines of boilerplate per entry point, and it is possible to forget one.** The policy itself still lives in exactly one place, which is the point of the exercise.

The `#if DEBUG` is the only place build configuration changes behaviour. Note the consequence: **release behaviour is not what you see while developing**, so the release path deserves a deliberate test before the add-in goes to anyone else.

Repeat suppression is built into `Handle` rather than a separate latch class: the first occurrence of a given context + exception type logs at Error and shows the dialog, later ones log at Debug and stay silent. That covers `OnCommandEnable` and the render callback without extra machinery. *Recovery* actions — the renderer unhooking itself after repeated failures — belong to the renderer, not to the error infrastructure.

### GCam.AddIn — logging setup

Serilog, wired once in `ConnectToSW` before anything else can fail:

```
%LOCALAPPDATA%\G-CAM\logs\gcam-20260912.log
```

Rolling daily, 7 files retained, 10 MB cap per file, shared-write enabled so two SOLIDWORKS instances do not fight over the handle. The first lines of every session record the G-CAM version, the SOLIDWORKS version and a session GUID — without those, a log a colleague sends you is guesswork.

Core never references Serilog. `SerilogGCamLog : IGCamLog` adapts it in `GCam.AddIn`.

### GCam.UI — presentation

`ErrorDialog.xaml` plus `WpfErrorPresenter : IErrorPresenter`:

- A one-line summary in plain language.
- A collapsed **Details** pane with exception type, message and stack.
- **Copy details** — puts the whole report on the clipboard. This is the point of the dialog: a teammate's bug report is only useful if they can paste it.
- **Open log folder** — opens Explorer at the log directory.
- Owned by the SOLIDWORKS main window, via the HWND from `IFrame::GetHWndx64`.

`ShowUserError` uses the same dialog with the details pane absent entirely.

## Entry points

Every one of these is called *by* something outside G-CAM, so every one needs a `try`/`catch` calling `ErrorHandler.Handle`.

| # | Entry point | Where | Policy |
| --- | --- | --- | --- |
| 1 | `ConnectToSW` | `GCamAddin` | Wrap each subsystem separately. Return `true` if the add-in is minimally usable; `false` only if nothing works |
| 2 | `DisconnectFromSW` | `GCamAddin` | `quiet: true` — never block an unload with a dialog |
| 3 | `ComRegisterFunction` / `ComUnregisterFunction` | `GCamAddinRegistration` | Runs under `regasm`, not SOLIDWORKS. No WPF, no Serilog — write to `Console` and a fallback file |
| 4 | `OnCommand` | `GCamAddin.Callbacks` | Full reporting — this is the main user-facing path |
| 5 | `OnCommandEnable` | `GCamAddin.Callbacks` | `quiet: true`, return `0` (disabled). Called constantly, so **never** show UI; repeat suppression keeps it to one log line |
| 6 | `JobTreeTabHost` constructor | `GCam.SolidWorks.Hosting` | Catch in the ctor; on failure host a plain error label so the tab appears but empty. An exception here means no tab and no explanation |
| 7 | PropertyManager page handlers | `PropertyPages/` | `PmpHandlerBase` implements all 37 `IPropertyManagerPage2Handler9` methods with the try/catch and delegates to a protected virtual of the same name, so pages cannot forget. The per-callback defaults are tabulated in [property-manager-pages.md](solidworks-api/property-manager-pages.md) |
| 8 | Document events (`ActiveModelDocChangeNotify`, `FileCloseNotify` in `Hosting/JobTreeTabs`; later `SaveToStorageNotify`, `LoadFromStorageNotify` in `Events/`) | `Hosting/`, `Events/` | Return `0` on failure. A throw during save risks corrupting the document's third-party storage |
| 9 | `BufferSwapNotify` render callback | `Rendering/ViewHooks` | `quiet: true`. The renderer counts its own consecutive failures and unhooks after 3, reporting once. Fires on every redraw — a dialog here is an unkillable modal storm |
| 10 | WPF event handlers, and the job tree's WinForms context menu | `GCam.UI` | Wrap each handler body; `Dispatcher.UnhandledException` as backstop. `JobTreeView` routes them all through one private `Handle` |
| 11 | Background toolpath tasks | `Core` via `Task.Run` | try/catch inside the awaited method. Presentation marshals back through `SwDispatcher` — a WPF dialog cannot be shown from a worker thread |
| 12 | `TaskScheduler.UnobservedTaskException` | `GCam.AddIn` | Log only. Safety net for a fire-and-forget task nobody awaited |

**Process-global handlers deserve care.** `AppDomain.CurrentDomain.UnhandledException` and `TaskScheduler.UnobservedTaskException` are per-process, and we share that process with SOLIDWORKS and every other add-in. Log only, never present UI, never change process behaviour, and filter to stacks containing a `GCam.*` frame so we are not logging someone else's faults as ours.

## Threading

`IErrorPresenter` is only safe on the SOLIDWORKS main STA thread. Since Core computes on background threads by design, the presenter implementation marshals through `ISwDispatcher` itself rather than trusting callers to remember. Logging is thread-safe and needs no marshalling.

## What this deliberately does not do

- **No telemetry or crash upload.** Internal tool; the log file plus "Copy details" is the reporting channel.
- **No retry or recovery.** A failed operation is abandoned, not retried. Recovery policy belongs to specific features once they exist.
- **No `Result<T>` in Core.** Expected failures use `GCamUserException`. Revisit if exception-driven control flow shows up in a hot path.
- **`OnCommandEnable` returns "disabled" on failure**, which means a broken check silently greys a button. The latched log entry is the only clue, which is a deliberate trade against dialog spam.
