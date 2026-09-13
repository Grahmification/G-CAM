# 0008. One tool list per part, shared by operations

**Status:** Accepted
**Date:** 2026-09-13
**Refines:** [0003](0003-jobs-embed-their-tools.md)

## Context

[0003](0003-jobs-embed-their-tools.md) settled that a part embeds a copy of its tool
rather than referencing a library, so a part opens and posts identically on a machine
with a different library or none. It did not settle *where* the copy lives, because no
operation existed to hold one.

Now it matters. Several operations routinely use one cutter — rough, finish, chamfer —
and each needs its own feeds and speeds. If each operation holds a private copy of the
whole tool, nothing stops two of them describing the same tool number with different
geometry, and the part then claims two different cutters are in the same pocket. The
machine has one.

HSMWorks and Fusion both show a per-document tool list with the operations using each
tool beneath it, which is the view a machinist sets up from: what has to be in the
carousel before this part can run.

## Decision

**`JobDocument.Tools` is one tool list per part**, shared by every job in it. Taking a
tool from a library copies it in once, keeping `Tool.Id` and stamping `SourceLibraryId`
as 0003 specifies. An operation **references** a part tool by id; a second operation
wanting the same tool selects the existing one and does not copy again.

What lives where:

| Lives on | What |
| --- | --- |
| The part tool | Identity, tool number, cutter geometry, holder |
| The operation (`Operation.Cutting`) | Spindle speed, feeds, coolant |

`Operation.Cutting` is seeded from the tool's defaults when the tool is chosen and
diverges freely afterwards. Editing a part tool's geometry marks every operation using it
stale. An unused tool stays in the list, marked unused, until it is removed by hand.

The tool library browser shows the part's list beside the libraries, each tool with the
operations using it beneath — a projection over `JobDocument` computed in Core
(`ToolUsage`), not assembled in a viewmodel.

## Alternatives considered

**A full private tool copy per operation, including geometry.** The literal reading of
0003, and simplest to persist. Rejected: it permits two operations to disagree about the
shape of one physical cutter with nothing to detect it, and the tool list becomes a
deduplicated view over copies rather than a statement of what the part needs.

**Copy-on-write: editing geometry inside an operation forks a new part tool.** Nothing is
ever changed underneath another operation, and the list stays a truthful inventory of
distinct cutters. Rejected as more surprising than the problem it solves — silent tool
numbering like `T4.1` appearing because a diameter was nudged, and a fork rule to explain.

**Prompt on every shared-tool edit.** Maximum control, rejected for putting a dialog on a
common path and making a correct answer the user's job every time.

**One tool list per job.** Keeps a job self-contained and copyable between parts with its
tooling. Rejected: duplicate copies of one physical cutter across jobs in the same part,
and tool-number clashes between jobs become possible.

**Auto-pruning unused tools on save.** Rejected: deleting an operation to rebuild it would
silently drop a tool someone had customised.

## Consequences

**Two operations cannot disagree about a cutter's shape.** The failure this exists to
prevent is structurally impossible rather than merely detected.

**Editing a tool's geometry has reach.** It invalidates every operation using it, which is
correct but must be visible — the edit shows what it will affect before it is applied.

**The list accumulates.** Unused tools stay until removed. That is the intended trade
against silently losing a customised tool, and it means the browser has to mark unused
entries clearly or the list stops describing what the part needs.

**0003 still holds.** A part remains self-contained, library edits still never reach it,
and re-linking remains an explicit action. This decision changes the scope of the copy
from per-operation to per-part, not whether there is one.

**Copying a job between parts now has to carry tools with it**, because the tools are not
inside the job. That path does not exist yet; when it does, it has to copy referenced
tools into the destination part's list and re-point the operations, merging by
`Tool.Id` + `SourceLibraryId` where the same tool is already present.
