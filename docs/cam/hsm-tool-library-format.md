# The HSMWorks tool library format (.hsmlib)

Everything below is **Verified** against a real Tormach library —
`docs/example_files/tool_library_hsmworks.hsmlib`, 11 tools — by reading it with
`HsmLibraryReader` and checking the numbers against each tool's own dimensions. The
format has no public specification that we have found, so the arithmetic *is* the
evidence.

G-CAM reads this format and does not write it; see
[decision 0002](../decisions/0002-imported-libraries-are-read-only.md).

## Shape of the file

```xml
<tool-table xmlns="http://www.hsmworks.com/xml/2004/cnc/tool-library" version="1.0">
  <tool version="1.1" type="flat end mill" unit="millimeters" guid="{…}" id="1">
    <description>Aluminum</description>
    <nc number="4" diameter-offset="4" length-offset="4" …/>
    <coolant mode="flood"/>
    <material name="carbide"/>
    <body diameter="12.7" flute-length="31.75" shoulder-length="31.75" …/>
    <holder description="TTS 3/4&quot; -ER20" guid="{…}">
      <section diameter="25.2" length="0"/>
      …
    </holder>
    <motion spindle-rpm="7500" cutting-feedrate="1800" …/>
  </tool>
</tool-table>
```

The document is **namespaced**, so every element lookup must be namespace-qualified.
Forgetting that produces an empty library rather than an error.

## The traps

### `taper-angle` means different things by tool type

The same attribute is an **included** angle on drills and spot drills, and a **half**
angle — measured from the tool axis — on chamfer mills. G-CAM normalises everything to
included on import.

The proof is each tool's own flute length. The `1/4" Chamfer Mill` has `diameter="6.35"`,
`tip-diameter="0"`, `taper-angle="45"` and `flute-length="3.175"`:

| Reading of 45 | Cone height | Matches flute length 3.175? |
| --- | --- | --- |
| Half angle | 3.175 / tan(45°) = **3.175** | Exactly |
| Included | 3.175 / tan(22.5°) = 7.665 | No |

And physically, a 45° chamfer mill cuts a 45° chamfer, which is 45° from the axis.
Drills go the other way: `taper-angle="118"` on a 2.5mm drill gives a 0.75mm point
height as an included angle, which is correct, and is meaningless as a half angle.

### `shoulder-length` is not `flute-length`

They are equal on ten of the eleven tools, which makes it easy to assume they always
are. The chamfer mill is the exception: `flute-length="3.175"` but
`shoulder-length="26"`. The cutting cone is 3.175mm; the body continues straight at full
diameter for another 23mm before the shank.

**The silhouette must run to the shoulder length**, not the flute length. Stopping at
the flute length draws a cone floating in mid-air, and — more seriously — under-models
the tool by 23mm for collision purposes. This was a real bug, now covered by a
regression test.

### `body-length` is the stickout

`overall-length − body-length` is the portion gripped by the holder. Across four tools
that gives 22.53, 14.56, 43.0 and 22.33mm — all plausible collet grips, which only works
if `body-length` is the exposed part. **Assumed**, on that arithmetic; verify against a
measured tool before trusting it for collision checking.

### Two different tool numbers

`<tool id="1">` is a library index. `<nc number="4">` is the carousel position the
machine uses. The second is what belongs in a posted `T` word — confusing them puts the
wrong tool in the program.

### Units are per tool, not per file

`unit="millimeters"` or `unit="inches"` sits on each `<tool>`. A library may mix them.
Feed rates are a length per minute and scale with the unit; spindle RPM does not.

### Holder sections are a cumulative profile

Each `<section>` gives a diameter and the axial length over which the profile *reaches*
it, walking from the tool end upward. A `length="0"` section is an instantaneous step in
diameter, not a zero-height stage:

```
<section diameter="25.2"  length="0"/>     start at Ø25.2
<section diameter="33.88" length="1.5"/>   cone Ø25.2 → Ø33.88 over 1.5mm
<section diameter="33.88" length="16.3"/>  cylinder Ø33.88 for 16.3mm
<section diameter="24.7"  length="0"/>     step down to Ø24.7
```

The TTS holder in the example has 15 sections, five of them steps, giving ten real
stages totalling 87.99mm.

The same holder `guid` repeats across tools, so holders are de-duplicated on import and
shared — matching G-CAM's model, where a shop has a handful of holders and many tools.

## What HSM stores that G-CAM does not, and vice versa

**HSM has no stepover or stepdown on the tool** — they live on the operation. Imported
tools therefore arrive with both at zero and need filling in before use.

Anything G-CAM has no property for is kept in `Tool.Extra` under an `hsm.` prefix and
written back out to the native format, so importing never silently destroys data.

## Tool types

Mapped: flat / ball / bull nose end mill, radius mill, chamfer mill, spot drill, center
drill, drill, tap (both hands). Anything else — reamer, thread mill, face mill, boring
bar — is **reported as a skipped tool by name**, never substituted for the nearest
shape. A tap silently machined as a drill wrecks a part, and the substitution would be
invisible afterwards.
