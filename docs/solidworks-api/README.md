# SolidWorks API notes

Things learned about the SolidWorks API while building G-CAM — interface behaviour, COM lifetime rules, units and coordinate conventions, UI construction, geometry access, and the gaps and bugs in the official docs.

Worth capturing here: anything that took more than a few minutes to figure out, anything the docs get wrong or omit, and any snippet that works and would be annoying to reconstruct.

Official reference: the SolidWorks API Help installed alongside SolidWorks, and <https://help.solidworks.com/> — but treat both as a starting point, not the truth, and verify against the installed version.

## Index

- [The add-in checkbox un-ticks itself](addin-wont-load.md) — diagnosing silent add-in load failures; stale CLSID version subkeys; which changes require re-registering.
- [Showing WPF windows from an add-in](wpf-in-solidworks.md) — owners, modality, the 64-bit HWND, and drawing.
- [Tabs in the Manager Pane](manager-pane-tabs.md) — why a tab created at connect time never appears, keeping one per document, and which COM objects you may release.
- [PropertyManager pages](property-manager-pages.md) — why a page cannot live inside your own tab and how to get the user back to it, `ref` where the help says `out`, and the handler QI that fails silently.
- [NuGet dependencies in an add-in](addin-dependencies.md) — why packages fail to load with no app.config, and why a try/catch cannot save you.
- [Drawing OpenGL over the 3D view](opengl-overlay.md) — `BufferSwapNotify` and the type you must cast to, why only OpenGL 1.1 is safely reachable, leaving the context as you found it, and why the Performance option everyone blames turns out to be innocent.
- [Coordinate systems and bounding boxes](coordinate-systems.md) — why the transform is measured rather than read out of `ArrayData`, and why `GetExtremePoint` rather than `GetBodyBox` or tessellation.
- [CodeStack](codestack.md) — third-party task-oriented resource; what it covers that the official help doesn't, and how to clone it offline.
