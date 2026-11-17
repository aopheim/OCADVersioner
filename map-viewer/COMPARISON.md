# Comparing two versions of an OCAD map

`OcadViewer.Comparison` answers the two questions every feature that compares
maps has to ask, once:

1. **Which object in this version is which object in that one?** (identity)
2. **Has it changed, and what about it?** (content)

The 3-way merge (`OcadViewer.Merge`, see MERGE-PLAN.md) and the diff table in
the Blazor app both ask them. Before this project each had its own answer, and
the answers disagreed: hiding an object was *deleted* in the table and
*modified* in the merge, and a boulder moved in an OCAD 11 file was *edited* in
one and *deleted + added* in the other. A user looking at the table and then
merging the same versions would have been told two different stories.

## Shape

```
Ocd  ←  Comparison  ←  Merge
             ↑
            Wasm (diff table)
```

- **Ocd** reads and edits the file. It knows where every field is in each OCAD
  version, and nothing about what a change means.
- **Comparison** decides identity and change, and measures what changed. It
  depends on Ocd only, and is domain code: no browser, no renderer, no packages.
- **Merge** keeps what is merge-specific: the 3-way rule, conflicts, decisions
  and writing.
- **The app** keeps one parsed `OcadFile` per version in a cache of its own,
  read from the bytes it already has. The renderer's `MapStore` knows nothing
  about comparing.

The architecture tests hold this in place: Comparison may reference Ocd and
nothing else, and no other project matches objects or reads the object index.

## Decisions

| Question | Answer | Why |
|---|---|---|
| Identity | Creation date first, then identical geometry. The index entry is never used. | A new object takes the next free entry, so the same entry holds unrelated objects in two versions. Bymarkabasen renumbered nearly every object between releases. |
| Dates shifted by whole hours | Allowed for, in both engines. | A map rebuilt by copying its objects into another file can shift every date by the hours between local time and UTC. Without this, every object in such a version is deleted and re-added. |
| Many objects with one creation date | Exact content pairs first; the rest by nearness through a spatial grid. | An import stamps thousands of objects with one date. Pairing every one with every other is quadratic, and the diff runs on the browser's only thread. |
| What counts as changed | `ContentComparer`: the whole record except dates, the colour field of symbol objects and the reserved byte at offset 55, with coordinates within 0.1 m. | One definition for the table and the merge. Hiding an object is a change; OCAD re-saving a vertex one unit away is not. OCAD sets byte 55 on every object when a map is opened for App Data Exchange (`point_line_area_app_exchange.ocd`), with nothing edited. |
| What changed about it | The comparer reports which fields differ (symbol, geometry, angle, text, status, other). "Same" means no field differs. | The table's Changes column and the merge's yes/no come from one rule, so they cannot drift. |
| Files of different OCAD versions | Compared field by field where the layouts differ, and always reported as changed in *other*. | The merge refuses to mix versions; the table should still say what moved when a map was saved in a newer OCAD between releases. |
| Hiding a symbol in the symbol box | Noise, not a change. | OCAD sets the symbol-box status itself. |
| OCAD 9 and 10 | Not compared. | Ocd reads 11, 12 and 2018 onwards. Older maps are still shown, with "cannot be compared". |
| Lengths, areas, movement, bounds | Measured in Comparison. | Testable without a browser; the table only formats. |
| Speed | Synchronous, held to a budget by a test on a large synthetic map. | A worker is a large piece of work in Blazor WebAssembly. Revisit only if real maps exceed the budget. |

## Test data

All `.ocd` fixtures live under `tests/TestData`: `merge/` as drawn for the
merge, `diff/` for the two-way diff. One shared `Fixtures` helper reads them.
The 9.8 MB benchmark map is `tests/TestData/large_map.ocd`, git-ignored.

## Order of work

1. This project, from `develop`: move `Merge.Matching` here, add the hour shift,
   the field-level answer, grid pairing and the two-way diff, and consolidate
   the fixtures. The merge's tests stay green.
2. Rework the diff table (#141) on top of it: drop its own reader, matcher and
   snapshot, and read through the app's comparison cache.
