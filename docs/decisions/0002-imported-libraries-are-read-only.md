# 0002. Imported tool libraries are read-only

**Status:** Accepted
**Date:** 2026-09-12

## Context

G-CAM reads HSMWorks/Fusion tool libraries (`.hsmlib`) as well as its own `.gcamtools`
format. Adding tool editing raised the obvious question: what happens when you edit a
tool in a library that came from HSMWorks?

G-CAM has a reader for `.hsmlib` and no writer. The example library the project is being
built against is `.hsmlib`, so this is the common case, not an edge one.

## Decision

**An imported library cannot be edited in place.** Editing commands are disabled on any
format G-CAM cannot write, the browser marks it `read-only` in the tree, and a
`Save as G-CAM library…` action produces an independent `.gcamtools` copy that is fully
editable.

`ToolLibraryImporter.CanWrite` is the single test, and it is true only for G-CAM's own
extension. Everything else — `LibrarySession`, the context menu, the toolbar — asks it
rather than checking extensions itself.

The conversion writes the new file immediately rather than waiting for the session
commit. Converting is a deliberate act with a path the user just chose, and the copy has
to appear in the folder tree to be selected and edited at all.

## Alternatives considered

**Write `.hsmlib` back out.** The best outcome for the user: a full round trip, and the
file stays usable by HSMWorks. Rejected for cost and risk. It needs a writer built and
verified against a schema we only have one sample of, and a bug in it corrupts a file
another application owns and a shop depends on. That is a poor trade against a feature
whose point is editing G-CAM's own libraries.

**Silently write a `.gcamtools` copy alongside on first edit.** No extra step for the
user. Rejected because of what it does to the folder: `Tormach.hsmlib` and
`Tormach.gcamtools` then both appear in the tree, with the same display name, diverging
from that moment on and nothing showing which is current. It trades one explicit action
for a permanent ambiguity.

**Hold edits in memory and ask where to save on commit.** No read-only surprise while
editing. Rejected because the surprise merely moves to the worst possible moment: you
discover the original will not be updated only after doing the work.

## Consequences

**Accepted costs.** Editing an imported library is a two-step job — convert, then edit.
The converted copy does not track the original, so re-importing an updated `.hsmlib`
later means merging by hand or re-importing over the top (which works, since importers
reuse the source guid as the G-CAM tool id, so a re-import updates rather than
duplicates).

**Gained.** HSMWorks keeps sole ownership of its own files, so nothing G-CAM does can
corrupt them. There is exactly one writable format to test and version. And the moment
of conversion is visible and deliberate, so nobody ends up with two libraries of the same
name quietly disagreeing.

**Revisit if** round-tripping to HSMWorks becomes a real workflow rather than a
one-directional import. That would justify the writer, and the `IToolLibraryReader`
split already leaves room for a matching writer interface.
