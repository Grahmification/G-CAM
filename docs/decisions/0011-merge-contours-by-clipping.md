# 0011. Merge contours by clipping each path against what the others forbid

**Status:** Accepted
**Date:** 2026-09-26

## Context

A 2D contour operation offset each selected contour on its own. With several contours at
one depth that cuts wrongly in ways HSMWorks does not. The inner of two concentric bosses
was cut along a path running through the outer boss's material. Two overlapping bosses
were each cut right round, through each other. The user checked HSMWorks' behaviour in
each case: it merges the paths into their combined outline, which sometimes looks like
dropping one.

The contours can be closed and cut outside (bosses), closed and cut inside (pockets), or
open — an open contour has a cutting side but no enclosed material. Any rule has to handle
all three, mixed, and leave an operation whose contours do not interact exactly as it was.

## Decision

**Clip each contour's cutter path against what the other contours at the same depths
forbid, then join what is left head to tail.** What a contour forbids depends on what it is
and, for a pocket, on what is asking:

| Contour | Forbids |
| --- | --- |
| Boss | Its grown shape |
| Pocket | To another pocket, its shrunk shape; to anything else, a band either side of its wall |
| Open | A band either side of it, rounded past its ends |

The pieces join without being reversed, because every path keeps the material on the same
hand. The clipping is Clipper2's, behind `IContourOffsetter`. The same regions, gathered
into one, are what a lead is checked against. See
[Several contours at one depth](../design/operations.md#several-contours-at-one-depth).

## Alternatives considered

**One combined region for the whole group**: the allowed area is the union of the pockets'
shrunk shapes (or the whole plane), less every boss's grown shape and every open contour's
band, and the toolpaths are its edges. It is a single boolean and gives the merged
outline directly, with no joining. Rejected on two counts. **A pocket then keeps every
cutter inside it**, so a boss elsewhere on the part — outside the pocket — is never cut.
Fixing that means giving pockets a local rule, which is the table above. And an open
contour's band has edges that are not toolpaths — its material side, its ends — so they
would have to be filtered back out by matching region edges against real cutter paths,
which is fragile wherever edges coincide.

**Drop a colliding path, without merging** — the first design, and half the size: test
each path against the others' material and discard it. Rejected once the user checked
HSMWorks: overlapping bosses are cut as one path round both, not as one boss with the
other missing.

**Pairwise curve intersection, joining at the crossings**, with no regions. Rejected
because a path lying wholly inside another boss — the concentric case — crosses nothing,
and it is the case that started this.

## Consequences

- **An operation whose contours do not interact is unchanged.** A contour whose bounding box
  nothing comes near is skipped before any region is built, and a path nothing takes from
  comes back as itself, vertex for vertex.
- **A new kind of contour needs a row in the table**, and a column: a pocket already
  forbids different things to different kinds, and anything else may too.
- **An open contour protects only a band.** Material far out on its material side is not
  known, so a path there is cut. HSMWorks has the same limit.
- **The pocket rule toward bosses is assumed, not measured.** A boss beside a pocket is kept
  off the pocket's wall but not out of the pocket's surroundings. If HSMWorks does something
  else there, this is the row to change.
- **Coincident paths are ambiguous.** A path lying exactly on another's region boundary is
  on the edge of the clip, where Clipper2 may keep or drop it. The tests avoid coincident
  walls on purpose; a real part could have them.
- **The work is quadratic in the contours at one depth**, mitigated by the bounding-box skip.
  Tens of contours are fine. Thousands would want a spatial index.
