---
name: solidworks-api
description: Look up the SOLIDWORKS 2025 API offline - interfaces, methods, properties, enums, and code examples - from the local help installed with SOLIDWORKS. Use whenever you need a signature, parameter meaning, enum value, or the Remarks for any ISldWorks/IModelDoc2/IBody2/ICommandManager/swXxx_e member, instead of guessing or fetching help.solidworks.com.
---

# SOLIDWORKS 2025 API reference (offline)

`swapi.py` reads the CHM help shipped with SOLIDWORKS 2025 SP3 and prints one topic as clean text. Prefer it over the web help: it is the exact version installed on this machine, needs no network, and costs roughly an eighth of the tokens of the equivalent web page.

**Always look up a signature before writing a call.** This API has many near-identical overloads (`CreateCommandGroup` / `CreateCommandGroup2`), silently different units, and load-bearing Remarks.

## Setup (once per machine)

```bash
python .claude/skills/solidworks-api/swapi.py setup
```

Extracts to `%LOCALAPPDATA%\G-CAM\swapi-help` (~19k topics, outside the repo). Every command below errors with a reminder if this hasn't been run.

## Commands

Run from the repo root; `SW=.claude/skills/solidworks-api/swapi.py`.

```bash
python $SW show ICommandManager.CreateCommandGroup2   # full topic: syntax, params, return, remarks
python $SW show swBodyType_e                          # enum with every member and its integer value
python $SW members IBody2                             # every method/property on an interface
python $SW find "GetTessTriangles"                    # locate a topic by partial name (regex ok)
python $SW grep "tessellat" --type IFace2             # full-text search across topic bodies
```

- `show` with an ambiguous query prints the closest match plus the other candidates — cheap to guess a name and let it disambiguate.
- `grep` without `--type` searches everything including the several thousand worked code examples; use `--limit` to keep output small.
- All commands default to C# and hide VB/VBA/C++ pages and signatures. `--all-langs` brings them back.

## Reading the output

**Topic names** are `Type.Member` (`IModelDoc2.Extension`, `swDocumentTypes_e`). In Remarks the help writes `IBody2::GetFaces`; the equivalent query is `IBody2.GetFaces`.

**Signatures** come from the doc generator, so types print as `System.int` / `System.string` / `System.object`. Write plain `int`, `string`, `object` in C#. `out`/`ByRef` parameters are shown correctly.

**`I`-prefixed twins** (`GetFaces` vs `IGetFaces`) are not versions. Verified in the 2025 help: the `I` variant returns a raw pointer for in-process unmanaged C++, the plain one returns a safe array. **From C#, always use the non-prefixed method.**

**Remarks is the section that matters.** It carries the constraints that aren't in the signature — required call order, registry side effects, unit conventions, and accuracy caveats. For a CAM add-in these are often decisive: `IFace2.GetTessTriangles`, for example, documents that its tessellation is display-only and explicitly *not* suitable for machining, which is the kind of thing that costs days if discovered late.

## Books searched

| Book | Covers |
| --- | --- |
| `sldworksapi` | Interfaces, methods, properties — the bulk of the API |
| `swconst` | Enums and constants (`swBodyType_e`, `swDocumentTypes_e`) |
| `swpublishedapi` | Interfaces the add-in *implements* (`ISwAddin`, PropertyManager page handlers) |
| `sldworksapiprogguide` | Conceptual overviews (`Return_Values`, `COM_vs_Dispatch`, `Understanding_the_SolidWorks_API_Class_Hierarchy`) |

Name collisions resolve in that order.

## When the local help falls short

It documents *what* the API declares, not *how it behaves*. For version-specific bugs, undocumented behaviour, or "why does this return null", fall back to the web — the [2025 API help](https://help.solidworks.com/2025/english/api/help_list.htm?id=2) and the SOLIDWORKS API forum. Anything learned that way belongs in `docs/solidworks-api/`, tagged with how it was confirmed.
