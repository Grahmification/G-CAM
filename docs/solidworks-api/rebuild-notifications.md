# Knowing when the model changed

G-CAM has to hear about a rebuild, because a toolpath cut from geometry that has since
moved is the one failure that reaches a machine. Everything here concerns
`GCam.SolidWorks/Events/PartRebuildWatcher`.

## `RegenPostNotify2`, and its obsolete predecessor — **From docs (2025 help)**

`IPartDoc` raises three related notifications:

| | Fires | Signature |
| --- | --- | --- |
| `RegenNotify` | *Before* a rebuild | `int()` — return `S_FALSE` to stop the rebuild |
| `RegenPostNotify` | After | `int()` — **"Obsolete. Superseded by `RegenPostNotify2`."** |
| `RegenPostNotify2` | After | `int(object stopFeature)` |

G-CAM takes `RegenPostNotify2`. The help says it "post-notifies the user program when a
part document has been **rebuilt or rolled back**", and `stopFeature` is what separates the
two: the `IFeature` immediately below the rollback bar for a rollback, and **null for a
rebuild**.

**Both are treated the same**, and `stopFeature` is ignored. Rolling the tree back past the
feature an operation cuts changes what that operation would produce just as surely as
editing the dimension would, so an add-in that only watched rebuilds would go quiet at
exactly the wrong moment.

`RegenNotify` is the wrong half of the pair here — it can veto a rebuild, which G-CAM has
no business doing, and it fires before the geometry it would be reacting to exists.

## It is a document notification, not an application one — **From docs**

`PartDoc` raises it, not `SldWorks`, and it carries no document. So there is **one
subscriber per open part**, held by that part's `DocumentTab`, exactly as `JobStorageHook`
is and for the same reason: a single shared handler would have to guess which part
rebuilt, and the obvious guess — the active document — is wrong whenever SOLIDWORKS
rebuilds something that is not in front.

## Do as little as possible inside it — **Assumed**

`PartRebuildWatcher` marks operations stale inside the notification, which is pure Core
and touches no COM, and **defers the redraw** — rebuilding the job tree and remeasuring
the model for the 3D scene — to a one-shot `System.Windows.Forms.Timer` at interval 1.

Tagged *Assumed*: nothing has been seen to go wrong doing the work inline, and it has not
been tried. It follows `GCamPropertyPage.RebuildAfterHandlerReturns`, which exists because
reshaping a page from inside a SOLIDWORKS callback *did* kill SOLIDWORKS repeatedly
([property-manager-pages.md](property-manager-pages.md)). Measuring bodies from inside
SOLIDWORKS' own rebuild is a smaller version of the same bet, and the deferral costs one
turn of the message pump.

One timer at a time. A rebuild arriving while one is pending needs no second redraw — the
first has not run yet and will see the same marks.

## Two things it deliberately does not do

**It does not mark the document dirty.** A rebuild that genuinely changed geometry has
already dirtied the part in SOLIDWORKS' own eyes, so the new `Stale` states are written by
the save it will prompt for anyway. Calling `SetSaveFlag` ourselves would mean a forced
rebuild of an *unchanged* part — <kbd>Ctrl</kbd>+<kbd>Q</kbd> on a part nobody has
touched — producing a spurious "Save changes?" on close. That rebuild's staleness is a
false positive we were happy to accept on screen and would not want to persist.

**It does not redraw when nothing changed.** `Staleness.ModelRebuilt` returns how many
operations it actually marked, and the watcher does nothing at all for zero. Nearly every
rebuild lands there — no operation generated yet, or everything already stale — and the
alternative is rebuilding the tree and the whole 3D scene on every <kbd>Ctrl</kbd>+<kbd>B</kbd>.

## Not yet verified — **2025 SP3**

The mechanism is unexercised on a machine as written. Worth confirming, in this order:

- **A dimension change marks operations stale**, dims the drawn toolpath and puts the red
  badge on the tree. This is the reported bug it exists to fix.
- **Opening a part does not.** SOLIDWORKS rebuilds on open under some conditions, and if
  that reaches us, every reopened part would show everything stale and the persisted state
  would be worth nothing. The watcher is subscribed *after* `LoadJobs` for this reason, but
  that only covers the load itself, not a rebuild SOLIDWORKS starts later.
- **Rolling the tree back and forward** — the `stopFeature` half, which no other path
  exercises.
