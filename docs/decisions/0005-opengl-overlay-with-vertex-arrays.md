# 0005. Draw the overlay with fixed-function vertex arrays

**Status:** Accepted
**Date:** 2026-09-12

Confirmed working 2026-09-12: the stock box draws on SOLIDWORKS 2025 SP3, on a rotated
coordinate system, with "Enhanced graphics performance" both on and off. The scale
argument below — that this shape still suits a large toolpath — remains an estimate, since
nothing large has been drawn yet.

## Context

G-CAM needs to draw its own graphics into the SOLIDWORKS 3D view: the stock box now, and
before long toolpath polylines, the tool and holder, and the Z-map surface the simulator
produces. `docs/architecture.md` had already settled that this happens through an OpenGL
overlay on `BufferSwapNotify` via hand-rolled P/Invoke, and that SOLIDWORKS owns the
context. What it had not settled is *which* OpenGL.

The constraints that were real at the time:

- **The context is not ours.** It is made current by SOLIDWORKS before
  `BufferSwapNotify` and we are guests in it for the duration of one callback. We do not
  know its version, its profile, or its pixel format, and we never created it.
- **Anything past OpenGL 1.1 needs `wglGetProcAddress`.** `opengl32.dll` exports only
  1.1 directly. Buffer objects, shaders and vertex array objects all have to be resolved
  at runtime against the current context, and the pointers are valid only for that
  context.
- **The data will get large.** A stock box is 36 vertices. A finishing toolpath is
  hundreds of thousands of segments and a Z-map is worse. Whatever shape the low-level
  API takes now, it has to still be the right shape then — the user asked for this
  explicitly.
- **Nothing must destabilise SOLIDWORKS.** A rendering mistake inside the host's context
  surfaces as a SOLIDWORKS bug, not as an add-in bug, and gets reported as one.

## Decision

Draw with **client-side vertex arrays in the fixed-function pipeline**:
`glVertexPointer` plus `glDrawArrays`, with the array held by a pinned `GCHandle` across
the pair of calls. Every entry point is a plain `DllImport` from `opengl32.dll`; nothing
is resolved at runtime.

The public shape is a **batch**: a run of vertices, one primitive kind, one colour, drawn
in one call. `GCam.Core.Rendering.RenderBatch` is that type, and `RenderScene` is a set of
named layers of them. Core decides what to draw and `GCam.SolidWorks` decides how, the
same split as everywhere else.

## Alternatives considered

**Immediate mode (`glBegin` / `glVertex3d` / `glEnd`).** The simplest thing that draws a
box, and the obvious starting point. Rejected on the scale requirement: it is one COM-free
but still per-vertex P/Invoke transition per vertex, which is fine for 36 and ruinous for
a toolpath — and the fix would be a rewrite of the drawing code rather than a change
underneath it. Rejected on an estimate rather than a measurement; the estimate is that
hundreds of thousands of P/Invoke calls per frame is not close.

**Vertex buffer objects.** The right answer for static geometry redrawn every frame, and
where this probably ends up if toolpath rendering turns out to be the bottleneck.
Rejected *for now* because `glGenBuffers` and friends are extensions: they need
`wglGetProcAddress` against SOLIDWORKS' context, they need the result cached per context,
and they need a fallback for when the lookup fails. That is a meaningful amount of
machinery to get working before anything at all appears on screen. The batch API is
deliberately shaped so this can be added underneath it without any caller changing — a
`RenderBatch` says what to draw, not how to upload it.

**A modern shader pipeline.** Best long-term performance and the only route to anything
fancy. Rejected as the wrong risk to take inside a host's context: it needs a GL version
we cannot guarantee, its own matrices (throwing away the correctly-set-up ones
`BufferSwapNotify` hands us), and it is the most likely of the three to fight SOLIDWORKS'
own renderer in ways that are hard to attribute.

**OpenTK or another managed GL binding.** Rejected earlier, in `docs/architecture.md`.
Worth restating: a binding that manages contexts is actively unhelpful when the context
is somebody else's, and it is another assembly to resolve through `AssemblyResolver` for
about twenty `DllImport` lines.

## Consequences

**Easier.** The entire interop surface is twenty static externs with no initialisation,
no capability detection and no failure path — the renderer cannot fail halfway. A batch is
one draw call whatever its size, so a large toolpath costs one transition, not one per
point. And because drawing happens in the context SOLIDWORKS set up, there are no
matrices, no projection and no camera anywhere in G-CAM: vertices are part coordinates and
that is all.

**Harder.** Per-frame vertex data crosses from managed memory every draw rather than
living on the GPU, so a genuinely large scene will redraw more slowly than a VBO would. It
is mitigated — `SceneRenderer` caches the converted float array against
`RenderScene.Version` and rebuilds only when the scene changes — but the upload is still
per frame. If that becomes the bottleneck, buffer objects go in behind the batch API.

**Foreclosed.** No shader effects: no per-pixel lighting on the simulated stock, no
depth-based fading of toolpath moves, no transparency that sorts itself. Anything of that
kind means revisiting this decision, not extending it.

**Pinning is now a rule to remember.** `glVertexPointer` hands OpenGL a raw address that
it reads later, when `glDrawArrays` runs. The array has to be pinned across both calls;
marshalling a `float[]` as a parameter pins it for only one. Getting that wrong produces
garbage geometry intermittently, whenever a collection happens to land between the two —
which is exactly the kind of bug that is expensive to find. It is contained in one method,
`SceneRenderer.DrawBatch`, and commented there.
