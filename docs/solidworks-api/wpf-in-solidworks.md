# Showing WPF windows from a SOLIDWORKS add-in

SOLIDWORKS is a native Win32 application. Everything below follows from that.

## Always set an owner

Use the SOLIDWORKS main frame as the window owner, or the dialog will fall behind SOLIDWORKS and look like a hang:

```csharp
var frame = (IFrame)swApp.Frame();
new WindowInteropHelper(window).Owner = new IntPtr(frame.GetHWndx64());
```

**`GetHWndx64`, not `GetHWnd`.** SOLIDWORKS is 64-bit and the 32-bit variant truncates the handle. See [addin-wont-load](addin-wont-load.md) for the general habit of checking which API variant is meant for 64-bit.

`WpfErrorPresenter` takes the handle as a `Func<IntPtr>` rather than a value, because the frame is not available at every point during start-up, and a dialog with no owner still beats losing the error.

## Modal versus modeless

`ShowDialog` is the safe default here, and G-CAM uses it for both the error dialog and the tool library browser. It runs its own nested message pump, so there is no question of input reaching the window from inside a Win32 host.

Modeless (`Show`) is nicer for a browser you want open while working, and top-level WPF windows own their HWND and message loop, so it should work — but it has not been tested against a real SOLIDWORKS session, and the tool library browser has a search box where a dropped keystroke would be obvious. `ToolLibraryDialog` is written so switching is a one-line change. **Assumed, not verified:** modeless windows behave correctly here. Test before relying on it.

The separate, harder case is WPF hosted *inside* a Win32 container via `ElementHost` — the FeatureManager tab. That genuinely does need care with keyboard and focus, and is a different problem from a top-level window.

## Keep a reference to a modeless window

A modeless window that nothing holds is eligible for collection while it is still on screen, which produces confusing intermittent failures. `ShowDialog` sidesteps this by blocking; if any window here goes modeless, something must hold it.

## WPF has no folder picker

There is no WPF folder-browse dialog. `GCam.UI` already references WinForms for `ElementHost`, so the tool library browser uses `System.Windows.Forms.FolderBrowserDialog` directly. Not elegant, but it avoids a dependency for one dialog.

## Presenting from a background thread

`IErrorPresenter` may be called from anywhere once toolpath calculation runs off-thread. `WpfErrorPresenter` captures `Dispatcher.CurrentDispatcher` at construction — which must therefore happen on the SOLIDWORKS main STA thread — and marshals with `BeginInvoke` rather than `Invoke`, so a worker thread is never blocked behind a modal dialog.

## Drawing

`ToolProfileView` overrides `OnRender` rather than composing shapes. For a grid whose line count depends on zoom, drawing imperatively is far simpler than generating and recycling elements, and `DrawingContext` output is vector-crisp at any DPI.

Two details that matter for a grid: snap 1px lines to half-pixel coordinates or they blur across two device pixels, and drop the fine grid once its spacing falls below a few pixels — a 1mm grid on a 60mm tool is a grey smear, not information.
