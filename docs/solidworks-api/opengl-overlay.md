# Drawing OpenGL over the SOLIDWORKS 3D view

How G-CAM puts its own graphics — the stock box, toolpaths and cut-direction arrows now,
the simulated tool later — into the SOLIDWORKS graphics window.

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
| `glEnable(GL_BLEND)`, `glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA)` | Ordinary alpha compositing. |
| `glEnable/glDisable(GL_DEPTH_TEST)` per batch | On for scene objects, which belong behind whatever is in front of them; off for annotations that must never be hidden. |
| `glDepthMask(GL_FALSE)` per transparent *or* always-on-top batch | Stops the box's near wall from occluding its far wall, lets the model show through, and — see below — keeps annotations out of the depth buffer entirely. |

### Depth testing and depth writing are different questions

Easy to conflate, and they have different answers per batch:

- **Depth testing** is whether the model can hide *this* batch.
- **The depth mask** is whether this batch can hide what comes *after* it.

An annotation — G-CAM's coordinate triad, and later a toolpath buried in material — wants
neither. Visible through the part, obviously. But it must also leave no trace in the depth
buffer, because **SOLIDWORKS renders Layer2 after this notification** (active sketches,
annotations, its own reference triad, per the `BufferSwapNotify` Remarks). Writing depth
from something that was itself drawn without depth testing leaves those values at whatever
depth the annotation happened to sit at, and SOLIDWORKS' own later drawing is then tested
against them. So `AlwaysOnTop` turns off both.

### Drawing order

**Scene geometry first — opaque, then transparent — then the always-on-top annotations.**
Two separate rules, both load-bearing: blending only composites correctly over what is
already in the colour buffer, so opaque has to be down first; and an always-on-top batch
drawn early would simply be painted over by the ordinary geometry it is meant to sit above
— depth testing is not what keeps it on top, draw order is.

`SceneRenderer.Rebuild` sorts on `AlwaysOnTop` then `IsTransparent`. Both are stable sorts,
so a producer still controls the order within each group.

## Sizing something in pixels rather than millimetres

**Verified on 2025 SP3** — the cut-direction arrows on the Operation page hold their size
at any zoom.

An annotation that should stay the same size on screen needs to know what a pixel is worth
in model units. That is readable from the context itself, without any SOLIDWORKS API:

```csharp
Gl.GetDoublev(Gl.GL_MODELVIEW_MATRIX, modelView);    // both column-major
Gl.GetDoublev(Gl.GL_PROJECTION_MATRIX, projection);
Gl.GetIntegerv(Gl.GL_VIEWPORT, viewport);
```

`ViewScale` then projects two points a known distance apart — the anchor, and the anchor
stepped 1mm along the world direction that maps to the screen's X axis — and divides by the
pixels between them. Measuring rather than extracting a scale factor is what makes it
independent of whether the projection is orthographic or perspective and of where
SOLIDWORKS put the zoom. Under perspective the answer legitimately differs with depth,
which is why it takes the point it is about.

The world direction that maps to eye +X is the first *row* of the modelview's rotation —
elements 0, 4 and 8 in column-major storage — normalised, since a scale may be carried
there.

Two things fall out of doing this here rather than through `IModelView`:

- **No view to find.** Whichever window fired the notification is the one whose matrices
  are current, so two windows at different zooms each size their own drawing correctly from
  a single shared scene. Going through the API would need a scene per view.
- **No repaint to chase.** There is no zoom-changed event to subscribe to; the size is
  simply recomputed on the frame that is already being drawn.

These are reads, so `GlState` has nothing extra to put back.

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
