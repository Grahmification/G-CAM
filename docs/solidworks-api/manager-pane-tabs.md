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

## Not yet exercised

Written and compiling, but **not yet run inside SOLIDWORKS** — no tab has been seen
appearing, and the notification-driven sync has not been watched working. The `ref`/`out`
and `GetType` findings are compile-time facts and stand; the reasoning about why the old
code produced no tab is read off the source and the documented API, not off a debugger.
Re-tag once it has actually been loaded.
