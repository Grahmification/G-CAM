# Storing data inside a SOLIDWORKS document

How G-CAM keeps a part's jobs, operations and toolpaths inside the `.sldprt` itself, and
the rules SOLIDWORKS imposes on doing it.

## The store, not the stream — **From docs (2025 help)**

There are two third-party storage APIs and they do **not** mix:

| | Flat stream | Structured store |
| --- | --- | --- |
| Get | `IModelDoc2::IGet3rdPartyStorage` | `IModelDocExtension::IGet3rdPartyStorageStore` |
| Release | `IRelease3rdPartyStorage` | `IRelease3rdPartyStorageStore` |
| Notifications | `LoadFromStorageNotify` / `SaveToStorageNotify` | **`LoadFromStorageStoreNotify` / `SaveToStorageStoreNotify`** |

G-CAM uses the **store**, because versioned CAM data wants several named sub-streams: one
readable `model.xml` plus one binary stream per toolpath. Pairing it with the *stream*
notifications is the mistake to avoid — the names differ by one word and the help for each
half only mentions its own.

There is a third, `AutoSaveToStorageStoreNotify`, for SOLIDWORKS' periodic auto-recover
save. Subscribing to it matters: without it the recovery copy comes back with the part's
geometry and none of its CAM data, which is worse than having no recovery copy, because it
looks complete.

## Signatures

```csharp
object IGet3rdPartyStorageStore(string SubStorageName, bool IsStoring);
bool   IRelease3rdPartyStorageStore(string SubStorageName);

// The notification delegates take no arguments and return int:
int SaveToStorageStoreNotify();
int LoadFromStorageStoreNotify();
```

`IGet3rdPartyStorageStore` returns `IUnknown`; the help says to QueryInterface for
`IStorage`, which in C# is a cast — `node as IStorage`.

**The notifications carry no arguments**, so a handler cannot tell which document is being
saved. One subscriber per open document is the only honest answer: the obvious guess, the
active document, is wrong exactly when it matters, because Save All walks documents that
are not in front. `JobStorageHook` holds its own `ModelDoc2` for this reason.

## The rules that bite

**Writing is locked until `SaveToStorageStoreNotify` fires.** There is no flushing on
demand. Data reaches the file only during a save SOLIDWORKS has already decided to do, so
an edit that wants to survive has to mark the document dirty with `IModelDoc2::SetSaveFlag`
and wait to be asked. An edit path that forgets loses the user's work without ever
prompting.

**Reading is safe any time a document is fully open** — the help says so explicitly. That
is worth knowing, because it means loading does not have to race
`LoadFromStorageStoreNotify`. G-CAM reads when it builds a document's tab, which covers a
part being opened and the ten already open when the add-in is switched on, through one path
rather than two.

**Every get must be matched by a release, including when the get returns null.** Skip it
and the third-party node stays locked for the rest of the session. The help adds two
useful details: the release is *not* required when the get happened inside one of the
storage notifications, and releasing more often than necessary is harmless. So G-CAM
releases unconditionally in a `finally`.

**The storage name is global across add-ins** and should be registered with SOLIDWORKS so
nothing collides. Ours is `ZaberGCamJobs` — under the 30-character cap and specific enough
that nobody else would choose it.

## Element names are short — **Assumed**

OLE structured storage limits an element name to 31 characters plus a terminator. **A
36-character GUID does not fit**, so toolpath streams cannot be named after the operation
whose path they hold. G-CAM numbers them `tp0001`, `tp0002`… and records the mapping in
`model.xml`.

Tagged *Assumed* rather than Verified: it is the documented OLE limit, not something G-CAM
has tested. Confirm by attempting a 40-character element name before relying on it for
anything else.

## Deleting leaves streams behind — **Verified by reading the code, 2026-09-15**

Toolpath streams are numbered `tp0001` upwards from a **fresh walk of the document on
every save**, and a save only ever *writes*. So deleting an operation that had a toolpath —
or one simply losing its path — renumbers everything below it and strands the
highest-numbered stream: nothing reads it again, because the operation-id → stream-name
mapping in `model.xml` only names the streams that exist now.

That is dead weight in every copy of the part from then on, and a part file is the thing
that gets emailed around. `JobDocumentStorage.PruneToolpathStreams` therefore walks upward
from the last stream it wrote, calling `IStorage::DestroyElement`, and stops when one is
not there — safe because the writer numbers them contiguously from 1.

**It can never throw**, because the caller's next statement is `Commit`. A tidy-up is
worth nothing beside the save it would otherwise abandon, so the whole walk is inside a
`try`, with the expected `COMException` (no such element) swallowed as the end of the walk
and anything else logged.

*Tagged from the code and the OLE contract rather than Verified on a machine: nobody has
yet opened a part, generated, deleted and saved, and confirmed the element is gone. Doing
that is the same experiment as the toolpath round-trip listed below — do both at once.*

## What the tree looks like

The help gives the layout SOLIDWORKS builds around us:

```
SwRootStorage
└── ThirdPtyStore
    └── <name SOLIDWORKS assigns>      ← one per add-in
        ├── model.xml                  ← our jobs, operations, tools
        ├── tp0001                     ← one toolpath
        └── tp0002
```

## Where this lives in G-CAM

- `GCam.SolidWorks/Persistence/Interop/Storage.cs` — the `IStorage` declaration, because
  .NET ships `System.Runtime.InteropServices.ComTypes.IStream` but **not** `IStorage`.
  Every method must be declared in vtable order even when never called; omitting one shifts
  every method after it, and a call would land in the wrong slot.
- `ComStreams.cs` — bytes in and out of an `IStream`.
- `JobDocumentStorage.cs` — the storage node, the two directions, the release discipline.
- `JobStorageHook.cs` — one per open part, holding the document the notification does not
  name.
- The format itself is in `GCam.Core/Persistence`, with no COM anywhere near it, so a full
  round trip is a headless test.

## Verified — **2025 SP3, 2026-09-13**

A round trip works: open a part, create a job, close the part, reopen it, and the job is
still there. That exercises the whole chain — `SetSaveFlag` making the part dirty, the
"Save Changes?" prompt, `SaveToStorageStoreNotify` arriving, `IGet3rdPartyStorageStore`
handing over an `IStorage`, `model.xml` written and read back, and the release discipline
not locking the node.

Still unexercised, and worth checking when the chance comes:

- **Toolpath streams.** Operations do generate toolpaths now, so a save after generating
  should write them - but nobody has confirmed a generated path survives a close and
  reopen. That is the one worth checking first: it is the only stored data with its own
  stream, and `tp0001` has never been read back. Delete one of two generated operations in
  the same sitting and the pruning above is exercised with it.
- **Several parts open at once**, which is what the one-subscriber-per-document design
  exists for. A single part cannot show whether it was needed.
- **Save All**, and auto-recover saves through `AutoSaveToStorageStoreNotify`.
- **A part written by one build read by another**, including the skip-and-report path for
  an operation whose strategy is missing.
