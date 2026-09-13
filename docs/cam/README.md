# CAM domain notes

CAM knowledge that isn't specific to SolidWorks: how toolpaths are generated and what makes them correct, safe, and efficient.

Likely to accumulate here: toolpath strategies and when each applies, stock and rest-material modelling, planar offsetting and its degenerate cases, tool and holder geometry, gouge and collision avoidance, linking and lead-in/lead-out moves, feeds and speeds, G-code dialects, and post-processor structure.

Where a choice is a tradeoff rather than a fact — tolerance versus toolpath size, stepover versus finish quality — write down the tradeoff, not just the value picked.

## Index

- [The G-CAM tool library format](gcam-tool-library-format.md) — our own .gcamtools schema, versioning, and the atomic-write rule.
- [The HSMWorks tool library format](hsm-tool-library-format.md) — .hsmlib field meanings, verified against a real library, and the traps in it.
