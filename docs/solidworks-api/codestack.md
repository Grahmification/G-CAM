# CodeStack — third-party SOLIDWORKS API resource

<https://www.codestack.net/solidworks-api/> — by Artem Taturevych / Xarial Pty Limited.

**Source is open.** The entire site is MIT-licensed markdown at <https://github.com/xarial/codestack>, so it can be cloned and read offline rather than scraped. **Verified 2026-09-12:** shallow clone is 65 MB; `solidworks-api/` holds 434 articles, 380 VBA files, 60 C# files.

Cloning on Windows needs `git config core.longpaths true` — several paths under `getting-started/inter-process-communication/` exceed MAX_PATH and checkout fails part-way with "Filename too long" while still reporting a successful clone.

## What it's for, versus the official help

They answer different questions, so neither replaces the other:

- **Official help** (the `solidworks-api` skill) — *what the API declares*. Complete, versioned to 2025 SP3, authoritative on signatures, enum values, and Remarks. Organised per member.
- **CodeStack** — *how to accomplish a task*. Organised per goal, with working end-to-end code. Covers things the reference cannot: which calls to combine, in what order, and the conventions nobody states in a method page.

The prose is often thin — many articles are a few lines plus a macro file. The value is concentrated in the code and in a small number of conceptual articles.

## Demonstrated value

`getting-started/api-object-model/i-api-versions` corrected a real error in our own notes. Working only from the official help, we had generalised from `IGetFaces` to "from C#, always use the non-`I` method" — which is wrong. CodeStack states the actual principle (I-versions return type-safe interfaces and don't expose events), and cross-checking it against the help confirmed the nuance now recorded in the skill's `reference.md`.

That is the failure mode this resource guards against: the official help contains the facts but scattered across member pages, which makes over-confident generalisation easy.

## Sections worth knowing about

- `getting-started/api-object-model/` — accessors, class diagram, I-versions, naming conventions. Direct overlap with the skill's `reference.md`; useful as a second opinion.
- `getting-started/add-ins/csharp/` — add-in scaffolding in C#, matching what this project is building.
- `troubleshooting/addins/` — `shared-library-conflict`, `sdk-installation`. Small, but this class of problem is absent from the official help.
- `geometry/` — unusually relevant to CAM: `ray-intersection`, `offset-planar-wire-body`, `precise-bounding-box`, `body-interference`, `bodies-diff`, `get-bspline-parameters`, `face-iso-curves`, `body-extreme-points`.

## Caveats

Code examples skew heavily VBA (380 vs 60 C# in the API section). API calls translate mechanically — the interfaces are identical — but expect to port, not copy.

It is community material, not vendor documentation, and some articles link to older help versions (2018, 2020). Treat it as **From docs (third-party)** and confirm anything load-bearing against the local 2025 help before relying on it. See [[README]] for the confidence tags.

## Related projects by the same author

`xarial/xcad` (a framework for SOLIDWORKS add-ins, C#) and `xarial/cad-plus`. Not in use here — this project builds directly against the interop assemblies — but worth knowing they exist before hand-rolling a large amount of add-in plumbing.
