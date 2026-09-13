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
`IModelViewManager::ActiveFeatureManagerTabIndex` is a read/write property — get it
before showing the page, set it back in `AfterClose`. `GCamPropertyPage` does exactly
that, and deliberately restores *the tab that was active* rather than hunting for G-CAM's
own index: it needs no way to identify our tab, and it does the right thing when a page
was opened from somewhere else.

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

### Controls can only be added or changed while the page is closed — **From docs**

`AddGroupBox` and `AddControl2` both carry the same Remark: use them before the page is
shown or while it is closed. Changing a page from inside its own handler does not work,
which is why anything dynamic has to happen between shows.

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
`PageShown` / `PageClosed` in their place. That is not tidiness: the tab restore lives in
`AfterClose`, and a page that overrode it and forgot to call `base` would leave the user
stranded on the PropertyManager tab with no obvious cause.

## Not yet exercised

Two pages compile and are wired to the New Job and New Operation buttons, but **nothing
here has been run inside SOLIDWORKS yet** — no screenshot, no confirmed callback, and in
particular the tab round trip has not been watched happening. The `ref`/`out` finding and
the assembly-visibility finding are compile-time and file-inspection facts and stand on
their own; the rest is the help plus a reading of the interop. Re-tag the runtime claims
once the pages have actually been opened.
