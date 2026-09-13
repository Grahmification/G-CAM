# The G-CAM tool library format (.gcamtools)

G-CAM's own format — the only one it writes. Read `.hsmlib` too, but never write it
([decision 0002](../decisions/0002-imported-libraries-are-read-only.md)).

A real example, converted from the HSMWorks library, is in
`docs/example_files/tool_library_hsmworks.gcamtools`.

```xml
<gcamToolLibrary version="1" units="mm" id="…" name="Shop tools">
  <holders>
    <holder id="…" name="TTS 3/4&quot; -ER20" vendor="…" productId="…">
      <section length="1.5" lowerDiameter="25.2" upperDiameter="33.88" />
    </holder>
  </holders>
  <tools>
    <tool id="…" number="4" name="Aluminum" type="FlatEndMill"
          material="carbide" holderId="…">
      <geometry diameter="12.7" cornerRadius="0" tipAngle="0" tipDiameter="0"
                fluteLength="31.75" shoulderLength="31.75" bodyLength="38"
                threadPitch="0" secondTipAngle="0" threadProfileAngle="0"
                fluteCount="3" shankDiameter="12.7" overallLength="60.53" />
      <cutting spindleRpm="7500" cuttingFeed="1800" plungeFeed="600"
               entryFeed="…" exitFeed="…" rampFeed="…" retractFeed="…"
               stepover="0" stepdown="0" rampSpindleRpm="7500"
               spindleClockwise="True" feedMode="PerMinute" coolant="Flood" />
      <machine diameterOffset="4" lengthOffset="4" turret="0"
               breakControl="True" manualToolChange="True" />
      <extra>
        <item key="hsm.type" value="flat end mill" />
      </extra>
    </tool>
  </tools>
</gcamToolLibrary>
```

## Rules

**Always millimetres and degrees on write.** The `units` attribute is read (`mm` or
`inch`) so a hand-edited file can declare inches, but the writer always emits `mm`.
Feeds are a length per minute and scale with the unit; spindle RPM does not.

**Always invariant culture.** Numbers are written with `"R"` under
`CultureInfo.InvariantCulture` and parsed the same way. A library written on a
comma-decimal machine has to load on a dot-decimal one — there is a test for awkward
values surviving a round trip.

**Holders are separate entries referenced by `holderId`.** A shop has a handful of
holders and hundreds of tools; duplicating holder geometry per tool would be wasteful
and would let copies drift. A dangling `holderId` is an error, not a warning — it fails
the load with a message naming the missing id.

**`<extra>` preserves what G-CAM does not model.** Fields from an imported library with
no G-CAM property are kept as key/value pairs, namespaced by source (`hsm.*`), and
written back out. Importing therefore never silently destroys data, and promoting one of
them to a real property later is a refactor with nothing lost in between.

**`type` values are the `ToolType` enum names.** Renaming a member is a file-format
change; add new members at the end.

## Versioning

`version="1"`. A reader refuses anything **newer** than it understands, with a message
saying so, rather than silently ignoring fields it cannot parse. Bump it only for a
change a version-1 reader could not cope with — adding an optional attribute is not one,
since missing attributes already read as zero or null.

## Writing is atomic

`LibrarySession` writes to `<path>.saving` and swaps, so a failure part-way through
cannot leave a half-written library where a good one was. If a `.saving` file is ever
left behind, a write was interrupted — the original is intact.
