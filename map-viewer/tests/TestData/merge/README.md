# 3-way merge fixtures

Small OCAD maps for the merge engine's tests (`tests/OcadViewer.Merge.Tests`),
drawn in OCAD 2020.8.6, ISOM 2017-2 symbol set at 1:10 000. Saved in **OCAD
2018 format** unless the name says otherwise (OCAD 2019 and 2020 save as 2018).

| File | What it is |
| --- | --- |
| `base.ocd` | The common ancestor. |
| `kari.ocd`, `ola.ocd` | Byte copies of base, then edited in OCAD as "Kari" and "Ola". |
| `kari_settings.ocd`, `ola_settings.ocd` | Byte copies of base: both renamed colour 5 (brown) differently and both duplicated symbol 101 into 101.004 with different descriptions. |
| `identity_edits.ocd` | Base with the middle boulder cut and pasted, the east boulder copied and pasted, and the knoll duplicated. |
| `base_v12.ocd`, `base_v11.ocd` | Base saved as OCAD 12 and OCAD 11. |
| `dump.txt` | `node ocddump.mjs base.ocd kari.ocd ola.ocd`. |
| `ocddump.mjs` | Dumps an OCAD 12/2018 file's objects: slot, symbol, type, status, dates, geometry hash. |

## Objects in base

Identity is the **creation date** (millisecond precision). Slot numbers are not identities.

| Slot | Symbol | Object | Created |
| --- | --- | --- | --- |
| 1 | 401 Open land | area, north-west | 22:32:13.139 |
| 2 | 406 | *deleted while drawing* (index status 3), must be ignored | 22:33:27.139 |
| 3 | 301 Uncrossable water | lake, south-west | 22:33:29.289 |
| 4 | 406 Slow running | forest, middle | 22:33:47.520 |
| 5 | 507 Less distinct footpath | path, west to east | 22:34:05.870 |
| 6 | 101 Contour | hill outline, east | 22:34:10.353 |
| 7 | 204 Boulder | middle | 22:34:11.987 |
| 8 | 204 Boulder | east | 22:34:12.570 |
| 9 | 109 Small knoll | on the hill | 22:34:21.606 |

## What Kari and Ola changed, and what the merge reports

| Base object | Kari | Ola | Reported |
| --- | --- | --- | --- |
| 1 Open land | — | NE vertex moved | Clean, Ola |
| 3 Lake | NE vertex moved | — | Clean, Kari |
| 4 Forest | NE vertex moved | S vertex moved | **Conflict: both edited** |
| 5 Footpath | — | Modification date and colour field rewritten, nothing visible | Not a change |
| 7 Middle boulder | — | Deleted | Clean, Ola |
| 8 East boulder | Deleted | Moved ~2 mm | **Conflict: edit vs delete** |
| new | 508 Narrow ride (slot 10) | — | Clean, Kari |
| new | 101 Contour inside the hill (slot 11) | — | Clean, Kari |
| new | — | 2 × 109 Small knoll on the hill (slots 10, 11) | Clean, Ola |
| Symbol 109 | Description changed | Description changed differently | **Conflict: both edited** |

Overlaps (10 m buffer): Kari's contour with Ola's knolls on the hill; and the
whole west half - Ola's open land, Kari's lake, Kari's ride and the boulder Ola
deleted - because the fixture map is only ~400 m across and their extents touch.

Merging with the forest taken from Kari, the east boulder from Ola, symbol 109
from Kari and both sides of each overlap gives a file OCAD opens and that shows
exactly those choices (checked by hand in OCAD 2020).

## What OCAD does to a file (observed while making these)

- **Editing** rewrites an object in its own slot, keeps CreationDate, updates ModificationDate.
- **Deleting** leaves the record in its slot with index status 3.
- **New objects** take the next free slot, so slots collide across branches.
- **Cut and paste, copy and paste, and Duplicate all keep the creation date.** A copy shares its original's date.
- ModificationDate, and the colour field of objects that have a symbol, change with no visible edit.
- Saving as **OCAD 12** drops deleted records and renumbers slots; dates are kept.
- Saving as **OCAD 11** from OCAD 2020 writes **zero** creation dates.
- The display-mode parameter string (type 1024) is rewritten whenever the file is saved.
- ServerObjectId is −1 on every object without OCAD Server.
- OCAD has **Object › Change Creation Date…**, so a user can change an identity by hand.
