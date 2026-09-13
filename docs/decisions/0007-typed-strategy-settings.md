# 0007. Strategy parameters are typed classes, not a named parameter bag

**Status:** Accepted
**Date:** 2026-09-13

## Context

Measured across the four strategies in the HSMWorks export — `face`, `adaptive2d`,
`contour2d`, `drill` — there are **231 distinct parameters, of which 47 are common to all
four**. `contour2d` alone carries 152.

So an operation is roughly one fifth shared and four fifths strategy-specific, and the
strategy-specific part has to be stored in the SOLIDWORKS document, edited on a
PropertyManager page, read by the strategy that generates the toolpath, and eventually
written to and read from template files.

HSMWorks itself uses a flat bag: `<parameter name="..." expression="..."/>`, with meaning
carried by convention in the name. That shape makes importing their templates nearly
mechanical and lets a new strategy exist without a new type.

## Decision

**Each strategy gets a typed settings class** — `Contour2dSettings`, `DrillSettings` —
deriving from an abstract `StrategySettings`, with real properties, enums and defaults.
`Operation` holds one polymorphically.

Groups that several strategies share but not all — linking, multiple depths, lead-in/out
— are **composed** as small classes held by the strategies that have them, not inherited
from a base that would give drilling a lead-in radius.

`StrategyCatalog` maps a `StrategyId` to its settings type, its `IToolpathStrategy` and
its display name. New Operation, the persistence reader and the template importer all
consult it, so a new strategy registers in one place.

## Alternatives considered

**Named parameter bag plus a per-strategy schema.** HSM's own shape. Genuinely tempting:
template import becomes name-matching, persistence is one generic writer, and a
schema-driven page builder would generate 152 controls without 152 lines of page code.
Rejected because every consumer that matters — the strategy computing the toolpath, the
post, the tests — would read magic strings with no compile-time check, and a renamed or
mistyped parameter would fail at runtime in the middle of a generate. The place this
design pays off is UI generation, and that benefit can be had separately without giving
up type safety everywhere else.

**Typed classes annotated with attributes that generate the page and the XML.** One
definition driving model, UI and file, with types preserved. Rejected *for now* on
sequencing rather than merit: it requires an attribute vocabulary and a page generator to
be designed and debugged before the first operation can be edited at all. It remains the
natural next step once two or three strategies exist and the real shape of the groups is
known from practice rather than guessed.

**One flat class with all 231 parameters.** Rejected: most fields meaningless at any
moment, and no way to validate or persist an operation without knowing which ones apply.

## Consequences

**Strategy code, posting and tests read real properties.** A misspelling is a compile
error. `Contour2dSettings` can be constructed in a headless test without a document, a
page or a schema.

**Each strategy costs a hand-built property page section.** This is the real price, and
it is paid per strategy rather than per parameter — bounded, and front-loaded onto the
four strategies we know about. The base page builds the Tool and Heights groups once for
everyone.

**Template import is name-based mapping onto typed properties**, not a dictionary copy.
That mapping has to be written and maintained per strategy, and it is where an unknown
HSM parameter gets reported rather than silently absorbed. A bag would have absorbed it
silently, which reads as an advantage until an unrecognised parameter changes what a
toolpath does.

**Adding a strategy means adding types**, and a strategy cannot be defined by data alone.
That is a deliberate trade: G-CAM has four strategies planned, not forty.

**The attribute-driven generator stays available.** Typed classes are a prerequisite for
it, not an obstacle — moving later means adding attributes to properties that already
exist.
