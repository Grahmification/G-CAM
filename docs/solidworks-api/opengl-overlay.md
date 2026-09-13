# Drawing OpenGL over the SOLIDWORKS 3D view

How G-CAM puts its own graphics — the stock box now, toolpaths and the simulated tool
later — into the SOLIDWORKS graphics window.

Target: **SOLIDWORKS 2025 SP3**. Statements below are tagged **Verified** (tried on that
version), **From docs** (the installed API help says so), or **Assumed**.

> **Status, 2026-09-12: verified drawing.** The stock box renders in a running
> SOLIDWORKS 2025 SP3, with "Enhanced graphics performance" both on and off, and on a
> rotated coordinate system.

## The one notification that matters

**From docs.** `IModelView::BufferSwapNotify` fires *"immediately before the buffers are
swapped when rendering shaded graphics in OpenGL"*, and its Remarks carry the two facts
the whole design rests on:

> The OpenGL context contains matrices such that graphics are drawn correctly relative to
> the part or top-level assembly, but not any components within the assembly.

> This event is fired after rendering to Layer0.

So at the moment this fires:

- **SOLIDWORKS' OpenGL context is current.** Do not create one, do not make one current.
  G-CAM's `Gl` class deliberately has no context management in it at all.
- **The modelview and projection matrices are already set for part coordinates**, in
  metres. Vertices are handed straight to OpenGL with no projection work anywhere in
  G-CAM — `SceneRenderer` converts millimetres to metres and nothing else.
- **The model has already been drawn** (Layer0), so anything drawn here lands on top of
  it and can blend against it.

`RenderLayer0Notify` fires *during* Layer0 and `GraphicsRenderPostNotify` after Layer2
(sketches, annotations, the reference triad). Buffer swap is the right hook for an
overlay that belongs in the scene with the model.

## Subscribing: the type matters

**Verified** by reflecting over `SolidWorks.Interop.sldworks.dll` (33.3.0.92).

`IModelView` has **no events on it**. The events live on the coclass interface
`ModelView`, which inherits both `IModelView` and `DModelViewEvents_Event`. So:

```csharp
var view = model.GetFirstModelView() as ModelView;   // ModelView, not IModelView
view.BufferSwapNotify += OnBufferSwap;               // int OnBufferSwap()
```

Casting to `IModelView` compiles and then offers no event to attach to, which reads like
the API is missing something. The handler is
`DModelViewEvents_BufferSwapNotifyEventHandler`, declared `int Invoke()` — return 0.

**A document has more than one view.** `Window ▸ New Window` gives a part a second one,
and each is a separate `ModelView` with its own notification. Subscribe to all of them or
the overlay appears in one window and not the other. Walk them with
`IModelDoc2::GetFirstModelView` then `IModelView::GetNext` until it returns null
(**From docs**).

`ViewportRenderer.HookViews` re-reads that list on every invalidate and adds or drops the
difference, rather than tracking window open and close events — the same
compare-and-adjust shape as `JobTreeTabs.Sync`, and immune to a missed event for the same
reason.

**Do not release the `ModelView` objects.** We did not create them; they are the
document's own windows and the RCW is the one SOLIDWORKS and every other add-in hold. The
same reasoning as the `ModelDoc2` note in `manager-pane-tabs.md`.

## Enhanced graphics performance — it does *not* break the overlay

**Verified on 2025 SP3, and it contradicts the advice you will find everywhere else.**

Tools ▸ Options ▸ Performance ▸ **Enhanced graphics performance** is widely reported —
across forum posts, blog articles and add-in documentation dating from the SOLIDWORKS
2019–2021 era — to switch SOLIDWORKS onto a rendering pipeline that does not send
`BufferSwapNotify` to add-ins, leaving overlays silently blank.

**On 2025 SP3 that is not what happens.** The G-CAM overlay was tested with the option
both on and off and drew correctly either way.

G-CAM briefly shipped a `ViewportRenderer.WarnIfOverlayDisabled` that read the setting and
warned the user their graphics would not appear. It has been removed. Taking inherited
wisdom about an older version on trust produced a warning that fires while the thing it
warns about is working — worse than no warning, because it sends whoever reads it to fix
something that is not broken.

If you need the setting anyway, it is readable from the API under a name that does not
resemble its label (**From docs**):

```csharp
bool enhanced = swApp.GetUserPreferenceToggle(
    (int)swUserPreferenceToggle_e.swEnablePerformancePipeline);
```

**No configuration is currently known to stop the notification arriving.**
`ViewportRenderer.HasDrawn` remains as the diagnostic for it: it goes true the first time
`BufferSwapNotify` arrives, which is what separates *"the drawing code is wrong"* from
*"the notification never came"* — otherwise indistinguishable, because both are a blank
screen.

## Only OpenGL 1.1 is safely reachable

**Verified** to the extent that a 1.1-only renderer works inside SOLIDWORKS' context;
**Assumed** as to what a newer one would do.

Everything `opengl32.dll` exports directly is OpenGL 1.1. Anything newer — buffer
objects, shaders, vertex array objects — has to be fetched through `wglGetProcAddress`
against whichever context is current, and returns function pointers that are only valid
for that context. Inside SOLIDWORKS that context is not ours to reason about: we do not
create it, we do not know its pixel format or its version, and we are handed it for the
duration of one callback.

So G-CAM draws with **client-side vertex arrays** — `glVertexPointer` plus
`glDrawArrays`, both plain `DllImport`s, no extension loading, nothing that can fail
halfway. That still batches an arbitrarily long toolpath into one draw call, which is the
property that actually matters. See
[decision 0005](../decisions/0005-opengl-overlay-with-vertex-arrays.md).

Pinning: `glVertexPointer` stores the address and OpenGL reads through it when
`glDrawArrays` runs. A `float[]` marshalled as a parameter is pinned only for the
duration of that one call, so the array **must** be held by a pinned `GCHandle` across
both calls. Getting this wrong produces garbage geometry or a crash, intermittently, once
a GC happens to land between the two.

## Leaving the context as you found it

**The rule from `docs/architecture.md`, made mechanical in `GlState`.**

SOLIDWORKS owns the context. Anything left enabled, any colour left set, any vertex array
left pointing into a managed buffer that has since moved becomes a SOLIDWORKS rendering
bug that looks nothing like an add-in problem — and gets reported as a SOLIDWORKS bug,
because that is what it looks like.

`glPushAttrib` / `glPopAttrib` save server state. **They do not save client state.** Which
vertex arrays are enabled and where they point is a separate stack,
`glPushClientAttrib` / `glPopClientAttrib` with `GL_CLIENT_VERTEX_ARRAY_BIT`. Pushing only
the first is the easy mistake: the array pointer survives the pop, and SOLIDWORKS then
draws through a pointer into our buffer.

G-CAM names the attribute groups it saves rather than using `GL_ALL_ATTRIB_BITS`, so the
list also documents what the drawing code touches.

## Drawing transparently over the model

The state `SceneRenderer.BeginOverlay` sets, and why each one:

| Call | Why |
| --- | --- |
| `glDisable(GL_LIGHTING)` | A lit primitive takes its colour from the material state, not from `glColor`. Overlays are flat annotations, not modelled surfaces. |
| `glDisable(GL_TEXTURE_2D)` | SOLIDWORKS may have a texture bound; it would tint everything drawn here. |
| `glDisable(GL_CULL_FACE)` | A translucent box has to show both of its walls. |
| `glEnable(GL_DEPTH_TEST)` | The overlay belongs in the scene, behind whatever is in front of it. |
| `glEnable(GL_BLEND)`, `glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA)` | Ordinary alpha compositing. |
| `glDepthMask(GL_FALSE)` per transparent batch | Stops the box's near wall from occluding its far wall, and lets the model show through. |

**Opaque batches are drawn before transparent ones.** Blending only composites correctly
over what is already in the colour buffer. `SceneRenderer.Rebuild` sorts on
`RenderColour.IsTransparent`, which is a stable sort, so a producer still controls the
order within each group.

## Asking for a repaint

`IModelView::GraphicsRedraw(null)` repaints the whole window, synchronously — the buffer
swap notification has already run by the time it returns (**From docs** for the method,
**Verified** for the synchrony). `IModelDoc2::GraphicsRedraw2` is marked obsolete in the
2025 help and points at this instead.

`RenderScene` raises `Changed` on every mutation and `ViewportRenderer` redraws from it,
so nothing that builds graphics has to remember to ask for a repaint.

## Still open

- **Assumed:** that `BufferSwapNotify` is not raised when the view is in wireframe or
  fast-HLR mode. The Remarks say it *is* also sent for HLR/HLV dynamic rotation and
  "any other wireframe repainting", so this may well be fine — untested either way.
  `IModelView::GetDisplayState` is the documented way to find out what mode a view is in.
- **Untested:** more than one window on the same part. The code handles it by design;
  nobody has opened a second window yet.
- **Untested:** the overlay under a high-DPI display scale, and whether `glLineWidth`
  values need scaling with it.
