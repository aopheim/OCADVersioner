# 3-way merge fixtures

Three small OCAD maps for testing the merge tool, drawn in OCAD 2020.8.6 and
saved in **OCAD 2018 format** (version 2018.1544), ISOM 2017-2 symbol set at 1:10 000.

| File | What it is |
| --- | --- |
| `base.ocd` | The common ancestor. |
| `kari.ocd` | A byte copy of `base.ocd`, then edited in OCAD as "Kari". |
| `ola.ocd` | A byte copy of `base.ocd`, then edited in OCAD as "Ola". |
| `dump.txt` | Every object in the three files, from `node ocddump.mjs base.ocd kari.ocd ola.ocd`. |
| `ocddump.mjs` | Dumps an OCAD 12+/2018 file's objects: slot, symbol, type, status, creation and modification date, geometry hash. |

## Objects in base

Identity is the **creation date** (millisecond precision); the slot number is not an identity.

| Slot | Symbol | Object | Created |
| --- | --- | --- | --- |
| 1 | 401 Open land | area, north-west | 22:32:13.139 |
| 2 | 406 | *already deleted in base* (status 3), must be ignored | 22:33:27.139 |
| 3 | 301 Uncrossable water | lake, south-west | 22:33:29.289 |
| 4 | 406 Slow running | forest, middle | 22:33:47.519 |
| 5 | 507 Less distinct footpath | path, west to east | 22:34:05.870 |
| 6 | 101 Contour | hill outline, east | 22:34:10.353 |
| 7 | 204 Boulder | middle | 22:34:11.987 |
| 8 | 204 Boulder | east | 22:34:12.570 |
| 9 | 109 Small knoll | on the hill | 22:34:21.605 |

## What each branch changed, and what a merge should report

| Base object | Kari | Ola | Expected |
| --- | --- | --- | --- |
| 1 Open land | — | NE vertex moved (extended east) | Clean: take Ola |
| 3 Lake | NE vertex moved (enlarged) | — | Clean: take Kari |
| 4 Slow running forest | NE vertex moved | S vertex moved | **Conflict: same object edited by both** |
| 5 Footpath | — | Modification date bumped, geometry and symbol identical | No change (a date alone is not an edit) |
| 7 Boulder (middle) | — | Deleted | Clean: delete |
| 8 Boulder (east) | Deleted | Moved ~2 mm | **Conflict: edit vs delete** |
| new | 508 Narrow ride, crosses the footpath (slot 10) | — | Clean add |
| new | 101 Contour inside the hill (slot 11) | — | **Spatial overlap** with Ola's knolls |
| new | — | 2 × 109 Small knoll on the hill (slots 10, 11) | **Spatial overlap** with Kari's contour |
| Symbol 109 definition | Description → "Small knoll, min 1 m (Kari)" | Description → "Knoll, small (Ola)" | **Conflict: symbol definition changed by both** |

Kari's slot 10 and Ola's slot 10 hold **different** new objects. A merge that
pairs objects by slot gets this wrong.

## What OCAD does to the file (observed while making these)

- **Editing** an object (moving vertices, moving a point) rewrites it in its own
  slot, **keeps CreationDate** and updates ModificationDate.
- **Deleting** leaves the object in its slot with index status 3. Readers must skip
  status ≠ 1 (2 = hidden).
- **New objects** take the next free slot, so slot numbers collide across branches.
- ModificationDate can change with no real edit (base slot 5 in `ola.ocd`), so
  "changed" must be decided from geometry, symbol and attributes, not from the date.
- ServerObjectId is −1 on every object (no OCAD Server), so it is no help.
- OCAD has **Object › Change Creation Date…**, so a user can break the identity by
  hand. Duplicate creation dates should be handled, not assumed impossible.

Not yet covered: cut/paste and duplicate (whether a pasted object keeps its
creation date), text objects, symbol graphics changes, and a symbol number added
by both branches.
