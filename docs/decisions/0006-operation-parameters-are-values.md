# 0006. Operation parameters are values, not expressions

**Status:** Accepted
**Date:** 2026-09-13

## Context

HSMWorks stores every operation parameter as an *expression*, not a number. From the
export in `docs/example_files/example_operations.hsmworks-template`:

```
bottom              = surfaceZLow + bottomOffset
bottomOffset        = -0.5mm
tolerance           = 0.001mm
highFeedrate        = Math.max(tool_feedCutting; tool_feedEntry; tool_feedExit)
tool_feedPerTooth   = tool_feedCutting/(tool_spindleSpeed * tool_numberOfFlutes)
tool_surfaceSpeed   = tool_isTurning ? (200m/min) : (tool_stockDiameter * Math.PI * tool_spindleSpeed)
```

That is a small language: unit-suffixed literals, references to other parameters,
arithmetic, ternaries and a `Math` namespace. Following it means a parser, a unit system,
a dependency graph with cycle detection, and per-field error reporting. Not following it
means deciding what happens to the derived values, which are the part users actually
interact with — a machinist enters surface speed or spindle speed depending on which
their handbook gives, and expects the other to follow.

G-CAM is an internal 3-axis tool. Expression-driven parameters are a feature of HSM's
template system rather than of everyday operation editing.

## Decision

**Parameters are typed values.** `double` in millimetres, `bool`, and enums. No parser,
no expression storage, no dependency graph.

Two things preserve what the expressions were actually buying:

1. **Unit-aware input.** Entry fields accept `10`, `10mm`, `0.5in` and convert on entry
   through `GCam.Core.Units`.
2. **Bidirectional derived pairs.** Surface speed, feed per tooth and feed per revolution
   are editable, and editing either end updates the other. Only the canonical value —
   spindle rpm, feed in mm/min — is stored; the derived one is recomputed for display
   every time it is shown. The conversions are pure functions in
   `Core/Tooling/FeedsAndSpeeds`.

## Alternatives considered

**Full expression model.** Exact HSM parity, and templates would import verbatim.
Rejected on size: a parser, units, evaluation ordering and cycle detection is a
subsystem, not a feature, and it would have to be right before the first toolpath could
be computed. Nobody here has asked to type a formula into a feed box.

**Values plus an optional expression string per parameter.** Roughly 80% of the behaviour
for a fraction of the machinery, and the escape hatch stays open. Rejected *for now* as
unnecessary rather than wrong — if a real request for computed parameters appears, this
is the shape to add, and nothing in the value-based model forecloses it.

**Values with read-only derived fields.** The straightforward reading of "plain values",
and rejected explicitly: forcing a machinist to convert surface speed to rpm by hand is
the exact friction the derived values exist to remove.

## Consequences

**Storing only the canonical value is what keeps the pair honest.** If `surfaceSpeed`
were stored as an evaluated number, editing the tool's diameter afterwards would leave it
describing a cutter that no longer exists. Recomputing on display cannot drift. HSM gets
the same property from expressions; we get it from not storing the derived half.

**Imported HSM templates lose non-literal parameters.** A parameter whose expression is
not a literal imports as the strategy's default and is listed in the import report. In
practice the non-literal ones are almost exactly the values G-CAM computes itself —
`tool_feedPerTooth`, `tool_surfaceSpeed`, `highFeedrate` — so the loss is smaller than it
looks. It is still a loss, and it is why importing reports rather than staying silent.

**Exporting to HSMWorks' own format is off the table**, since every parameter would have
to be written as an expression. G-CAM writes its own template format instead; see
`docs/design/operations.md`.

**Round-tripping a derived value is lossy in the last decimal.** Entering a surface speed
computes an rpm, which recomputes a slightly different surface speed. Displayed values
are rounded to the precision a machinist uses, so this is invisible — but it is why the
derived value must never become the stored one.
