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

## The 3D preview

**Selecting a job in the tree is what shows it.** Selection is the one signal that means
"this is the job I am looking at" — it covers clicking, arrowing through the tree, and the
reselection after a refresh, without any of them knowing a preview exists. An operation
node stands in for its job here as it does for the context menu, so drilling into a job
does not make its stock disappear.

What appears is the stock as a translucent yellow box and the job's coordinate system as a
red/green/blue triad at its origin — **two scene layers, not one**, so a stock box that
cannot be computed still leaves the origin on screen. That is the half a user is more
likely to be checking when the stock is wrong.

The Job property page previews its *clone* as it is edited, so both follow what is being
typed and Cancel leaves nothing behind. The page is built once for the session and finds
the preview for whichever part is in front, which is why it takes a `Func<IJobPreview>`
rather than one instance.

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

## Not yet persisted

**Jobs live only as long as the document is open.** That is what lets a job name its
bodies and coordinate system as plain strings; when persistence lands, those become
persistent reference ids from `IModelDocExtension::GetPersistReference3`, because renaming
a body must not silently change what a proven job cuts. The comment at
`Job.ModelBodyNames` says so at the point it matters.

The persistence constraints themselves — when SOLIDWORKS lets you write, and what it does
not let you do — are under "Rules with teeth" in
[architecture.md](../architecture.md).
