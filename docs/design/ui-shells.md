# UI shells

The four custom UI surfaces, what hosts each one, and the hosting constraints that shaped
them. Read this before adding a surface or moving one between technologies.

| Surface | Built by | Notes |
| --- | --- | --- |
| CommandManager tab, toolbar and menu | `GCamAddin.CommandManager.cs` | Five buttons: New Job, New Operation, Tool Library, Post Process, Simulate |
| Manager Pane tab | `JobTreeTabs` → `JobTreeTabHost` → `JobTreeView` | ActiveX → WinForms → ElementHost → WPF; selecting it brings the G-CAM ribbon tab forward |
| Job and Operation PropertyManager pages | `GCamPropertyPage` → `JobPropertyPage` / `OperationPropertyPage` | SOLIDWORKS-native, built by the API rather than WPF. Both are real and both work. The Operation page is five tabs — see [Operations](operations.md) |
| Tool library window | `ToolLibraryDialog.ShowBrowser` / `PickTool` → `ToolLibraryWindow` | Modal, parented to the SW frame. One window, two modes — browse, or choose a tool for an operation |

Post Process and Simulate are still deliberate no-ops.

**The tool library window doubles as the tool picker**, and the difference is one
delegate: `PickTool` subscribes to `ToolActivated`, which is what makes double-clicking a
row choose it and close rather than open the editor. OK also chooses, taking whichever row
is selected; Cancel, Escape and the close box choose nothing, because returning the
last-highlighted row would assign a tool nobody agreed to.

What comes back is **checked out**, not handed over — `ToolLibrary.CheckOut` copies the
tool and stamps `SourceLibraryId` ([0003](../decisions/0003-jobs-embed-their-tools.md)).
The window's tools belong to the open library, so a part that kept one would edit the
library every time somebody changed the operation's cutter.

The Operation page reaches this through a `Func<Tool>` supplied by `GCamAddin.Tools.cs`,
because the page is in `GCam.SolidWorks` and the browser is WPF in `GCam.UI`. Neither
project can see the other; the add-in is the only one that sees both. See
[Operations](operations.md) for what happens to the tool once it arrives.

**The toolbar buttons act on the default job**; the job tree's context menu is where a specific job or operation is reached, and it is the richer route. Post Process and Simulate do nothing at all yet.

**There are three menus, chosen by what is selected** rather than by what was clicked, and
built fresh on each right-click so enablement cannot go stale:

| Selection | Items |
| --- | --- |
| One job | Edit…, Rename, New Operation…, Generate, Duplicate, Make Default, Delete |
| Several jobs | Generate, Duplicate, Delete |
| One operation | Edit…, Rename, Generate, Suppress, Duplicate, Delete |
| Several operations | Generate, Suppress, Duplicate, Delete |
| Both kinds at once | Generate, Delete |

A menu per kind rather than one that hides rows, because the two share almost nothing
beyond the verbs: Make Default and New Operation mean nothing on an operation, and Suppress
means nothing on a job.

**The commands that can only act on one thing are left out when several rows are
selected**, not shown greyed — half a menu of dead rows reads as something being broken.
Generate is greyed out when every selected operation is suppressed, since `GenerationQueue`
skips those; offering a command guaranteed to do nothing is worse than not offering it.
Suppress is one command over the whole selection rather than a toggle each: anything still
running gets suppressed, and only when none of them is does it turn into Restore, because a
per-row toggle would leave a mixed selection in a state nobody asked for.

**The row that was right-clicked comes from `OriginalSource`, never from `sender`.**
Right-click selects the row before the menu opens, and that handler is on
`PreviewMouseRightButtonDown` — a **tunnelling** event, which runs root-first. An
operation's parent job therefore gets it first, and `e.Handled` stops the tunnel before
the operation's own item is reached, so `sender` is the *outermost* row rather than the
one under the cursor. That put the selection on the job and gave you the job's menu.
`OriginalSource` is the element the input system hit-tested whichever way the event is
routed, so the innermost `TreeViewItem` above it is always the right row. Worth knowing
because the obvious reading — that handling the event stops it reaching the parent — is
true of bubbling events and exactly backwards here.

**Rows drag to reorder**, and a drag moves the row under the cursor rather than the
selection — a drag says which row it is about, unlike a menu command. It starts only once
the pointer passes Windows' own drag threshold, so a shaky click stays a click, and the
insertion line is drawn by the row itself (`JobTreeNode.Drop`) rather than by an adorner.
The left-button handler that starts it is on the `TreeView` and not on the rows, for the
tunnelling reason below: on the rows it fires once per row in the chain.

**Delete acts on what is selected, and never on the job above it.** An operation node
still stands in for its job in New Operation, and used to in the 3D preview as well; Delete
is the one place where that would be destructive. It was: <kbd>Del</kbd> on an operation
deleted its entire job, silently, because `DeleteSelected` read `SelectedJobNode` like
everything around it.

**A right-click inside the selection leaves it alone**, which is what makes "select three
operations, right-click, Delete" mean the three. Only a click on a row that is not selected
replaces the selection with it. One question covers the whole selection, naming what is
about to go — a name for a single job or operation, counts past that, because reading eight
names back is not a check. There is no undo — `Core/Commands` is unbuilt — and a delete
takes generated toolpaths with it.

**Every tree edit ends in `IJobEditor.DocumentChanged()`.** The tree mutates Core objects
directly, with no property page involved, so nothing on those paths would otherwise reach
`IModelDoc2::SetSaveFlag`; SOLIDWORKS would then never offer the save that writes the
document's storage, and the edit would be gone at close with nobody asked. Delete,
Duplicate, Rename, Make Default and Suppress all go through it. The pages have always
marked dirty on commit — the tree did not, so every job deleted or renamed before this
was lost unless something else happened to dirty the part.

**The Manager Pane tab is where jobs live** — the icon strip beside the
FeatureManager design tree and the PropertyManager, the same place HSMWorks puts its CAM
tree. A tab belongs to a *document*, not to the application, so `JobTreeTabs` subscribes
to `ActiveModelDocChangeNotify` and `FileCloseNotify` and re-syncs the set of tabs
against the set of open parts on every notification; `EnumDocuments2` catches up with
documents that were already open when the add-in connected. One call at connect time is
the obvious implementation and produces no tab at all, because add-ins connect before any
document exists — see
[manager-pane-tabs.md](../solidworks-api/manager-pane-tabs.md), which also covers which of
these COM objects may be released and which must not.

**`Events/` now exists**, holding `PartRebuildWatcher` — the per-document subscriber that
marks operations stale when the part is rebuilt (see
[rebuild-notifications.md](../solidworks-api/rebuild-notifications.md)). It was the third
subscriber, which is the trigger this note had always named for factoring an events layer
out of `JobTreeTabs`.

**Only the new one moved.** `JobTreeTabs` still subscribes directly to
`ActiveModelDocChangeNotify` and `FileCloseNotify`, and `JobStorageHook` still owns the
storage notifications. Those two are application-level and document-level respectively and
work; moving them is a refactor with no behaviour change, and it can happen when something
needs it to rather than for tidiness. What the folder settles now is where the *next*
subscription goes.

**Property pages are rebuilt for every show, not cached.** Reusing one means reshaping it
before each show, and reshaping means `IPropertyManagerPageControl.Visible`, which kills
SOLIDWORKS after a handful of uses. Building is a dozen API calls; do not "optimise" it
back. The job tree's context menu is WinForms for historical reasons only — it was swapped
during that investigation and WPF turned out to be innocent.

**Editing happens on a PropertyManager page, which cannot live inside the G-CAM tab.**
`CreatePropertyManagerPage` takes no parent and no pane — SOLIDWORKS always renders a
page on the PropertyManager tab. So editing is a round trip:
`G-CAM tab → PropertyManager tab → OK/Cancel → G-CAM tab`. Only the last hop is ours;
`GCamPropertyPage` makes it by recording `ActiveFeatureManagerTabIndex` before showing
the page and setting it back after it closes. Details and the alternatives that were
weighed are in
[property-manager-pages.md](../solidworks-api/property-manager-pages.md).

## Toolbar icons and callbacks

**Icons.** `ICommandGroup.IconList` wants a *strip* per size containing every button's icon side by side; `MainIconList` wants a single icon per size. Both need files at 20/32/40/64/96/128 px. Placeholders are generated by `tools/make-placeholder-icons.py` — the button order there must match the `GCamCommand` enum, since a command's value is its index into the strip. The files are copied next to the assembly because SOLIDWORKS reads them from disk by path, not as embedded resources.

**Callback strings are resolved by name at runtime**, so a typo fails silently rather than at compile time. `AddCommandItem2` is passed `nameof(OnCommand)` to keep them honest, and the callbacks must stay `public` on the add-in class registered via `SetAddinCallbackInfo2`.
