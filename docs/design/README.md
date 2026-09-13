# Design notes

How one part of G-CAM hangs together: the objects involved, which project each lives in,
and the constraints that decided the shape. One file per subsystem.

These are the notes you read *when working on that subsystem* — which is the whole point
of keeping them out of [architecture.md](../architecture.md). That file holds what applies
everywhere: the layout, the dependency rules, and the rules with teeth. These hold what
applies to one area, and they grow as areas land.

The status table at the top of [architecture.md](../architecture.md) links here from each
area that has a note, so start there if you do not already know which subsystem you want.

## Index

- [Jobs](jobs.md) — the job model, what `JobDocument` owns rather than the viewmodel, the three-projects-that-cannot-see-each-other arrangement, and the 3D preview.
- [UI shells](ui-shells.md) — the four custom UI surfaces, what hosts each, why property pages are rebuilt per show, and the toolbar icon strip.

Add a file when a subsystem's design stops fitting in a paragraph, and add its line here.
