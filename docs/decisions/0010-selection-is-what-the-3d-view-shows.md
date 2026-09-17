# 0010. Selection is what the 3D view shows, one row at a time

**Status:** Accepted
**Date:** 2026-09-16

## Context

Until now G-CAM drew a job's stock box, its origin triad and *every* generated toolpath it
held, and it did so whenever the job or any operation under it was selected. Since the tree
restores a selection after every rebuild, and a rebuild follows every edit and every
generate, that amounted to "always". On a part with three jobs and a dozen operations the
3D view was a permanent thicket: the stock box hid the walls the paths were cutting, and
the path being worked on was indistinguishable from the eleven that were not.

The obvious fix is to let the user say what they want to see. There are two established
ways for a CAM tree to do that, and they are not compatible: a per-item visibility state
the user toggles, or selection.

The constraint that shaped the answer is that **a WPF `TreeView` selects exactly one row
and cannot be talked out of it**. Anything that hangs off selection and needs more than one
row at a time therefore also needs a selection model of our own.

## Decision

**Selection decides what is drawn, and it says so a row at a time.** A selected job shows
its stock and its origin. A selected operation shows its toolpath and its job's origin, and
no stock. Nothing is drawn for a row that is not selected.

**The tree selects several rows**, with the gestures every Windows tree uses — click,
Ctrl-click, Shift-click for a range — so comparing two toolpaths means selecting both.

The rules are Core's: `MultiSelection<T>` in `Core/Selection` holds the click / Ctrl / Shift
behaviour, and `PreviewSelection` in `Core/Rendering` holds which of stock, origin and
toolpath each kind of selection asks for. `JobTreeViewModel` supplies the nodes and nothing
else.

## Alternatives considered

**Per-item visibility, HSMWorks-style** — a show/hide state on each job and operation,
toggled from the context menu or an eye icon, independent of selection. Rejected because it
is a second kind of state to keep: it has to be stored somewhere (the document? the
session?), it has to be decided for every newly created operation, and it goes stale — the
common complaint about it is a path you cannot find because you hid it three sessions ago
and forgot. Selection is state the user is already maintaining, continuously and visibly,
and it cannot be forgotten because it is on screen. This is not a permanent rejection: a
pinned "keep this visible" flag layered *on top of* selection would compose with what is
here, and would be the thing to build if the selection-only rule starts to chafe.

**Keeping the job's operations drawn when a job is selected** — the old behaviour, with
only the operation case narrowed. Rejected because it makes the job level the noisy one
again for exactly the case that prompted this, and because the rule is then not one rule but
two. "What is selected is what you see" needs no explaining.

**Single selection only** — the cheap version: no multi-select, and comparing two paths
means clicking one, then the other. Rejected on the user's call. Comparing a roughing pass
against the finishing pass that follows it is a routine thing to want, and holding one of
them in your head is not comparing them.

**A custom `TreeViewItem` control template** to get multiple selection, instead of turning
off the built-in highlight and drawing our own. Rejected as more surface than the problem
needs: a full template means owning the expander, the indentation and the focus visuals for
the life of the project, when the only thing wrong with the stock one is the colour of a
single border.

## Consequences

**The 3D view is now legible**, and an operation's toolpath can be looked at against bare
model geometry rather than through a translucent stock box.

**A selection now costs measurement per selected job.** Resolving a coordinate system and
measuring the model along its axes happens once per job in the selection rather than once,
which is why `PreviewSelection` groups by job and why `JobPreview` catches failures per job
rather than across the whole call.

**The tree has a selection model of its own to maintain.** WPF's own selection is still
there as `IsCurrent`, carrying focus and the keyboard; G-CAM's is `IsSelected`, drawn by the
row template. Two things called selection is a real cost, and the place it bites is that
anything driving WPF's selection programmatically must not let the resulting
`SelectedItemChanged` be read as a user click — `JobTreeView` guards three such cases.
**Re-entering that event took SOLIDWORKS down** on upward Shift-clicks, when a selection
rule answered "changed" wrongly; every selection change now goes through one guard.

**Every tree command had to grow a plural.** Generate, Suppress, Duplicate and Delete now
act on a selection, `IJobEditor.GenerateOperation` became `GenerateOperations`, and the
delete prompt has four shapes instead of two. Commands that genuinely cannot act on several
things — Edit, Rename, Make Default, New Operation — act on the anchor and are left out of
the menu entirely when more than one row is selected.

**The built-in selection highlight is suppressed by overriding four `SystemColors` resource
keys** inside the item style. That is a documented WPF technique rather than a supported
API, and if a future Windows theme stops honouring it the symptom is cosmetic: the current
row carries a second, differently coloured highlight.
