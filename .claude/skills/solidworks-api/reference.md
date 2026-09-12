# How the SOLIDWORKS API is laid out

A navigation map: which interface owns what, and how to get from one to another. Every claim below was checked against the 2025 SP3 help with `swapi.py` — where the help states something directly it is quoted or cited by topic name.

Read this when you don't yet know *which* interface to look up. Once you have a name, go straight to `swapi.py show`.

## The shape of it

It is a **COM API of interfaces, not a class library.** Per `Understanding_the_SolidWorks_API_Class_Hierarchy`, SOLIDWORKS deliberately publishes no class hierarchy diagram, because it uses interfaces, interface inheritance, and **factory methods that return interfaces** — not implementation inheritance. There is no `new` for API objects: you always obtain them from something that already exists.

Two consequences that shape all lookups:

1. **You navigate by accessor, not by construction.** To reach anything, walk down from `ISldWorks`. If you can't find a constructor, you're looking for the wrong thing — find the parent that hands it to you.
2. **Casting between interfaces is a plain C# cast.** The help calls it QueryInterface; in C# use `as`/`is`. The documented QueryInterface families:
   - `IPartDoc`, `IAssemblyDoc`, `IDrawingDoc` → `IModelDoc2`
   - `IEdge`, `IFace2`, `IFeature`, `ILoop2`, `IVertex` → **`IEntity`**
   - `ISketchArc`, `ISketchLine`, `ISketchSpline`, … → `ISketchSegment`
   - `IAttribute` → `IFeature`

   `IEntity` is the one that matters most: it is how any topology object becomes selectable.

## The object chain

```
ISldWorks                         (the application; handed to you in ConnectToSW)
├── ActiveDoc ──────────────► IModelDoc2      (the open document)
│   ├── Extension ──────────► IModelDocExtension
│   ├── SelectionManager ───► ISelectionMgr
│   ├── FeatureManager ─────► IFeatureManager
│   ├── ConfigurationManager, SketchManager, ActiveView
│   └── (cast) ─────────────► IPartDoc / IAssemblyDoc / IDrawingDoc
├── GetCommandManager(cookie) ► ICommandManager    (UI; needs the add-in cookie)
├── GetModeler() ───────────► IModeler             (geometry factory)
└── GetMathUtility() ───────► IMathUtility         (points, vectors, transforms)
```

**`IModelDocExtension` is where newer functionality went.** When `IModelDoc2` seems to be missing something, check `.Extension` before concluding it doesn't exist — `SelectByID2`, `GetCorrespondingEntity`, and `CreateMeasure` all live there.

## Topology vs geometry — the distinction that matters for CAM

These are two parallel trees and conflating them will waste your time.

**Topology** — the structure of the solid, *what is connected to what*:

```
IBody2 → IFace2 → ILoop2 → ICoEdge → IEdge → IVertex
```

Traversal is `GetFaces`/`GetFirstLoop`/`GetCoEdges`/`GetEdges`, plus `GetNext`-style walking. A loop yields both `GetEdges` and `GetCoEdges`; the co-edge carries the edge's orientation *within that particular loop*, which is what you need for deciding material side.

**Geometry** — the actual mathematical surface or curve underneath:

| From topology | Accessor | Gives geometry |
| --- | --- | --- |
| `IFace2` | `GetSurface` | `ISurface` |
| `IEdge` | `GetCurve` | `ICurve` |
| `IVertex` | `GetPoint` | point coordinates |

A face is a *trimmed region of* a surface. For toolpath work you will usually need both: topology to know the boundary, geometry to evaluate and offset it.

**Tessellation is not geometry.** `IFace2.GetTessTriangles` Remarks state plainly that its triangles are for graphics display and "do not represent a tessellation that can be used, for example, by a machining application" — it directs you to traverse faces and build your own faceting from the topology and geometry instead. Read that topic in full before relying on it.

## Making and combining geometry

`IModeler` (from `ISldWorks.GetModeler`) is the factory for **temporary bodies** — bodies that exist in memory without appearing in the feature tree. This is the natural home for stock, rest material, and intermediate toolpath geometry: `CreateBodyFromBox`, `CreateBodyFromCyl`, `CreateBrepBody3`, `CreateBsplineSurface`, and the `Create*Surface` family.

Boolean operations are on the body itself: **`IBody2.Operations2`**, taking a `swBodyOperationType_e` — `SWBODYADD` (15903), `SWBODYCUT` (15902), `SWBODYINTERSECT` (15901). That plus temp bodies is the core of any material-removal simulation.

`IBody2.GetBodyBox` gives a bounding box; `IModeler.CheckInterference3` and friends do collision checks.

`IMathUtility` supplies `CreatePoint`, `CreateVector`, `CreateTransform`, `ComposeTransform` — use these rather than rolling your own transform math, because API calls that take transforms expect these objects.

## Features and the tree

`IFeatureManager` creates features; `IFeature` is one node. Feature *types* are identified by `swFeatureNameID_e`.

For a CAM add-in, **macro features** are the mechanism for putting your own regenerable operations into the tree. The programming guide has a dedicated section — `Overview_of_Macro_Features`, `Inserting_Macro_Features`, `Editing_Macro_Features`, `Macro_Features_and_Dimensions`.

## Add-in UI

| Need | Interface | Book |
| --- | --- | --- |
| Toolbars, tabs, menus | `ICommandManager` → `ICommandGroup` | `sldworksapi` |
| Task/property panels | `IPropertyManagerPage2` | `sldworksapi` |
| Panel callbacks | `IPropertyManagerPage2Handler` … `Handler9` | **`swpublishedapi`** |
| The add-in itself | `ISwAddin` | **`swpublishedapi`** |

Anything **you implement** rather than call lives in `swpublishedapi`. If a handler interface isn't turning up, that's the book it's in.

## Naming conventions

**Numeric suffixes are versions, and the original usually still exists.** `IBody` and `IBody2` are both in the help, as are `CreateCommandGroup`/`CreateCommandGroup2` and `DeleteFaces` through `DeleteFaces5`. **Default to the highest number** unless you have a reason not to; the older ones are kept for compatibility.

**`I`-prefixed twins are not versions**, and which one C# should use depends on what the member returns. Both cases verified in the 2025 help:

| Returns | Non-`I` form | `I` form | Use from C# |
| --- | --- | --- | --- |
| A single object | `ActiveDoc` → `System.object` | `IActiveDoc2` → `ModelDoc2` | **the `I` form** — it is type-safe, no cast |
| An array | `GetFaces()` → `System.object` | `IGetFaces` → raw C++ pointer | **the non-`I` form**, then cast |

The tell is the Return Value line: `IGetFaces` reads "VBA, VB.NET, C#, and C++/CLI: **Not supported**", so it is C++-only. When no such note appears, the `I` form is the typed one and saves you a cast.

The one trade-off: `I`-versions do not expose events. If you need to sink events, take the non-`I` interface.

**Enums live in `swconst` and end in `_e`** — `swBodyType_e`, `swSelectType_e`, `swBodyOperationType_e`. `swapi.py show <enum>` prints every member with its integer value. Values are frequently non-obvious (the body-operation constants are in the 15900s), so look them up rather than assuming.

**Everything is in meters and radians.** The help states "SOLIDWORKS API functions operate in meters" — document display units are a separate concern, converted via `ISldWorks.GetUserUnit`. Unit confusion is the classic source of toolpaths that are off by a factor of 1000.

**An HRESULT of S_OK does not mean success.** Per `Return_Values`, it means the call was *dispatched* successfully, not that it achieved anything. Object returns must be null-checked; the help's own example checks every returned pointer.

## Where to look first

| You need | Start at |
| --- | --- |
| The app, add-in startup, options | `ISldWorks`, `ISwAddin` |
| The open document | `IModelDoc2`, then `.Extension` |
| What the user picked | `ISelectionMgr`, and `IEntity` for the picked object |
| Solid structure, faces, edges | `IBody2` → `IFace2` → `ILoop2`/`IEdge` |
| Actual surface/curve math | `IFace2.GetSurface`, `IEdge.GetCurve` |
| Stock, offsets, temp bodies, booleans | `IModeler`, `IBody2.Operations2` |
| Points, vectors, transforms | `IMathUtility` |
| Custom tree operations | `IFeatureManager`, macro features |
| A constant or flag value | `swconst`, `swXxx_e` |

## Gaps

The indexed books are `sldworksapi`, `swconst`, `swpublishedapi`, `sldworksapiprogguide` — 19,064 topics. Obsolete APIs ship in a **separate `obsoleteapi.chm` that is deliberately not indexed**. If a name appears in old forum code but `find` returns nothing, it's likely obsolete; check that file directly before assuming the name is wrong.

Project-specific behaviour learned by experiment — what actually returns null, version quirks, workarounds — belongs in `docs/solidworks-api/`, not here. This file describes the API as shipped; that folder records what we've found out about it.
