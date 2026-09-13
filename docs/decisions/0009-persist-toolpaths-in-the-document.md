# 0009. Generated toolpaths are stored in the SOLIDWORKS document

**Status:** Accepted
**Date:** 2026-09-13

## Context

An operation's toolpath is the expensive part: seconds to minutes of computation, and the
thing that actually gets posted to a machine. When a part is reopened, either it is there
or it is recomputed.

HSMWorks stores toolpaths in the document, which is why a proven part shows its paths
immediately on open and can be posted without regenerating. The cost is file size — a
toolpath is easily tens of thousands of points — and the risk that a stored result no
longer matches the inputs that produced it.

G-CAM already commits to storing jobs inside the document through third-party storage,
which constrains how and when writing can happen: read and write only on
`LoadFromStorageNotify`/`SaveToStorageNotify`, and stream names are short — the
third-party storage name is capped at 30 characters.

## Decision

**The generated toolpath is persisted with the operation.** Reopening a part shows and
posts the same paths without recomputing, and without needing the model geometry to still
resolve.

Layout inside `IGet3rdPartyStorageStore`:

```
GCam
  model.xml        jobs, operations, parameters, the part tool list — versioned XML
  tp0001, tp0002…  one binary stream per generated toolpath
```

The model is XML — small, diffable, readable when something has gone wrong. Toolpaths are
binary, in their own streams, keyed by a counter rather than by operation id because a
36-character GUID does not fit a structured-storage element name. `model.xml` holds the
operation-id → stream-name map.

Correctness is defended by state, not by a hash: every input change marks the operation
`Stale` (see `docs/design/operations.md`), the stored path is drawn dimmed, and posting a
stale operation warns.

## Alternatives considered

**Regenerate on open, persist only inputs.** Small documents, and a stored path can never
be stale because there isn't one. Rejected: opening a part with a dozen operations would
mean recomputing everything before anything could be posted or even seen, and a part
whose model changed would quietly produce different G-code from the run that was proven
on the machine. Being *able* to see what the machine last did is most of the value.

**Persist as a cache keyed by a hash of the inputs.** Shown on open, discarded when the
hash disagrees — no false staleness, no stale posting. Rejected on the hard part: the
hash must cover the model geometry, and a face can change shape without changing its
persistent id, so the hash would have to digest resolved geometry on every open. That is
most of the cost of regenerating. Conservative staleness gets the safety for none of it.

**Everything in one XML stream.** One thing to version, trivially inspectable. Rejected:
a few thousand moves as text adds megabytes to every part file and to every save.

## Consequences

**Part files grow.** A part with a dozen generated operations carries its toolpaths. This
is the direct cost, accepted because the alternative is recomputation on every open.

**A stored toolpath can be wrong about the model**, and only the staleness rules stand
between that and a bad part. Those rules are therefore deliberately over-eager: a
SOLIDWORKS rebuild marks every operation in the part stale even if nothing relevant moved.

**A corrupt toolpath stream costs a regeneration, not a job.** Because each path is its
own stream and the parameters live in `model.xml`, an unreadable path is recoverable by
regenerating. That separation is most of the reason for the split.

**Stream naming needs a map, not a convention.** The 30-character limit forces short
generated names, so nothing can be found by deriving a name from an operation id. Losing
`model.xml` orphans every toolpath stream — acceptable, since without the parameters the
paths are meaningless anyway.

**Toolpath serialisation becomes a versioned format** that has to be read back by later
builds. A format change means either a migration or discarding stored paths and
regenerating, which is the escape hatch — it is always legal to throw a toolpath away.
