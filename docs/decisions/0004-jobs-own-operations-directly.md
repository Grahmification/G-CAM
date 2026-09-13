# 0004. A job owns its operations directly, with no Setup level

**Status:** Accepted
**Date:** 2026-09-12

## Context

`docs/architecture.md` specified a three-level model from the start — Job → Setup →
Operation — and the build-order section named it as step 2. That was written before any
of the model existed.

When the job model came to be built, the question was what each level would actually
hold. In HSMWorks the **Setup** is where the work is: it owns the stock, the work
coordinate system, the WCS offset and the model selection, and operations sit directly
inside it. There is no separate job above it. Fusion 360 is the same, under the same name.

A three-level G-CAM would therefore have had a Job holding little more than a name and a
machine — and the machine is deliberately out of scope for now.

## Decision

**A job owns its operations directly.** `Job` carries the model selection, the stock, the
coordinate system and the work offset; `Operation` sits inside it. A G-CAM Job is what
HSMWorks calls a Setup.

This reverses the three-level model in `architecture.md`, which has been updated.

## Alternatives considered

**Job → Setup → Operation, as originally specified.** Anticipates a part machined in two
fixturings, where two setups share a job's model and machine. Rejected for now: with the
machine out of scope, the Job level would have held a name and nothing else, and every
piece of UI would have had to manage a level with no content — a tree node to expand, a
page to fill in, a parent to choose when creating an operation. A level that exists only
to be passed through is a level that gets in the way.

**Name the concept "Setup" and match HSMWorks exactly.** Tempting for a team moving off
HSMWorks. Rejected because "job" is what the rest of G-CAM already says — the tree tab,
the toolbar button, the ADR above this one — and renaming it everywhere to gain
familiarity with one tool was not worth the churn.

## Consequences

**Multiple fixturings mean multiple jobs.** Two setups on the same part become two jobs,
each with its own coordinate system and stock. That is slightly more to fill in than
sharing a parent would be, and it is honest: the two really do differ in everything a
job currently holds.

**Re-introducing a level later is a migration, not an edit.** Jobs are not yet persisted,
so today the cost is nil. Once jobs are written into SOLIDWORKS documents, inserting a
level above them means reading old documents that do not have it. If a machine
definition arrives and wants to be shared across setups, that is the moment to weigh this
again — and the read side of persistence should be written expecting the shape to move.

**`architecture.md` no longer describes a Setup type.** The layout tree and the build
order were updated in the same change. `Core/Model/` holds `Job`, `Operation` and
`Stock`; there is no `Setup.cs` to write.
