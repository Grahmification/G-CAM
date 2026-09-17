# Tabs in the Manager Pane

How to get a tab into the left-hand Manager Pane — the icon strip that holds the
FeatureManager design tree, the PropertyManager, the ConfigurationManager and the
DisplayManager. It is where HSMWorks and CAMWorks put their CAM trees, and where G-CAM
puts its job tree.

Learned building `GCam.SolidWorks/Hosting/JobTreeTabs.cs` against SOLIDWORKS 2025 SP3.

## The tab belongs to a document, not to the application — **From docs**

`CreateFeatureMgrControl4` hangs off `IModelDoc2::ModelViewManager`, so there is no such
thing as adding the tab once at start-up. Every document needs its own call, and the tab
dies with the document.

This is the trap that cost G-CAM its tab entirely. The original code did:

```csharp
var model = _swApp.ActiveDoc as ModelDoc2;
if (model == null) return;          // ← almost always taken
```

Add-ins connect while SOLIDWORKS is still starting, normally with **no document open at
all**, so the early return fired every time and the tab was never created — and because
nothing was retried when a part was later opened, it never appeared at all. There was no
error; the code did exactly what it said.

The fix is to treat it as a set to keep in sync rather than a one-off call: on every
document notification, compare the open documents against the tabs being held and add or
drop the difference. `ActiveModelDocChangeNotify` alone covers open, new, activate and
close, and being idempotent means a duplicated or missed event costs nothing.

**An add-in can also be switched on mid-session** with a dozen parts already open, so the
same sync has to run once at connect. `ISldWorks::EnumDocuments2` is the way to walk
them.

## `swFeatMgrPaneBottom` is the value for an ordinary tab — **From docs**

Reading the enum alone suggests you are choosing half of a split FeatureManager, which is
not what you want. The Remarks on `CreateFeatureMgrControl4` settle it:

> To add a tab to the FeatureManager design tree, specify WhichPane with
> `swFeatMgrPane_e.FeatMgrPaneBottom`.

The top/bottom distinction only means anything once the tree has been split.

## `EnumDocuments2` returns more than the user has open — **From docs**

It includes documents loaded as references — the parts inside an open assembly — which
have no window and no Manager Pane. The help points at `IModelDoc::Visible` to tell them
apart, and G-CAM filters on `Visible` plus a document type of `swDocPART`.

Note the asymmetry in `JobTreeTabs.Sync`: tabs are *added* only for visible parts but
*pruned* against every open document, visible or not. Pruning on the same filter would
delete a part's tab the moment it stopped being visible rather than when it closed.

## Releasing the tab takes `DeleteView`, not just a release — **From docs**

`IFeatMgrView::DeleteView` is what removes the tab. Calling `Marshal.ReleaseComObject` on
the `FeatMgrView` without it drops our reference and leaves the tab sitting in the
document with a dead control behind it. Do both, in that order.

**Open question: `DeleteView` throws during `DisconnectFromSW`** — *Verified (2025 SP3),
cause unknown*. Every session end so far logs:

```
Unhandled exception in Forget
System.Runtime.InteropServices.SEHException (0x80004005): External component has thrown an exception.
   at SolidWorks.Interop.sldworks.FeatMgrViewClass.DeleteView()
```

It is caught and the COM object is still released, so the unload completes — but the tab
is presumably *not* deleted, which would leave a dead G-CAM tab behind when the add-in is
disabled without SOLIDWORKS restarting. Not yet investigated. Candidates: the hosted
ActiveX control is already being torn down by the time we ask; or `DeleteView` needs the
owning document to be active. Worth checking whether deleting the tabs earlier — while
the documents are still fully alive — avoids it.

## Do not release the `ModelDoc2` — **Reasoned, not measured**

The house rule is to release COM objects explicitly rather than leave them to the GC, and
`JobTreeTabs` does that for the `FeatMgrView` and the `IEnumDocuments2` enumerator, both
of which it created.

It deliberately does not for `ModelDoc2` or `ModelViewManager`. Those wrappers are not
ours — the CLR hands out one runtime callable wrapper per COM identity, so it is the same
wrapper SOLIDWORKS' own managed code and every other add-in is using. `ReleaseComObject`
severs that one shared wrapper, and the next person to touch the document gets a
`InvalidComObjectException`. Release what you created; borrow everything else.

The same RCW-per-identity guarantee is what makes `Dictionary<ModelDoc2, FeatMgrView>`
work — the same document is the same key however you reached it. The stock SOLIDWORKS
add-in template keys its open-document table the same way.

## `IEnumDocuments2::Next` takes `ref`, not `out` — **Verified (2025 SP3)**

```csharp
void Next(int Celt, out ModelDoc2 Rgelt, ref int PceltFetched)
```

The help documents `PceltFetched` as an output parameter and reflection agrees — but the
interop declares it `[In, Out]`, so C# requires `ref` and rejects `out` with
`CS1620: Argument 3 must be passed with the 'ref' keyword`. Initialise the variable
first; `ref` will not.

Worth knowing generally: **`ParameterInfo.IsOut` reports these as `out`**, because it
reads the marshalling flag rather than the C# calling convention. Dumping interop
signatures by reflection is not a reliable way to answer this question — only the
compiler is.

## `IModelDoc2.GetType()` hides `object.GetType()` — **Verified (2025 SP3)**

```csharp
var docType = (swDocumentTypes_e)model.GetType();   // compiles, returns swDocPART etc.
```

`IModelDoc2` declares its own parameterless `GetType()` returning an `int`. When the
static type is the interface, C# member lookup finds the interface member and never falls
through to `object.GetType()`. It reads like a bug and is not one, which is worth a
comment at every use.

## The tab goes missing silently if the control is not registered — **From docs + prior experience**

`CreateFeatureMgrControl4` activates the ActiveX control by ProgID, so
`GCam.SolidWorks.dll` has to be COM-registered — not just `GCam.AddIn.dll`. If it is not,
the method returns `null`, and that is the entire diagnostic: a working toolbar and a
missing tab.

`JobTreeTabs.ReportMissingControl` turns that null into a `GCamUserException` naming
`deploy\register.cmd`, reported **once per session** rather than once per document — with
six parts open the same unfixable problem would otherwise be announced six times.

## Making the ribbon follow the tab — **Verified (2025 SP3)**

Selecting the G-CAM tab in the Manager Pane brings the G-CAM CommandManager tab forward,
so the toolbar matches what the pane is showing. Two API pieces:

- `PartDoc::FeatureManagerTabActivatedNotify(int CommandIndex, string CommandTabName)`
  fires whenever the active Manager Pane tab changes. It is a **document** event, so it
  is subscribed per part, alongside the tab itself.
- `ICommandTab::Active` is read/write. Setting it true selects that ribbon tab.

**The same notification is what tells G-CAM it has lost the pane** — the name simply does
not match. The job tree puts its selection away then, which takes the 3D preview with it;
see [jobs.md](../design/jobs.md). The one exception is a G-CAM property page, which moves
the pane onto the PropertyManager's tab itself: `GCamPropertyPage.AnyOpen` says so, and it
is raised before `Show2` rather than from `AfterActivation`, because the order SOLIDWORKS
raises those two in is not documented. **Assumed**, not measured.

Two things about the `IFeatMgrView` route, which looks like the obvious one and is not:
`ActivateNotify` and `DeactivateNotify` are documented as firing only for views made with
`CreateFeatureMgrView2`, and G-CAM's tab comes from `CreateFeatureMgrControl4`. Untested
here — the document notification was already subscribed and already proven, so there was no
reason to find out.

### `CommandTabName` is the tooltip you passed

The help's parameter descriptions for this delegate are copied and wrong — both
`CommandIndex` and `CommandTabName` are documented as "Index of the active tab in the
Manager Pane". Observed values:

```
Manager Pane tab activated: index 5, name 'G-CAM jobs, setups and operations'
Manager Pane tab activated: index 0, name 'FeatureManager Design Tree'
Manager Pane tab activated: index 6, name 'CAMManager'
```

So the name is the **tooltip string passed to `CreateFeatureMgrControl4`** — the only
human-readable string SOLIDWORKS was ever given for the tab, since the call has no title
parameter. The built-in tabs report their own display names. `JobTreeTabs.IsOurTab`
matches case-insensitively on `"G-CAM"` as a substring, which is robust to the tooltip
being reworded.

Note that indices are not stable across installations — index 6 above is CAMWorks, which
another machine will not have. Match on the name, not the index.

### `ICommandTab.Active` tracks visibility, not selection — **Verified (2025 SP3)**

This one cost three round trips, so it is worth stating plainly: **`Active` is not "is
this the tab on screen", and must never be used to guard the assignment.**

The help says only "Gets or sets whether this CommandManager tab is active". Observed, on
a G-CAM tab that was *not* the selected ribbon tab in any of these cases:

```
visible true,  active before true,  after true
visible false, active before false, after false
```

`Active` mirrored `Visible` every time. So the natural-looking

```csharp
if (!tab.Active) tab.Active = true;      // never runs when the tab exists
```

skips the assignment in exactly the case where the work is needed, and runs it only when
the tab is hidden — where setting `Active` does not show the tab but *does* throw the
ribbon to the first tab, Features. The symptom is perfectly inverted from the cause:
nothing happens when the tab is enabled, and something wrong happens when it is disabled.

Assign unconditionally, and guard on `Visible` instead — a user who has hidden the G-CAM
tab should not be dragged to Features for their trouble.

### The `OnIdleNotify` deferral was unnecessary — **Verified (2025 SP3)**

There is a wrong turn recorded here because it is an easy one to take twice. When the
ribbon first refused to follow the tab, the theory was that SOLIDWORKS discards UI
changes made part-way through its own Manager Pane update, and the fix was a one-shot
`ISldWorks::OnIdleNotify` subscription that applied the change once SOLIDWORKS was idle.

**That theory was never confirmed, and the real cause turned out to be the `Active` guard
above.** Once the guard was fixed the ribbon followed correctly — though at that point
the deferral was still in place, so what was proven is that the guard was the bug, not
that the deferral was doing nothing.

It has since been removed, and `ActivateCommandTab` now sets `ICommandTab.Active`
directly from inside `FeatureManagerTabActivatedNotify`. That has been in constant use
across many sessions since without the ribbon failing to follow — not a deliberate
regression test, but a lot of evidence. If it ever does stop following, **this is the
first thing to suspect**, and the deferral is in this file's history.

**A postscript, and a correction.** Deferral was later reintroduced across the add-in on
the belief that showing a PropertyManager page from this tab's hosted control was unsafe.
That belief was wrong. Pages were crashing SOLIDWORKS for an entirely unrelated reason —
`IPropertyManagerPageControl.Visible`, see
[property-manager-pages.md](property-manager-pages.md) — and deferral never fixed it in
any of its four forms. All of that machinery has since been removed; pages are opened
directly from the tree, exactly as they are from the toolbar.

So the conclusion originally recorded here stands after all: the deferral was
unnecessary. It is worth knowing that it was removed, reinstated on a misdiagnosis, and
removed again — a reminder that "the symptom changed when I added this" is not evidence
that the thing you added was right.

Worth keeping in mind before reaching for `OnIdleNotify` in general: it fires
continuously, so any subscription has to be one-shot, and that is a lot of moving parts
to add on a hunch.

The callback into the ribbon is an `Action` handed to `JobTreeTabs` by `GCamAddin`, not a
direct call: the CommandManager belongs to `GCam.AddIn`, and `GCam.SolidWorks` does not
reference it.

## What has and has not been seen working

Run in SOLIDWORKS 2025 SP3 (revision 33.3.0) on 2026-09-12:

| | |
| --- | --- |
| Tab appears on an open part | **Confirmed** — `G-CAM tab added to …Test CAM Part.SLDPRT` |
| `FeatureManagerTabActivatedNotify` fires, name matches the tooltip | **Confirmed** |
| The same notification firing when the pane moves *off* the G-CAM tab | **Not verified** — what the tree's selection clearing rests on |
| Whether it fires before or after `AfterActivation` when a page opens | **Not verified** — sidestepped by counting the page before `Show2` |
| `ICommandTab.Active` mirrors `Visible`, not selection | **Confirmed** — logged in both states |
| Setting `Active` on a *hidden* tab | **Confirmed harmful** — jumps the ribbon to Features |
| Setting `Active` on a *visible* tab selects it | **Confirmed working** — with the idle deferral still in place |
| Doing so from inside the notification, no deferral | **Not retested** — the deferral was removed after that confirmation |
| `DeleteView` on disconnect | **Confirmed broken** — throws SEHException every session |
