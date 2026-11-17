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
| `kari_v12.ocd`, `ola_v12.ocd`, `kari_v11.ocd`, `ola_v11.ocd` | Kari and Ola saved as OCAD 12 and OCAD 11 (File > Save As makes a copy). |
| `kari_symbols.ocd` | Base: symbol 204 duplicated into 204.002 with two objects drawn with it; a new 204 boulder; lake and knoll moved. |
| `ola_symbols.ocd` | Base: symbol 204 deleted, answering **Yes** to "delete the assigned objects too?" (the boulders get status 3); lake and knoll moved differently. |
| `ola_symbols_kept.ocd` | Base: symbol 204 deleted, answering **No** - the boulders stay, pointing at a missing symbol (OCAD draws red crosses, "204.000 unknown symbol"). |
| `texts.ocd`, `texts_edited.ocd` | Base plus an unformatted text "Storigarden" (901) and a formatted text "Langs stien" (902); the copy changes the first to "Storigarden nord". |
| `sprint_base.ocd`, `sprint_kari.ocd`, `sprint_ola.ocd` | A sprint map (ISSprOM 2019-2, 1:4000) playing out the same scenario as base/kari/ola; see below. |
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
- **Deleting a symbol** asks "Do you want also delete the assigned objects?". Yes gives the objects status 3; No leaves them pointing at a symbol that no longer exists.
- **Save As OCAD 11** reports the 2018 features it drops (structure patterns, cut at border) and saves a copy; the open map stays as it was.

## The sprint set

`sprint_base.ocd` was started with File > New > Orienteering map > "Orienteering
Map ISSprOM 2019 4000" (OCAD 2020.8.6) and drawn with exact sizes from OCAD's
Rectangle and Ellipse dialogs. Kari and Ola repeat, object for object, what
they do to the forest map, so `SprintMapTests` can run one scenario on both:

| Sprint object (created 2026-11-17 11:xx) | Plays | Kari | Ola | Reported |
| --- | --- | --- | --- | --- |
| 521 Building, west (08:59.454) | the forest | moved | moved | **Conflict: both edited** |
| 521 Building, east (09:20.618) | — | — | — | Not a change |
| 401 Open land (09:40.836) | the open land | — | moved | Clean, Ola |
| 301 Uncrossable water (10:15.419) | the lake | moved | — | Clean, Kari |
| 515 Uncrossable wall, a closed rectangle (12:33.824) | the footpath | — | — | Not a change |
| 417 Tree, middle (12:47.545) | the middle boulder | — | Deleted | Clean, Ola |
| 417 Tree, east (12:48.754) | the east boulder | Deleted | Moved | **Conflict: edit vs delete** |
| 418 Bush (13:00.243) | the knoll | — | — | Not a change |
| new | the ride and contour | 521 Building by the bush | — | Clean, Kari |
| new | the knolls | — | 2 × 418 Bush by the bush | Clean, Ola |
| Symbol 418 | symbol 109 | "Prominent bush (Kari)" | "Prominent bush (Ola)" | **Conflict: both edited** |

`sprint_kari.ocd` also holds a building drawn by mistake and undone (status 3),
which must be ignored.

## Symbols in use

| Merge | Reported |
| --- | --- |
| base, `kari_symbols`, base | Symbol 204.002 clean (Kari); kept while its two objects are kept, even if rejected. |
| base, `kari_symbols`, `ola_symbols` | Symbol 204 **conflict: symbol in use** (Kari drew a new boulder). Taking Ola's deletion removes Kari's boulder too. |
| base, base, `ola_symbols` | Symbol 204 clean (Ola): nobody else uses it. |
| base, base, `ola_symbols_kept` | Symbol 204 **conflict: symbol in use** (Ola's own boulders still use it). |
