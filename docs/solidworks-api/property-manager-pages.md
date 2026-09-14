# PropertyManager pages

What it takes to put a native PropertyManager page on screen from an add-in, and the
places the API help and the interop assemblies disagree.

Written while building `GCam.SolidWorks/PropertyPages` against SOLIDWORKS 2025 SP3.

## A page cannot be hosted in your own Manager Pane tab — **From docs**

The obvious thing to want, once G-CAM has its own [Manager Pane
tab](manager-pane-tabs.md), is for the operation editor to appear *inside* that tab. It
cannot.

`ISldWorks::CreatePropertyManagerPage` takes a title, options, a handler and an error
code — no parent window, no pane, no tab. SOLIDWORKS owns the placement and always
renders the page on the PropertyManager tab; the help spells this out under
`ISWPropertySheet`: "A PropertyManager page is displayed when the PropertyManager tab is
selected on the left-side panel." The only placement control `Show2` offers is stacking
onto another page. In the other direction, `CreateFeatureMgrControl4` takes an ActiveX
control and nothing else, so a page cannot be pushed into our tab from that side either.

So the editing flow has to be a round trip through the Manager Pane's tabs:

```
G-CAM tab  ->  (edit)  ->  PropertyManager tab  ->  (OK/Cancel)  ->  G-CAM tab
```

**The last hop is the only one that is ours to arrange.**
`IModelViewManager::ActiveFeatureManagerTabIndex` is a read/write property — read it
before showing the page, set it back after the page has closed. `GCamPropertyPage`
deliberately restores *the tab that was active* rather than hunting for G-CAM's own
index: it needs no way to identify our tab, and it does the right thing when a page was
opened from somewhere else.

Related, if you ever do need our tab's index: `GetFeatureManagerTabs` returns the tabs
**right to left**, and there are `GetFeatureManagerTreeTabIndex`,
`GetPropertyManagerTabIndex`, `GetConfigurationManagerTabIndex`,
`GetDimXpertManagerTabIndex` and `GetDisplayManagerTabIndex` for the built-in ones. All
are 2017 and later.

The upside of losing this argument: a native page gets selection boxes that pick faces
straight out of the graphics area, unit-aware number boxes, and OK/Cancel/undo behaving
the way every other SOLIDWORKS command does. Re-implementing that inside our own tab
would be a large amount of work to arrive somewhere slightly worse.

## `ref` where the help says `out` — **Verified (2025 SP3)**

Two signatures in this area are documented as having output parameters and declared in
the interop as `[In, Out]`, so C# requires `ref` and rejects `out`:

```csharp
object CreatePropertyManagerPage(string Title, int Options, object Handler, ref int Errors)
bool   OnSubmitSelection(int Id, object Selection, int SelType, ref string ItemText)
```

Both produce `CS1620: Argument 4 must be passed with the 'ref' keyword`, which is at
least a compile-time failure rather than a silent one. Initialise the variable before
passing it; `ref` will not do it for you.

**Reflection agrees with the help, not the compiler.** Enumerating the interfaces with
`ParameterInfo.IsOut` reports these parameters as `out`, because `IsOut` reads the
marshalling flag rather than the C# calling convention. So dumping signatures by
reflection is an unreliable way to answer this particular question — the compiler is the
only authority. Everything else in these interfaces matched.

`OnPopupMenuItemUpdate(int Id, ref int retval)` is `ref` in both the help and the
interop, so it is not part of this.

## The handler interface is version 9, and a failed QI is nearly silent — **From docs**

`ISldWorks::CreatePropertyManagerPage` still documents its `Handler` parameter as
`IPropertyManagerPage2Handler5`. The current interface is
`IPropertyManagerPage2Handler9` (SOLIDWORKS 2012 and later) and implementing it
satisfies the older one.

If SOLIDWORKS cannot QueryInterface the handler, the page is still created and shown —
it just never calls back. The only signal is the `Errors` value:
`swPropertyManagerPage_UnsupportedHandler` (1). `GCamPropertyPage.Build` treats anything
other than `swPropertyManagerPage_Okay` as a failure for exactly that reason; a page with
dead buttons and no exception is a miserable thing to debug.

## COM visibility of the handler class — **Verified (2025 SP3), runtime path untested**

`GCam.SolidWorks` has no assembly-level `[ComVisible(false)]` — the SDK only emits that
attribute when the `ComVisible` MSBuild property is set, and it is not. Confirmed by
reading the generated `obj/Debug/net48/GCam.SolidWorks.AssemblyInfo.cs`. Its public types
are therefore COM-visible by default, which is why `PmpHandlerBase` needs no COM
attributes for SOLIDWORKS to query it. This matches the stock SOLIDWORKS add-in template,
whose handler classes carry no attributes either.

Note the difference from `GCam.AddIn`, which *does* declare `[assembly: ComVisible(false)]`
and opts `GCamAddin` in explicitly.

Two consequences worth keeping in mind:

- **A page handler must not have a public parameterless constructor.** `regasm` registers
  every COM-visible public class that has one, and the build regasms this assembly for
  the FeatureManager tab control. `GCamPropertyPage` takes its dependencies as constructor
  arguments, so it is not registrable.
- If that assembly-level attribute is ever added, the handler classes need
  `[ComVisible(true)]` or the callbacks stop arriving.

## Building the page

### Controls can only be added while the page is closed — **From docs**

`AddGroupBox` and `AddControl2` both carry the same Remark: use them before the page is
shown or while it is closed. A page cannot grow a control while it is on screen, which is
why G-CAM rebuilds a page per show rather than reshaping one — see the `Visible` section
below, which is the other half of that story and much the more dangerous half.

### A combobox's item list cannot change once the page is shown — **Verified (2025 SP3)**

**`Clear` and `InsertItem` each kill SOLIDWORKS outright** — instantly, on the first call,
with no exception, no log line, no Windows Error Reporting entry and no dump. Same family
as `IPropertyManagerPageControl.Visible` below, and worse: `Visible` at least takes about
four shows, these take one call. Two separate crashes, one per method.

`Clear`, `AddItems`, `InsertItem` and `DeleteItem` carry no Remark restricting *when* they
may be called, unlike `AddControl2`, which says plainly that a control cannot join a page
already on screen. **That silence is not permission.** The help does annotate what is legal
while displayed when it means to — `IPropertyManagerPage2.Title` says "whether the page is
displayed or not", `EnableButton` says "only after the page is displayed" — so an
unannotated member should be read as page-build-time only.

What is actually known, as opposed to inferred:

| On a shown page | |
| --- | --- |
| `Combobox.Clear` | **Fatal**, measured |
| `Combobox.InsertItem` | **Fatal**, measured |
| `Label.Caption` | **Fatal**, measured |
| `IPropertyManagerPageControl.Visible` | **Fatal**, measured (section below) |
| `Numberbox.Value`, `Combobox.CurrentSelection` | **Unknown** — assume fatal |

**Nothing written to a shown page has yet been seen to survive.** Two generalisations were
tried and both were wrong, at the cost of a crash each:

1. *"`Clear` is fatal because it invalidates the `CurrentSelection` index, so append with
   `InsertItem` instead."* `InsertItem` died in the same place.
2. *"Changing a control's structure is fatal; writing a value to a control that already
   exists is fine."* `Label.Caption` is exactly such a value write, and it died too — as
   `Visible` should already have warned, being an ordinary property on an existing control.

The working rule is therefore the blunt one: **a shown page's controls are read-only.**
Anything else needs measuring first.

**The design consequence is not a workaround, it is a constraint.** A control whose
*contents* must change while the page is up cannot be a combobox. Two wrong turns were
taken before this was clear: first rebuilding the list with `Clear` + `AddItems`, then —
on the theory that `Clear` was fatal because it invalidated the `CurrentSelection` index —
appending with `InsertItem` instead. Both died at the same place. The list is simply frozen.

So `OperationPropertyPage` shows the tool as a **label plus a Browse button**, HSMWorks'
own shape, which needs no list: Browse is what adds a tool to the part, so the set of tools
necessarily changes while the page is up, and no drop-down can survive that.

### Changing what a shown page shows: rebuild it

`GCamPropertyPage.RebuildAfterHandlerReturns` is the sanctioned way, and given the table
above it is the *only* way. It closes the page and shows it again, which re-runs
`BuildControls` and `LoadControls` with the page closed — the one state in which controls
can be written to at all.

Two details it gets right and a hand-rolled version would not:

- **Deferred by a one-shot `System.Windows.Forms.Timer`, never immediate.** Closing the
  page inside a handler leaves it gone when the handler returns to SOLIDWORKS, which the
  `LockedPage` remark says may crash. The timer moves the work to a later turn of the
  message pump, after the handler has returned normally.
- **The derived page never sees the close.** `AfterClose` skips `PageClosed` and the tab
  restore while rebuilding, so nothing is committed, the Manager Pane tab the user came
  from is preserved, and `LoadControls` repopulates from the clone being edited — edits in
  progress survive the rebuild.

This is cheap because these pages are rebuilt for every show already; building one is a
dozen API calls.

**How it was found**, since the symptom names nothing: the crash leaves no trace anywhere,
so the only evidence is which log line was written last. `BrowseForTool` was bisected with
a `Log.Debug` between every call. That works only because `shared: true` on the Serilog
file sink flushes per event, so the last line written is trustworthy even through a hard
kill — see `LoggingSetup.cs`. It is the technique to reach for next time; there is nothing
else to go on.

Cleared by the same runs, and worth knowing because all three were suspects:
`IModelDoc2::SetSaveFlag` from inside a live page handler, a modal WPF dialog shown from
inside `OnButtonPress`, and the nested message pump that comes with it.

### It is not a threading problem, and WPF is not involved — **Verified (2025 SP3)**

Worth writing down because it is the natural suspicion and it is wrong, and because the
same wrong turn was taken during the `Visible` investigation before it.

**There is no `IThreadSafe` in the SOLIDWORKS API.** Every "Thread" topic in the 2025 help
is a *cosmetic thread* — `ICThread`, `IThreadFeatureData`, `swThreadMethod_e` — screw
threads on a part, not CPU threads. Checked against the installed CHM.

**WPF does not run the dialog on another thread.** `Window.ShowDialog` pushes a nested
`Dispatcher` frame on the *calling* thread; a WPF Dispatcher is affinitized to the thread
that created the window, which here is SOLIDWORKS' main STA thread. There is no thread
switch and so nothing to marshal back from. See [wpf-in-solidworks.md](wpf-in-solidworks.md).

The evidence from the crashes themselves says the same thing four ways:

- Each crash happened **after** the WPF dialog had closed and returned, on a direct COM
  call to a page control.
- `IModelDoc2::SetSaveFlag` in the same handler, with the same dialog just dismissed, was
  fine.
- The fix — a deferred rebuild driven by a **WinForms** timer, no WPF anywhere — works.
- Each crash was **deterministic on the first call** of one named method. Threading faults
  are intermittent and move around; these did neither.

That last point generalises, and the `Visible` section below reached it first from the
opposite direction: *a deterministic-looking crash that moves when you change unrelated
things is a resource story, not a threading story.* A crash that reproduces on call one of
a specific method is an API-contract story. Neither is a threading story.

What genuinely is unsettled about WPF here is narrower and separate: re-entrancy from the
nested pump (SOLIDWORKS can deliver notifications while a modal dialog is up — untested),
modeless windows (Assumed, untested), and `ElementHost` keyboard and focus inside the
FeatureManager tab, which is a real problem but a different one. Threading proper only
starts to matter when toolpath calculation leaves the STA thread, which needs the
`SwDispatcher` that does not exist yet.

**A live `Visible` call is still in the codebase and is now a prime suspect.**
`JobPropertyPage.ShowControlsFor` calls it from inside `OnComboboxSelectionChanged` when
the stock mode changes — a shown page, the exact conditions that are fatal here. It is
listed below as not yet exercised, which is the only reason it has not bitten. Changing
stock mode on the Job page should be expected to crash until someone proves otherwise, and
the fix is the same one this page took: choose a shape that does not need the call.

### `AddControl2` hides controls you do not explicitly make visible — **From docs**

`swControlOptions_Visible` is mandatory with `AddControl2`. The older `AddControl` showed
the control regardless of the options, so the 2014 change turns a copied-in snippet into
a control that simply is not there. Pass `Visible | Enabled` unless you mean otherwise.

### Create the page locked — **From docs**

`swPropertyManagerOptions_LockedPage`. The help is unusually blunt: if the page is gone
when a handler returns control to SOLIDWORKS, SOLIDWORKS might crash, and some API calls
try to close the page. Locking also rules out stacking, which is no loss.

### `Show2` needs an active document; `CreatePropertyManagerPage` does not — **From docs**

Build the page whenever you like, but showing it with no document window returns
`swPropertyManagerPage_NoDocument` (-2). G-CAM turns that into a `GCamUserException`
reading "Open a part before creating a job" rather than a defect report — it is a thing
a user can do, not a bug.

`Show2`'s options parameter only defines `swPropertyManagerShowOptions_StackPage`, so a
page that does not stack passes a bare `0`.

## Most controls do not render their caption — **Verified (2025 SP3)**

`AddControl2` takes a `Caption`, and for a **number box, combobox, text box or selection
box it is accepted and never drawn**. The field arrives on the page with nothing naming
it. SOLIDWORKS' own "Create PropertyManager Page" example gives the game away: it passes
`caption = ""` for every one of those types.

A `swControlType_Label` immediately above the control is the only way to name it, which
is why `AddLengthField` on the job page always creates the pair together and shows and
hides them as one. `AddCombobox` does not even accept a caption any more, so the mistake
cannot be made silently.

Captions *are* drawn for checkboxes, options and buttons, where the caption is the
control's own text rather than a label for it.

A selection box has no caption at all, so where one needs naming the cheapest answer is
to give it its own group box and let the group header do the work - which is what the
job page's Coordinate system group is for.

## Selection boxes need a mark each — **From docs**

A selection box is `swControlType_Selectionbox` plus two settings that matter:

```csharp
box.Mark = 1;                                        // unique within the page
box.SetSelectionFilters(new[] { (int)swSelectType_e.swSelSOLIDBODIES });
box.SingleEntityOnly = false;
```

**The mark is how SOLIDWORKS decides which box a click belongs to**, and how you tell the
selections apart afterwards. A page with two boxes and one mark cannot separate them.
G-CAM's job page uses 1 for the model bodies and 2 for the coordinate system.

Useful filters so far: `swSelSOLIDBODIES` (76) and `swSelCOORDSYS` (61).

**What the box displays is not what you selected.** Clicking a coordinate system in the
graphics area can land on one of its parts, and the box then reads
`CoordinateSystem1\Point`. The object behind it is still the coordinate system feature —
`IFeature.Name` returns `CoordinateSystem1` — so only the display is wrong, which is worse
than it sounds because it looks like the wrong thing was picked.

**`OnSubmitSelection`'s `ItemText` fixes that**, and the help buries the fact in its last
Remarks line: *"ItemText is returned to SOLIDWORKS and stored on the selected object and
can be used by your PropertyManager page selection list boxes for the life of that
selection."* Return the feature's own name and the box shows it, whichever part was
clicked.

The same callback is where a selection is vetted: returning false refuses it exactly as if
the filter had, so the job page also requires an `IFeature` reporting
`GetTypeName2() == "CoordSys"`. It fires on every pre-select hover, so keep it cheap,
take no action that touches the model, and say nothing.

**Height is in dialog units, not pixels.** The help says so and it is easy to miss: `50`
is about three rows. A single-entity box wants roughly `14`. `MapDialogRect` converts if
you ever need real pixels.

**Read selections through `ISelectionMgr`, not the box.** `GetSelectedObjectCount2(mark)`
and `GetSelectedObject6(index, mark)` — one-based — are the pair that respect the mark.
Pass the mark to *both*; asking for a count with one mark and fetching with another
silently renumbers what you get.

**Read them as they change, not at OK.** `OnSelectionboxListChanged` is the moment the
contents are reliably present. By the time the page is closing the selection manager has
been cleared, so a page that waits until `AfterClose` to look finds nothing.

To put a saved selection back, `IModelDocExtension::SelectByID2` takes the name and a
type string — `"SOLIDBODY"`, `"COORDSYS"` — plus the mark, so a restored selection lands
in the right box.

**Clear the selection when the page closes.** A page that selects things to show the user
what it is editing owns those selections, and they outlive the page otherwise: a body
still lit up in the graphics area, a coordinate system still highlighted in the feature
tree. `JobPropertyPage.PageClosed` calls `ClearSelection2(true)` on every close reason,
and does it *after* committing — clearing can itself fire `OnSelectionboxListChanged`,
which would empty the values on their way out.

## `IPropertyManagerPageControl.Visible` is fatal after a few uses — **Verified (2025 SP3)**

**This is the most important thing on this page.** Setting `Visible` on a page's control
kills SOLIDWORKS outright — no exception, nothing in the log, no Windows Application
Error entry, the process simply goes. It does not fail the first time. In testing it took
**four shows of the page, reproducibly**, from any trigger, and the fault always landed on
the first `Visible` write of the fourth show.

Something accumulates; four was the limit here. The help documents no restriction of any
kind on this property.

So a page must not be reshaped on the way in. G-CAM's answer:

- **Pages are rebuilt for every show.** `GCamPropertyPage.Show` releases the previous page
  and calls `Build()` again. That is a dozen API calls and is not worth caching at this
  price.
- **Controls are created with the visibility they need**, by passing (or withholding)
  `swControlOptions_Visible` in `AddControl2`. Creating a control hidden is safe; hiding
  it later is not.
- `ShowControlsFor` still exists and still calls `Visible`, but is reached only when the
  user changes the stock mode on a live page. If that ever develops the same four-strikes
  limit, the answer is to close and reopen the page on a mode change.

### How it was found, because the symptoms were badly misleading

Five wrong diagnoses came first, each plausible, each reinforced by a correlation:

| Looked like | Actually |
| --- | --- |
| Showing a page from a WPF click handler | The click path just spent shows faster |
| A missing deferral — tried `OnIdleNotify`, WPF `Background`, `SystemIdle`, a WinForms timer | The call stack was never involved |
| Keyboard focus sitting in the hosted control | Moving it to the frame changed nothing |
| WPF `ContextMenu` popups | A WinForms menu crashed identically |
| The menu still dismissing as the page opened | Double-click crashed too, just later |

What broke it open was a count. Double-click worked three times and failed on the fourth —
and a failure that needs four attempts is not about *how* it is called, it is about
history. Every earlier theory had been explaining a correlation: the context menu costs an
extra show, so it tipped over on the first Edit rather than the fourth.

The lesson worth keeping: **ask how many times it takes before asking what is different
about the call.** A deterministic-looking crash that moves when you change unrelated
things is a resource story, not a threading story.

### Duplicate control ids are silently accepted

A related self-inflicted bug found on the way: two controls on the same page were given
id 21, because a label was added at `IdBodies + 1` and a constant elsewhere was also 21.
SOLIDWORKS reports nothing — the page just behaves oddly. Ids are per page; keep them in
one block and leave gaps.

## Populate the page before showing it, not from `AfterActivation` — **Verified (2025 SP3)**

`AfterActivation` fires from *inside* `Show2`, while SOLIDWORKS is still assembling the
page. Assigning to a combobox there fires its own `OnComboboxSelectionChanged`, and a page
that responds by reshaping itself is rearranging a page mid-build. The result was a page
that flickered and then displayed nothing.

`GCamPropertyPage.LoadControls` is called from `Show()` before `Show2`, and
`JobPropertyPage` guards its change callbacks with a `_loading` flag so a load cannot be
mistaken for the user typing. Only work that genuinely needs a live page belongs in
`PageShown` — restoring selections is the one case, because `SelectByID2` routes by mark
and the marks belong to selection boxes on a page that actually exists.

## Error handling

Every one of the thirty-seven handler methods is an entry point, and
`PmpHandlerBase` wraps all of them — see entry point 7 in
[error-handling.md](../error-handling.md). The defaults it chose, and why:

| Situation | On exception | Why |
| --- | --- | --- |
| `OnNextPage` / `OnPreviousPage` / `OnPreview` / `OnTabClicked` | return `true` | They ask permission to move. Refusing strands the user on a page that has already gone wrong |
| `OnSubmitSelection` | return `false`, `quiet` | A validation check that threw has proved nothing. Fires on every pre-select hover, so a dialog would be a modal storm |
| `OnKeystroke` | return `false`, `quiet` | Fall through and let SOLIDWORKS handle the key |
| `OnPopupMenuItemUpdate` | `retval = 0`, `quiet` | 0 is unchecked and greyed out. An item we cannot describe is one nobody should be able to pick |
| `OnActiveXControlCreated` | `swHandleActiveXCreationFailure_Continue` | Keep the page up with a missing control rather than cancelling it |
| `OnHelp` | return `false` | Lets SOLIDWORKS open its own help |
| everything else | swallow after reporting | |

`OnClose` fires while the page and its command are already closing and the help says an
add-in can do no real work there. Commit in `AfterClose`.

`GCamPropertyPage` seals `AfterActivation`, `OnClose` and `AfterClose` and offers
`PageShown` / `PageClosed` in their place, so a page cannot skip the Manager Pane tab
restore by overriding one and forgetting to call `base`.

## A length number box is in metres — **Verified (2025 SP3)**

The help documents neither `IPropertyManagerPageNumberbox.Value` nor `SetRange2`'s unit
parameter as being in any particular unit, which matters because getting it wrong is the
1000× error and would show up as stock a metre thick rather than as a crash.

Measured on a document in millimetres: typing **1 mm** read back as **0.001**. So the box
exchanges **metres** — SOLIDWORKS' system units — not the document's display units, even
though it shows and accepts mm. The conversion lives in `JobPropertyPage.ToBoxLength` /
`FromBoxLength` and nowhere else.

## What has been seen working

Run in SOLIDWORKS 2025 SP3 (revision 33.3.0) on 2026-09-12:

| | |
| --- | --- |
| Job page opens, values round-trip, OK commits and Cancel does not | **Confirmed** |
| Length boxes are in metres | **Confirmed** |
| Editing repeatedly from double-click, Enter and the context menu | **Confirmed** after the `Visible` fix |
| `Visible` on each show | **Confirmed fatal** — fourth show, every time |
| Page rebuilt per show, controls created pre-hidden | **Confirmed** |
| Selection boxes: bodies and coordinate systems | **Not yet exercised** |
| `Visible` on a user-driven stock mode change | **Not yet exercised** — the one remaining caller |
| A button control, and `OnButtonPress` reaching the page | **Confirmed** — Browse… opens the tool picker |
| A modal WPF dialog shown from inside `OnButtonPress` | **Confirmed** — the tool library browser, and the nested pump is fine |
| `IModelDoc2::SetSaveFlag` from inside a live page handler | **Confirmed** |
| `IPropertyManagerPageCombobox.Clear` on a shown page | **Confirmed fatal** — first call |
| `IPropertyManagerPageCombobox.InsertItem` on a shown page | **Confirmed fatal** — first call |
| `IPropertyManagerPageLabel.Caption` on a shown page | **Confirmed fatal** — first call |
| Numberbox `Value` and combobox `CurrentSelection` on a shown page | **Not yet exercised** — assume fatal |
| `RebuildAfterHandlerReturns` — deferred close and re-show | **Confirmed** — Browse rebuilds the Operation page, edits and selections survive |
