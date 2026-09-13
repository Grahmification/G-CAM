# G-CAM docs

Working knowledge base for this project. The SolidWorks API is large, sparsely documented, and full of behaviour you only discover by trying it — and CAM has its own body of domain knowledge. Both are worth writing down as they're learned, so the next person (or the next Claude session) doesn't rediscover them.

This is a notebook, not a manual. Half-finished notes are fine and better than nothing.

## Where things go

Start with **[architecture.md](architecture.md)** — the project layout, the dependency rules, and the constraints that shape them. Its status table links to the design note for each subsystem, so it is also the way in when you know *what* you are working on but not *where* it lives. **[error-handling.md](error-handling.md)** covers the boundary/logging/reporting plan.

| Folder | Contents |
| --- | --- |
| `design/` | How one subsystem of G-CAM hangs together: the objects, which project each lives in, and what decided the shape. One file per subsystem, linked from the status table in `architecture.md`. |
| `solidworks-api/` | How the SolidWorks API actually behaves: interfaces, COM quirks, units, event and lifetime gotchas, working snippets. |
| `cam/` | CAM domain knowledge independent of SolidWorks: toolpath strategies, geometry/offsetting, feeds and speeds, G-code dialects, post-processing. |
| `decisions/` | Architecture decisions and why they were made. See `decisions/TEMPLATE.md`. |

One topic per file, `kebab-case.md`, named for the topic rather than the date (`command-manager-tabs.md`, not `2026-09-12-notes.md`). Link between files liberally.

Add a line to the relevant folder's `README.md` index when adding a file.

## Conventions

**Mark how confident you are.** This matters more than usual here, because much of what goes in `solidworks-api/` will come from experiment rather than documentation, and a guess that reads like a fact will cost hours later. Tag claims that aren't obvious:

- **Verified** — observed it work, and say how (which SolidWorks version, what you ran).
- **From docs** — link the API help page or forum post.
- **Assumed** — reasoning that hasn't been tested. Say what would confirm it.

**Record the version.** SolidWorks API behaviour changes between releases. Note the version any verified finding was observed on.

**Keep snippets runnable.** Prefer a short block that compiles against the real interfaces over pseudocode.

**Prune what turns out to be wrong.** Delete or correct stale notes rather than layering caveats on them — a wrong note is worse than a missing one.
