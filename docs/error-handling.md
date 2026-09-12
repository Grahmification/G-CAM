# Error handling

Plan for catching, logging and reporting exceptions. Decided 2026-09-12; not yet implemented.

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
├── Boundary.cs            the wrapper every entry point uses
└── FaultLatch.cs          stops repeat reporting from high-frequency callbacks
```

`Boundary` is the whole pattern in one class:

```csharp
public sealed class Boundary
{
    public Boundary(IGCamLog log, IErrorPresenter presenter) { ... }

    /// Entry points that return nothing.
    public void Run(string context, Action action);

    /// Entry points that must return something to SOLIDWORKS even on failure.
    public T Run<T>(string context, Func<T> func, T fallback);

    /// Async work started from an entry point.
    public Task RunAsync(string context, Func<Task> action);

    /// High-frequency callbacks: logs, never shows UI. Returns false on failure.
    public bool RunQuiet(string context, Action action);
}
```

and its body is the policy:

```csharp
try
{
    action();
}
catch (GCamUserException ex)
{
    _log.Info("User error in {0}: {1}", context, ex.Message);
    _presenter.ShowUserError(ex.Message);      // plain message, no stack, never rethrown
}
catch (Exception ex)
{
    _log.Error(ex, "Unhandled exception in {0}", context);
#if DEBUG
    throw;                                      // debugger breaks here, live stack intact
#else
    _presenter.ShowError(ex, context);
#endif
}
```

The `#if DEBUG` is the only place build configuration changes behaviour. Note the consequence: **release behaviour is not what you see while developing**, so the release path deserves a deliberate test before the add-in goes to anyone else.

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

Every one of these is called *by* something outside G-CAM, so every one needs a `Boundary` wrapper.

| # | Entry point | Where | Policy |
| --- | --- | --- | --- |
| 1 | `ConnectToSW` | `GCamAddin` | Wrap each subsystem separately. Return `true` if the add-in is minimally usable; `false` only if nothing works |
| 2 | `DisconnectFromSW` | `GCamAddin` | `RunQuiet` — never block an unload with a dialog |
| 3 | `ComRegisterFunction` / `ComUnregisterFunction` | `GCamAddinRegistration` | Runs under `regasm`, not SOLIDWORKS. No WPF, no Serilog — write to `Console` and a fallback file |
| 4 | `OnCommand` | `GCamAddin.Callbacks` | Full `Run` — this is the main user-facing path |
| 5 | `OnCommandEnable` | `GCamAddin.Callbacks` | `Run<int>` with fallback `0` (disabled). Called constantly, so **never** show UI; latch so it logs once, not once per repaint |
| 6 | `JobTreeTabHost` constructor | `GCam.SolidWorks.Hosting` | Catch in the ctor; on failure host a plain error label so the tab appears but empty. An exception here means no tab and no explanation |
| 7 | PropertyManager page handlers | `PropertyPages/` | Every `IPropertyManagerPage2Handler9` method wrapped. Base class does it once so individual pages cannot forget |
| 8 | Document events (`SaveToStorageNotify`, `LoadFromStorageNotify`, file open/close) | `Events/` | `Run<int>` with fallback `0`. A throw during save risks corrupting the document's third-party storage |
| 9 | `BufferSwapNotify` render callback | `Rendering/ViewHooks` | `RunQuiet` **plus** `FaultLatch`: after 3 consecutive failures, unhook the renderer and report once. Fires on every redraw — a dialog here is an unkillable modal storm |
| 10 | WPF event handlers and commands | `GCam.UI` | Wrap in viewmodel command bodies; `Dispatcher.UnhandledException` as backstop |
| 11 | Background toolpath tasks | `Core` via `Task.Run` | `RunAsync`. Presentation must marshal back through `SwDispatcher` — a WPF dialog cannot be shown from a worker thread |
| 12 | `TaskScheduler.UnobservedTaskException` | `GCam.AddIn` | Log only. Safety net for a fire-and-forget task nobody awaited |

**Process-global handlers deserve care.** `AppDomain.CurrentDomain.UnhandledException` and `TaskScheduler.UnobservedTaskException` are per-process, and we share that process with SOLIDWORKS and every other add-in. Log only, never present UI, never change process behaviour, and filter to stacks containing a `GCam.*` frame so we are not logging someone else's faults as ours.

## Threading

`IErrorPresenter` is only safe on the SOLIDWORKS main STA thread. Since Core computes on background threads by design, the presenter implementation marshals through `ISwDispatcher` itself rather than trusting callers to remember. Logging is thread-safe and needs no marshalling.

## What this deliberately does not do

- **No telemetry or crash upload.** Internal tool; the log file plus "Copy details" is the reporting channel.
- **No retry or recovery.** A failed operation is abandoned, not retried. Recovery policy belongs to specific features once they exist.
- **No `Result<T>` in Core.** Expected failures use `GCamUserException`. Revisit if exception-driven control flow shows up in a hot path.
- **`OnCommandEnable` returns "disabled" on failure**, which means a broken check silently greys a button. The latched log entry is the only clue, which is a deliberate trade against dialog spam.
