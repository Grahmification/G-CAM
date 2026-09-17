# Jobs

How the job model, the job tree and the 3D preview fit together. Read this before
touching `Core/Model`, `JobTreeViewModel`, the Job property page or `JobPreview`.

A **job** is what HSMWorks calls a Setup: it owns the model selection, the stock, the
coordinate system and the work offset, and operations sit directly inside it. There is no
Setup level — see [0004](../decisions/0004-jobs-own-operations-directly.md).

```
GCam.Core.Model
  JobDocument      the jobs of one part, and which is the default
    Job            model bodies, stock, coordinate system, work offset
      Operation    placeholder until the operation work
  Stock            three modes, computing a box from the model extent
  WorkOffsets      G54–G59
```

**`JobDocument` owns the rules, not the viewmodel.** Unique names, what the default is,
where the default goes when the job holding it is deleted, what a duplicate is called —
all of it is in Core, where headless tests reach it. The same reasoning that put
`ToolSearch` there. `JobTreeViewModel` turns that model into nodes and holds selection
and edit state, and nothing else.

**`Job` owns the same rules one level down.** `RemoveOperation`, `DuplicateOperation` and
`RenameOperation` sit beside `NextOperationName` for exactly the reason the job-level ones
sit on `JobDocument`: they are rules about the model, and there is no `GCam.UI.Tests`
project to reach them in a viewmodel. Operation names are unique **within a job**, not
across the part — that is the scope `NextOperationName` already numbers in, and two jobs
may each hold a "2D Contour1" without anyone being confused. A duplicate lands directly
after its original with a fresh id and no toolpath, mirroring `JobDocument.Duplicate`.

What the tree does *not* own: staleness. Deleting or duplicating an operation changes what
reaches everything below it, so the viewmodel asks `Staleness.OperationOrderChanged`, and
suppressing one asks `Staleness.OperationEnabledChanged`. Those rules live in
`Core/Generation`, which `Core/Model` cannot reference.

**Three things meet at the job tree, and none of them can see the other two.** The tree
is WPF in `GCam.UI`, which never references SOLIDWORKS. The property pages are in
`GCam.SolidWorks`, which does not know the tree exists. So the tree states intent through
`Core/Abstractions/IJobEditor`, and `GCamAddin` — the only project that knows everything
— implements it.

**Getting the viewmodel into the tab is not constructor injection.** SOLIDWORKS activates
`JobTreeTabHost` through COM, so it has a parameterless constructor and no dependencies.
`JobTreeTabs` recovers the instance with `IFeatMgrView::GetControl` straight after
creating the tab and calls `Bind` on the view inside it. One `JobDocument` and one
viewmodel per open part, held together in `JobTreeTabs`.

## The shape of the tree

```
📄 Bracket Operations        PartNode    — one, always there
  📁 Job1                    JobNode
    ⚙ 2D Contour1            OperationNode
```

**The part row is the tree's root and has no model behind it.** `JobDocument` is the part's
CAM data; the row is the viewmodel's own, named from SOLIDWORKS through a `Func<string>`
passed in by `JobTreeTabs` — GCam.UI cannot reach SOLIDWORKS, and asking again on every
rebuild is what makes a Save As follow with nothing subscribed to a rename. It offers New
Job and Generate All, draws nothing in the 3D view, and cannot be renamed, dragged or
deleted.

It is there for a part with no jobs too, which is why there is no "no jobs" message and no
heading above the tree any more: an empty part shows its own row, and that row can be
right-clicked to make the first job. A message could not.

**It does not fold away.** Collapsing it would hide every job in the part and leave one row
saying nothing. The refusal is `JobTreeNode.CanCollapse`, in the model, because there are
three ways to collapse a row — the arrow, <kbd>←</kbd>, double-click — and one rule beats
intercepting each of them. The arrow is then hidden as well, so nothing is offered that
would only snap back; that part is a visual-tree walk in `JobTreeView.OnRowLoaded` rather
than a replacement `TreeViewItem` template, which would mean owning the indentation, the
focus visuals and the selection highlight for the sake of one arrow.

## Selecting in the tree

**Several rows can be selected at once**, with the gestures every tree in Windows uses:
click, Ctrl-click to add or remove one, Shift-click for a range measured down the rows the
user can see. The rules themselves are `MultiSelection<T>` in `Core/Selection`, which knows
nothing about trees or nodes — headless tests reach it, and the mistakes it is there to
prevent (a range that runs the wrong way, an anchor left pointing at something no longer
selected) are exactly the ones a click-through misses.

**The anchor is deliberately two things at once**: where a Shift-click measures from, and
what a command that can only act on one thing acts on — Edit, Rename, Make Default, New
Operation. Those four are left out of the context menu entirely when several rows are
selected, rather than shown greyed; half a menu of dead rows reads as something being
broken. Generate, Suppress, Duplicate and Delete act on everything selected, and a
selection holding both kinds of row gets a third menu offering only Generate and Delete.

**A WPF `TreeView` selects exactly one row and cannot be talked out of it**, so G-CAM's
selection is the node's own `IsSelected`, drawn by the row template, and the built-in
highlight is turned off by overriding `SystemColors.HighlightBrushKey` and its three
relatives inside the item style. WPF's own selection is still bound, as `IsCurrent` — it is
what carries focus, arrow keys and scroll-into-view, and what a programmatic selection
moves. The two are not the same thing: Ctrl-click a selected row and it stays current while
ceasing to be selected.

The one trap in wiring that up: **the view drives WPF's selection itself in two places** —
restoring a selection after a rebuild, and focusing a right-clicked row — and each of them
makes `TreeView` raise `SelectedItemChanged` as though the user had clicked. Both would
collapse a multiple selection to one row. `OnTreeSelectionChanged` therefore ignores the
event while a modifier is held (the mouse handler owns those), while the view is driving
it, and when the row is already the anchor and already selected.

**Re-entering that event kills SOLIDWORKS**, so every selection change goes through
`JobTreeView.Apply`, which ignores the tree while one is being made. Changing the selection
writes `IsCurrent` — `TreeViewItem.IsSelected` — back onto the rows, so the `TreeView`
raises `SelectedItemChanged` inside the handler already running it, and from there into the
preview's COM calls. What exposed it: `MultiSelection.ExtendTo` reported a change it had
not made on *upward* Shift-clicks only, because it took its answer from `SelectAll`, which
settles the anchor on the first row of the range — the anchor itself only when the range
runs down. A selection rule that answers "changed" wrongly is not a wasted redraw here.

## Reordering

**Rows are dragged to reorder them** — an operation within its job or into another one, a
job among the jobs. A row dropped on its parent's own row goes first inside it: an
operation on a job, a job on the part. A job dropped on an operation is refused, because
the rows around an operation belong to a job that is not the one being moved, and the part
row does not move at all.

**The destination is "before this row", never an index.** `JobDocument.MoveOperation` and
`MoveJob` take the row the dragged one lands in front of, or null for last. Taking a row
out of its own list shifts every position after it, so an index would need a correction
that is wrong in one direction only — and it would live in a mouse handler, where no test
can reach it. Stated this way the correction is in Core, pinned by
`OperationReorderingTests`.

What a move invalidates: within a job, `Staleness.OperationOrderChanged`, because order
decides what stock an operation meets. Between jobs, that for the job it left, plus
`OperationEdited` in the one it joined — its toolpath was computed in the coordinate system
of the job it came from. It keeps that toolpath, which is the model's position throughout:
a path is the last thing the machine cut, and only `Staleness` says whether it can still be
trusted. An operation arriving under a name the destination job already uses is renamed on
arrival, because names are unique within a job and not across the part.

## The 3D preview

**What is selected is what is drawn, one row at a time.** Selecting a job shows its stock
and its origin; selecting an operation shows that operation's toolpath and its job's
origin, and no stock box to hide what it is cutting. Nothing appears for a row that is not
selected — a job no longer drags every toolpath it holds onto the screen, and an operation
no longer drags its job's stock.

Selection is the trigger because it is the one signal that means "this is what I am looking
at" — it covers clicking, arrowing through the tree, and the reselection after a refresh,
without any of them knowing a preview exists. It is stated in full each time rather than
patched, so a deselection, a cancelled page and a deleted job all amount to the same empty
call.

**That rule lives in Core**, as `PreviewSelection` in `Core/Rendering`: a builder takes the
jobs and operations a selection holds and groups them by job, saying for each whether its
stock is wanted and which of its operations are. Grouping is not tidiness — resolving a
job's coordinate system and measuring the model along its axes is the expensive half of
drawing anything, and everything drawn for that job needs exactly it.

`PreviewSelection` says what is **selected**, not what can be drawn. A suppressed operation
still belongs to it; `JobPreview` is what decides that a suppressed one draws nothing (it
will not be cut, and the greyed row says so) and that a stale one draws faded (it is still
the only picture of what the machine last did).

**A layer each, not one between them** — `stock:{job}`, `job-origin:{job}`,
`toolpath:{operation}`. A stock box that cannot be computed still leaves the origin on
screen, which is the half a user is more likely to be checking when the stock is wrong; two
selected operations draw without either knowing about the other; and one job failing to
measure costs only its own layers. `JobPreview` tracks the layers it has put up, because an
operation that has just been deleted still has one on screen and nothing left in the
document can name it.

The Job property page previews its *clone* as it is edited, so both follow what is being
typed and Cancel leaves nothing behind — `PreviewSelection.ForJob`, the same one job with
stock and origin that selecting it in the tree gives. The page is built once for the session
and finds the preview for whichever part is in front, which is why it takes a
`Func<IJobPreview>` rather than one instance.

The Operation page has no preview call of its own. It does not need one: it is opened from
the tree with its operation selected, so the toolpath on screen is already that operation's
and only that one. A live preview of the path being edited is a different feature, and is
listed as such in [operations.md](operations.md).

Three parts again, none of which can see the other two: `Core/Abstractions/IJobPreview`
is what the tree and the page both state intent through, and `JobPreview` in
GCam.SolidWorks is what answers it. The same arrangement as `IJobEditor`. Unlike
`IJobEditor` it is implemented in GCam.SolidWorks rather than in the add-in, because
everything it needs — the document, its bodies, its coordinate systems, its windows — is
COM, and `JobTreeTabs` already holds one per open part.

**The triad is sized proportionally, not screen-constant.** Its arms are 30% of the
largest dimension of the stock — or of the model, when the stock is not usable yet, since
a job with no stock set up is exactly when someone is checking where the origin is. That
keeps it legible on a 20mm part and on a two-metre one with nothing to configure, and it
costs nothing per frame because it is rebuilt only when the job changes.

The alternative, HSMWorks' constant apparent size, was considered and rejected *for now*:
it needs `IModelView.Scale2` and `FrameHeight` read per view, the geometry rebuilt on
every `ViewChangeNotify` — continuously, while rotating or zooming — and, because two
windows on one part can sit at different zooms, a scene per *view* rather than per
document. That last part is the real cost: it would change the shape of the renderer, not
just add a subscription. Revisit if the proportional triad turns out to be annoying in
practice.

**Rendering rides on the G-CAM tab's lifetime.** `JobTreeTabs.DocumentTab` owns the
document's `ViewportRenderer` and `JobPreview` alongside its viewmodel, and disposes
them in `Forget` *before* the tab's view goes — unsubscribing from a window needs the
window still to be there. The two cover exactly the same set of documents: a part with a
G-CAM tab is a part that can have jobs, and a job is the only thing there is to draw. This
does mean `JobTreeTabs` now has a second responsibility. When a third subscriber to the
document notifications appears — persistence, most likely — that is the moment to factor
`Events/` out of it.

## Persistence

**Jobs are saved inside the part** and come back when it reopens — verified on 2025 SP3 on
2026-09-13. How that works is in
[third-party-storage.md](../solidworks-api/third-party-storage.md); what gets written is
`Core/Persistence`. The one rule to remember while editing jobs: **an edit has to mark the
document dirty**, or SOLIDWORKS never offers the save that would write it, and the user
loses the work without being asked.

**A part with no jobs is never written to.** `JobDocument.HasNothingToStore` is the test,
and it counts jobs only — asking SOLIDWORKS for the storage node in order to write is what
*creates* it, so without this every part anyone opened and saved with the add-in loaded
picked up a few hundred bytes of empty G-CAM XML. The exception, and the reason the rule is
not simply "no jobs, no write": a part that already holds G-CAM data is always written,
so that deleting the last job survives a reopen.

## What a job points at

**Bodies and coordinate systems are `GeometryRef`s**, carrying SOLIDWORKS' own persistent
identity from `GetPersistReference3` alongside the name. `Job.ModelBodies`,
`Job.CoordinateSystem` and `OperationFrame.CoordinateSystem` all work this way, as the
geometry an operation selects already did.

Names alone were enough while a job lasted one session. They stopped being enough the
moment jobs began surviving a reopen: a stored name cannot tell a renamed body from a
different body that has since taken the old name, so a renamed body would silently change
what a proven job cuts.

**SOLIDWORKS is still addressed by name.** `SelectByID2` is the only route into a
PropertyManager selection box, so the reference's job is to say *which name to use now* —
resolve the id, ask the entity what it is called today, select that. A rename therefore
follows correctly rather than breaking.

### Migrating a part saved before this

A reference read from such a part has a name and no id, which is a usable state rather
than an empty one — `GeometryRef.IsEmpty` means "identifies nothing", not "has no id",
precisely so that a save cannot throw the names away.

`PersistentRefs.CurrentName` is where the migration happens, and it **mutates the
reference on purpose**: resolving by id updates the stored name after a rename, and
resolving by name stamps the id in. Anything that resolves a reference therefore also
migrates it, so an old part comes out modern the first time it is saved for any reason.

One asymmetry worth knowing: a reference that **has** an id which no longer resolves falls
back to nothing, not to the name. The entity is gone, and a new one may have taken its
name — selecting that would be the silent repointing this whole change exists to
prevent.
