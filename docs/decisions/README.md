# Decisions

Short records of architectural decisions and the reasoning behind them, so the "why" survives after the code makes the "what" obvious.

Worth a record: anything that would be expensive to reverse, anything where a reasonable person would pick differently, and anything you'd otherwise have to re-argue in six months. Not worth a record: routine choices the code already explains.

Number files in order: `0001-short-title.md`. Copy `TEMPLATE.md` to start. Decisions are append-only — when one is replaced, leave the original in place, mark it superseded, and link the new one.

## Index

- [0001. Target .NET Framework 4.8 for the add-in](0001-target-net-framework-48.md) — why not .NET 8, and the one-runtime-per-process constraint that decides it.
- [0002. Imported tool libraries are read-only](0002-imported-libraries-are-read-only.md) — why editing an .hsmlib means converting it first.
- [0003. A job embeds a copy of its tool](0003-jobs-embed-their-tools.md) — why a part is self-contained, and library edits never reach a saved job.
