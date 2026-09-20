# Turning an edge into points

What it takes to get the points of one edge out of SOLIDWORKS, and the one rule that
silently gives you a different curve than the one you asked for.

Written while building `GCam.SolidWorks/Extraction/ContourExtraction` against 2025 SP3.

## `Sense` decides which way to ask, and an arc punishes getting it wrong — **Verified (2025 SP3)**

The obvious way to tessellate an edge is the documented one:

```csharp
var curve = edge.GetCurve() as Curve;
CurveParamData p = edge.GetCurveParams3();          // needs GetCurve first
var points = curve.GetTessPts(chordTol, lengthTol, p.StartPoint, p.EndPoint) as double[];
```

**That is wrong for any edge whose curve runs the other way.** `ICurveParamData.Sense` is
false when the curve and the edge are in opposite directions, and the help spells out the
consequence in its Remarks: *"StartPoint corresponds to the end of the edge, EndPoint
corresponds to the start of the edge."* Handing those to `GetTessPts` unswapped asks it to
travel from the end of the edge to its start.

A line does not care — the same segment comes back, its points in reverse. **An arc has
another way round, and takes it.** A 1mm-radius fillet spanning 1.85 rad came back as the
4.43 rad arc that is not on the part: measured 4.355mm of chords where the edge is
1.855mm long. On screen it reads as the highlight folding back across the corner, and it
reaches the toolpath, not just the preview — the path cuts a curve the model does not
have.

So the points must be asked for in the edge's own direction:

```csharp
object from = p.Sense ? p.StartPoint : p.EndPoint;
object to   = p.Sense ? p.EndPoint   : p.StartPoint;
```

Which is worth doing anyway: the tessellation then runs the way the edge does, so anything
that walks edges and anything that draws them agree about which end is which.

**Whether a given edge has `Sense` false is a modelling artefact**, not a property of the
geometry, which is what makes this so slow to find: the same fillet radius is fine on one
corner and folded on the next, and it looks like an angle or a tolerance problem. In one
test part every `LINE_TYPE` edge had `Sense` true and every `CIRCLE_TYPE` and
`ELLIPSE_TYPE` edge had it false.

*(A fillet across a corner that is not 90° meets its neighbouring faces in **elliptical**
arcs, so `ELLIPSE_TYPE` edges are ordinary fillet geometry rather than anything exotic.)*

## Check the length; wrong geometry looks exactly like right geometry

`ICurve::GetLength3(uStart, uEnd)` measures the curve itself between the edge's own
parameters — `UMinValue` and `UMaxValue` from the same `CurveParamData` — so it is
independent of whatever the tessellation decided to do. Comparing the two costs one COM
call per edge and turns this whole family of faults into a log line. `ContourExtraction`
does it on every edge it tessellates.

Be generous with the threshold: a tessellation is *meant* to come out shorter than its
curve, because the chords cut every corner, and by more at coarser tolerances. G-CAM
allows 10%. The wrong arc was out by 240%.

**Known open question.** One small `ELLIPSE_TYPE` edge still tessellates about 35% *longer*
than `GetLength3` says it is — 0.243mm against 0.18mm — which a chordal approximation
cannot do. Either those points are still not quite the right curve, or `GetLength3` is
unreliable for an ellipse. Not yet chased; the check reports it on every pick.

## Other things worth knowing

- **`GetCurveParams3` needs `GetCurve` to have been called first.** The help says so, and
  nothing complains if you skip it.
- **The curve you get back is already trimmed to the edge** and re-parameterised from 0,
  while `UMinValue`/`UMaxValue` are in the base curve's parameter space. The two do not
  line up, and `GetEndParams` reports the trimmed one.
- **Negative parameter space exists.** If `Sense` is false and `UMinValue` > `UMaxValue`,
  the help says to swap the two and negate them. Not yet seen here, so not yet handled.
- `LengthTolerance` filters out short segments; 1e-6 m is enough to drop the slivers a
  tessellator leaves at a tangency without touching a real feature.
