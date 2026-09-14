# Coordinate systems and bounding boxes

Getting a job's coordinate system out of SOLIDWORKS as a transform Core can use, and
measuring how big the model is along that system's axes rather than the part's.

Target: **SOLIDWORKS 2025 SP3**. Tagged **Verified**, **From docs**, or **Assumed**.

> **Status, 2026-09-12: verified on a rotated coordinate system**, which is the case this
> design exists for and the one an axis-aligned test part cannot exercise. The stock box
> lands correctly on SOLIDWORKS 2025 SP3.

## The transform, and why G-CAM measures it instead of reading it

**From docs.** `IModelDocExtension::GetCoordinateSystemTransformByName(name)` returns an
`IMathTransform` for a coordinate system feature. It maps **coordinate-system space into
part space**; `IMathTransform::Inverse` gives the other direction.

**The trap is `IMathTransform::ArrayData`.** It is 16 doubles and the help says only:

> The first 9 elements define the 3x3 rotation matrix. The next 3 elements define the
> translation component. The next element defines the scaling component. The last 3
> elements are unused.

It never says whether those nine are stored **by row or by column**. Read them the wrong
way round and you get the transpose — which is *the correct answer for every
axis-aligned coordinate system* and wrong only once one is rotated. That is the worst
possible failure mode: it works on every test part anyone builds deliberately, and breaks
on the first real one.

So `CoordinateSystems.Measure` does not decode the array. It asks SOLIDWORKS where four
known points land and reads the answer off:

```csharp
Vec3 origin = Apply(maths, transform, 0, 0, 0);
Vec3 x = Apply(maths, transform, 1, 0, 0) - origin;
Vec3 y = Apply(maths, transform, 0, 1, 0) - origin;
Vec3 z = Apply(maths, transform, 0, 0, 1) - origin;

return Matrix4.FromAxes(ToMillimetres(origin), x, y, z);
```

where `Apply` is `IMathUtility::CreatePoint` → `IMathPoint::MultiplyTransform` →
`IMathPoint::ArrayData`. A transform is completely described by what it does to the
origin and the three unit axes, and `MultiplyTransform` documents its own convention —
*"the point is rotated, scaled, and then translated"* (**From docs**) — so nothing here
is being guessed at. Eight COM calls per preview, which is nothing next to being quietly
wrong on rotated jobs.

**Verified:** a job on a rotated coordinate system puts its stock box in the right place —
precisely the test that would have exposed a transposed matrix, and the one no
axis-aligned part can perform.

Subtracting the origin from each axis image is what makes the result independent of units
as well as of layout: the probe points are one metre long because SOLIDWORKS works in
metres, but the differences come out dimensionless. Only the translation is converted
into millimetres.

This technique is also why `GCam.Core.Matrix4` has no inverse: SOLIDWORKS supplies both
directions and `JobFrame` carries the pair.

## Bounding boxes: not `GetBodyBox`

**From docs, and decisive.** `IBody2::GetBodyBox` returns a box *axis-aligned to the
part*, and its Remarks say:

> IMPORTANT: The values returned are approximate and should not be used for comparison or
> calculation purposes. Furthermore, the bounding box may vary after rebuilding the model.

Two separate disqualifications. Sizing stock *is* calculation, and for a job on a rotated
coordinate system a part-aligned box describes the wrong box entirely.

**`IBody2::GetExtremePoint` answers the question directly.**

```csharp
bool found = body.GetExtremePoint(dx, dy, dz, out x, out y, out z);
```

> This method returns the furthest possible point of intersection between a plane normal
> to the direction vector specified and the model as the plane moves along the direction
> vector.

Ask along the job's six axis directions — ±X, ±Y, ±Z, each transformed into part space
and renormalised — and the box around the six points that come back is the **exact tight
box in job space**. The −X extreme point holds the smallest X any point of the body has,
so nothing can fall outside it; likewise on each axis. It is exact for curved faces,
which a tessellated box is not.

That last point matters here specifically. `IFace2::GetTessTriangles` is the obvious
alternative and its own Remarks rule it out (**From docs**):

> These triangles are intended for graphics display purposes and do not represent a
> tessellation that can be used, for example, by a machining application.

Worth remembering for the geometry kernel generally, not just for bounding boxes.

Units: `GetExtremePoint` works in metres like the rest of the API. `ModelExtent` converts
at that boundary and everything above it is millimetres.

## Still open

- **Untested:** a coordinate system with a non-unit scale. The probe handles it
  arithmetically — `Matrix4` carries whatever scale the axes have — and `ModelExtent`
  renormalises the search directions before asking for an extreme point. Nobody has built
  one to check.
- **Assumed:** that `GetExtremePoint` is cheap enough to call six times per body on every
  keystroke in a stock field. It has not been measured; it has also not been noticed.
- Selections are **identified** by persistent reference and **resolved** by name: the
  reference from `IModelDocExtension::GetPersistReference3` says which name to ask
  `SelectByID2` for now, so a renamed coordinate system still resolves. See
  [Jobs](../design/jobs.md) for the migration of parts saved before references.

## Re-selecting an edge or a face: clear the selection first — **Verified (2025 SP3)**

Edges and faces have no names, so `SelectByID2` cannot address one and the persistent
reference is the only handle. `GetObjectByPersistReference3` returns the object; selecting
it means `IEntity::Select4`.

**`Select4` deselects an entity that is already selected**, whenever a PropertyManager page
with a selection list box is up. `IEntity::Select4`'s own Remarks say it, along with the
other half of the surprise: *"SOLIDWORKS ignores the Append argument because the selection
is always appended to the selection list."* So `Append: true` does not mean "add to what is
there" — it means nothing at all, and a second select is a toggle off. It returns false
when it does that.

That is what made operations look like they had forgotten their geometry. Closing the
Operation page used to leave its picks selected; reopening it then restored onto a live
selection, turned every contour back off, and left the box empty — while the references
themselves had resolved perfectly, so generation went on cutting exactly the right edges.
An empty box next to a working toolpath is the signature.

Two things follow, and G-CAM does both:

- **Clear the selection before restoring one** — `IModelDoc2::ClearSelection2(true)` at the
  top of the restore, so nothing can be a toggle-off.
- **Drop the page's selections when it closes**, as `JobPropertyPage` already did. They are
  the page's picks, not the user's, and leaving them behind is what made them stale.

The wrong first guess, recorded because it is a plausible-looking dead end: that the
`Resolve(...) as Entity` cast was returning null, since generation resolved the same
reference fine and only selecting failed. It was not — the cast succeeded. `IEntity` is
still the right type to cast to, being where `Select4` is declared, but it was never the
fault. What settled it was logging the three failure modes apart — reference gone, object
will not cast, select refused — which turned one ambiguous warning into a one-line answer.
