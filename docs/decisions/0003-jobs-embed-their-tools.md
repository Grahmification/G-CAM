# 0003. A job embeds a copy of its tool, it does not reference one

**Status:** Accepted
**Date:** 2026-09-12

## Context

An operation needs a tool. Tools live in libraries on disk; jobs live inside the
SOLIDWORKS document. When a part is opened on a machine with a different tool library —
or none — what should the operation use?

Nothing consumes this yet: job persistence into the SOLIDWORKS document is not built.
The decision is recorded now because the model already implements it, and because
reversing it later means changing what is stored in every saved part.

## Decision

**The job stores a full copy of the tool**, written into the SOLIDWORKS document
alongside the operation, plus the id of the library it came from.

- `Tool.CloneForJob(sourceLibraryId)` takes the copy and stamps its origin.
- `ToolLibrary.CheckOut(toolId)` is the library-side entry point.
- `Tool.Id` is preserved on the copy, and `Tool.SourceLibraryId` records where it came
  from, so the pair identifies the library entry it was taken from.
- Re-linking a job to its library is an explicit action. Library edits never reach a job
  on their own.

## Alternatives considered

**Reference by id only.** One source of truth, and fixing a tool in the library would
propagate everywhere. Rejected: a part opened without that library cannot be posted at
all, and a colleague's differently-edited library would silently change toolpaths that
have already been proven on a machine. Silent change is the specific failure worth
paying to avoid.

**Embed with no link back.** Simplest, but a corrected tool definition would mean
editing every operation by hand, with nothing recording where the tool originally came
from.

**Reference while editing, snapshot at post time.** Keeps the library authoritative and
the posted output traceable, but the document alone is still not reproducible — which is
most of the point.

## Consequences

**A part is self-contained.** It opens and posts identically on any machine. This is
what Fusion and HSMWorks do, for the same reason.

**Library edits do not reach existing jobs.** That is the intent, not an oversight. It
also means a genuinely wrong tool definition has to be re-linked deliberately, per job —
so a "re-link from library" action will be needed once jobs exist, and should show what
would change before changing it.

**Copies must be deep.** `Tool.Clone` deep-copies geometry, cutting data, holder and the
property bag; `LibrarySession.SaveAsCopy` additionally re-points cloned tools at the
cloned holders. A shallow copy anywhere here would reintroduce exactly the shared-state
problem this decision exists to prevent, and is covered by tests.
