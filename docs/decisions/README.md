# Decisions

Short records of architectural decisions and the reasoning behind them, so the "why" survives after the code makes the "what" obvious.

Worth a record: anything that would be expensive to reverse, anything where a reasonable person would pick differently, and anything you'd otherwise have to re-argue in six months. Not worth a record: routine choices the code already explains.

Number files in order: `0001-short-title.md`. Copy `TEMPLATE.md` to start. Decisions are append-only — when one is replaced, leave the original in place, mark it superseded, and link the new one.

## Index

- [0001. Target .NET Framework 4.8 for the add-in](0001-target-net-framework-48.md) — why not .NET 8, and the one-runtime-per-process constraint that decides it.
- [0002. Imported tool libraries are read-only](0002-imported-libraries-are-read-only.md) — why editing an .hsmlib means converting it first.
- [0003. A job embeds a copy of its tool](0003-jobs-embed-their-tools.md) — why a part is self-contained, and library edits never reach a saved job.
- [0004. A job owns its operations directly](0004-jobs-own-operations-directly.md) — why there is no Setup level, and what it would cost to add one later.
- [0005. Draw the overlay with fixed-function vertex arrays](0005-opengl-overlay-with-vertex-arrays.md) — why not immediate mode, buffer objects or shaders inside somebody else's GL context.
- [0006. Operation parameters are values, not expressions](0006-operation-parameters-are-values.md) — why G-CAM does not implement HSM's expression language, and how derived values stay editable from both ends anyway.
- [0007. Strategy parameters are typed classes, not a named parameter bag](0007-typed-strategy-settings.md) — 231 parameters across four strategies, and why type safety beat generic UI generation.
- [0008. One tool list per part, shared by operations](0008-document-tool-list.md) — where the embedded tool copy from 0003 actually lives, and why geometry is shared while feeds are not.
- [0009. Generated toolpaths are stored in the SOLIDWORKS document](0009-persist-toolpaths-in-the-document.md) — why a reopened part shows its paths without recomputing, and what defends against a stale one.
- [0010. Selection is what the 3D view shows, one row at a time](0010-selection-is-what-the-3d-view-shows.md) — why a job no longer drags every toolpath onto the screen, and what multiple selection cost to get.
- [0011. Merge contours by clipping each path against what the others forbid](0011-merge-contours-by-clipping.md) — why several contours at one depth are clipped one at a time rather than cut from one combined region, and what a pocket forbids.
