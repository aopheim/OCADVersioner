# The diff table

> **Status: objects, on OcadViewer.Comparison.** Which object is which, whether
> it changed and how much are decided in `OcadViewer.Comparison` - the same
> answers the merge gets. See **COMPARISON.md** for those rules and why. This
> document is about the table in the Blazor app that shows them.

The table answers *which map objects were added, edited and deleted between two
versions*. It replaces the Angular app's `json-diff-service.ts`, which compared two
GeoJSON files produced by `ocad2geojson`.

An earlier version of this branch had its own reader, matcher and snapshot
(`OcadViewer.Diff`). The architectural review found they disagreed with the merge
engine on the same files, so they were replaced by `OcadViewer.Comparison`.
The sections further down that check the table in the browser record what was
measured with that earlier engine; the method still applies.

## Shape

```
OcadViewer.Comparison      (see COMPARISON.md)
  VersionDiff.Compare      older?, newer OcadFile -> Added / Changed / Deleted,
                           with which fields changed, metres, m², movement, bounds

OcadViewer.Wasm
  ComparisonCache          one OcadFile per version, read through ProjectService;
                           releases once, the working file every time
  OcadDiffSource           IDiffSource: versions from the cache -> table rows;
                           returns a DiffResult (rows, or why there are none)
  DiffTable.razor          Added / Edited / Deleted tabs, a Changes column on the
                           Edited tab, a zoom button per row
  Workspace.razor          clears the table when a new pair starts loading, and
                           shows a result only if its pair is still the one asked for
```

The renderer's `MapStore` knows nothing about comparing. The price is that each
compared version's bytes are read twice from the open folder: once by the
renderer in JavaScript, once by the cache.

Every row carries a rectangle in paper millimetres (OCAD units ÷ 100, Y up, as
PurplePen draws them). An edited object's covers where it was and where it is
now. The zoom button passes it to `mapWorkspace.zoomTo`, which pads it by 8 mm
so a point gets a sensible frame. Each row also carries it as `data-bounds`,
which is how the table was checked against the change overlay (below).

A version shown against itself means two things. Picked from the list, it is the
oldest release, which has nothing before it, so every object is added - as in the
Angular app. Picked in the dropdowns, it is an ordinary comparison with no changes.

## Tests

The comparison is tested in `tests/OcadViewer.Comparison.Tests`, on the files in
`tests/TestData/diff`. What each contains, as read from its object index:

| Fixture | Normal objects |
|---|---|
| `1knoll` | entry 1: knoll 109.0 |
| `point_line_area` | entries 3, 4, 5: contour 101.0, knoll 109.0, area 403.0. Entry 1's knoll is now deleted. |
| `point_line_area_moved` | the same three entries and creation dates, each edited |
| `point_line_area_deleted` | none; all five entries kept for undo |

Made from those in OCAD 2020.8, which still saves the 2018 format:

| Fixture | How it was made | What OCAD wrote |
|---|---|---|
| `point_line_area_406` | area changed with Change Symbol | entry 5 now 406.0, modification date moved |
| `1knoll_rotated` | knoll rotated 45° | angle 450 (tenths), same point, modification date moved |
| `blank` | File, New, symbols loaded from `point_line_area` | no index entries at all |
| `point_line_area_imported` | `point_line_area` imported into `blank`, offset 0 | entries 1-3 instead of 3-5, same coordinates and creation dates, new modification dates |
| `point_line_area_pasted` | its objects copied and pasted into `blank` | as imported, but every object shifted by (-1.27, +1.18) mm |
| `point_line_area_app_exchange` | File › OCAD App Data Exchange opened, nothing exported | OCAD saved first: entries 1-3, undo entries gone, every date -1 h (to UTC), and header byte 55 set on every object |
| `point_line_area_optimized` | Map › Optimize / Repair | entries 1-3, undo entries gone, dates kept |
| `point_line_area_cut` | contour cut in two (Cut Line) | entry 3 one half; new entry 6 the other, **with the contour's creation date** |
| `point_line_area_duplicated` | knoll duplicated | new entry 6, **with the original's creation date** |
| `point_line_area_text` | text "Knoll" (901.0) added | entry 6: anchor point plus four box corners |
| `point_line_area_text_edited` | the text changed to "Knolls" | same entry and creation date; the box widened |
| `point_line_area_symbol_hidden` | Symbol › Hide Objects on 403.0 | symbol 403.0's status 0 → 2; no object changed |

The app side - the cache, the rows, the page - has no unit tests; it is checked in
the browser (below).

## Not yet done

- **Real-map baselines.** The earlier branch had `RealMapTests`, which diffed
  Bymarkabasen and Storigarden in place (copyrighted, never committed) against
  hand-checked counts. They went with `OcadViewer.Diff`. Their counts were for the
  earlier matcher, so they have to be re-recorded on Comparison before coming back.
- **Symbols, colours, georeferencing and scale, templates and settings.** Next
  PRs, as further results from Comparison. A symbol hidden in the symbol box is
  *not* reported: OCAD sets that status itself (COMPARISON.md).
- **Version metadata counts.** `ProjectService.CreateVersionAsync` still writes
  zero added/edited/deleted counts into `V<n>.json`. The cache can now provide
  both files when a version is created.
- **Exact curve lengths.** Control points are skipped and the chord measured.

## Checking the table in the browser, without the folder picker

`PLAN.md` says anything behind "Open project folder" needs a human, because
`showDirectoryPicker` opens a native dialog. It does not have to: Chromium's
origin private file system hands out real `FileSystemDirectoryHandle`s. Write a
project into it and have the picker return that handle, and everything after the
picker runs the app's own code on real handles.

Publish without AOT and serve it (see `PLAN.md`). Put a project's files under
`wwwroot/testproject/` in the published output (`mymap.ocd`, `V1.ocd`, `V1.json`,
`V2.ocd`, `V2.json`), open the page, and paste in the console:

```js
const root = await navigator.storage.getDirectory();
try { await root.removeEntry('ocad-testproject', { recursive: true }); } catch {}
const proj = await root.getDirectoryHandle('ocad-testproject', { create: true });
async function put(dir, name, url) {
  const fh = await dir.getFileHandle(name, { create: true });
  const w = await fh.createWritable();
  await w.write(await (await fetch(url)).arrayBuffer());
  await w.close();
}
await put(proj, 'mymap.ocd', '/testproject/mymap.ocd');
const rel = await proj.getDirectoryHandle('Releases', { create: true });
for (const v of ['V1', 'V2']) {
  const d = await rel.getDirectoryHandle(v, { create: true });
  await put(d, `${v}.ocd`, `/testproject/${v}.ocd`);
  await put(d, `${v}.json`, `/testproject/${v}.json`);
}
window.showDirectoryPicker = async () => proj;
```

Then press "Choose directory". Checked this way with `mymap` = `point_line_area_moved`,
V1 = `1knoll`, V2 = `point_line_area`:

| Comparison | Table |
|---|---|
| V2 → working file | Edited 3: contour +47.6 % length, knoll moved 78 m, area +20.9 %; all *Geometry* |
| V1 → V2 | Added 3 (contour 250 m, knoll, area 8 386 m²), Deleted 1 (the V1 knoll) |
| V1 alone | Added 1 (the knoll) |

Symbol names come from the map ("Small knoll", "Rough open land"), and no
console errors.

### A real project: Storigarden

The same check against the Storigarden project (six releases and a working file,
OCAD 2018, about 6.6 MB and 2 900 objects each), from copies of its files:

| Comparison | C# diff (added / edited / deleted) | Angular's counts in `V<n>.json` |
|---|---|---|
| V6 → working file | 0 / 0 / 0 (the files are byte-identical) | — |
| V5 → V6 | 14 / 2 / 1 | 16 / 4 / 3 |
| V4 → V5 | 29 / 5 / 2 | **696 / 284 / 669** |
| V3 → V4 | 7 / 5 / 0 | 7 / 5 / 0 |
| V2 → V3 | 8 / 3 / 1 | 13 / 2 / 6 |
| V1 → V2 | 20 / 3 / 0 | 0 / 0 / 0 (nothing recorded) |
| V1 alone | 2 882 added | — |

Where the two disagree, Angular's extra additions equal its extra deletions (2, 5,
667): untouched objects counted as deleted and re-added because their GeoJSON ids
moved. That is the ocad2geojson id problem this diff exists to avoid, and V3 → V4,
where nothing shifted, matches exactly.

Every row was also checked against the release dates: no added object was
created before the older release, every edited object was last changed after it,
and no deleted object was created after the newer one. That covers 84 added, 18
edited and 4 deleted objects. Switching versions took about 3.4 s each on the
non-AOT build. That includes parsing a 6.6 MB map and redrawing; the diff's own
share was not measured.

### The table against the change overlay

The overlay (`MapStore.RenderChangeMask`) knows nothing about objects: it renders
both versions and marks every pixel that differs. That makes it an independent
check on the table, in both directions:

- **Nothing missed:** every red pixel should fall inside the box of some row in
  the table, padded a little because a box bounds coordinates, not ink.
- **Nothing invisible:** every row's box should hold at least one red pixel.

Run in the browser from the mask (`PlanChangeMask` / `RenderChangeMask` through
`dotnetExports.js`) and the rows' `data-bounds`:

| Pair | Red pixels | Inside a row's box, padded 0.25 mm | padded 0.5 mm | Rows with no red pixels |
|---|---|---|---|---|
| fixtures V2 → working file | 328 | 99.4 % | 100 % | — |
| Storigarden V1 → V2 | 804 | 100 % | 100 % | 0 of 23 |
| V2 → V3 | 679 | 100 % | 100 % | 0 of 12 |
| V3 → V4 | 3 520 | 100 % | 100 % | 0 of 12 |
| V4 → V5 | 3 737 | 100 % | 100 % | 0 of 36 |
| V5 → V6 | 4 018 | 96.1 % | 100 % | 0 of 17 |

At 0.5 mm, the width of a thick line, the two agree completely on every pair.
That answers the question the Storigarden check above left open: nothing
visible changed that the table does not list.

Its limit is what boxes are: a large area's box covers ground the area does not,
so a missed object lying inside a big reported box would go unnoticed. And a
change that draws nothing different (an edit undone, a hidden object) cannot
show up in the overlay at all.

### A rebuilt project: Bymarkabasen

Bymarkabasen (three releases and a working file, OCAD 2018, about 23 000
objects each) is what showed that the index entry is not enough. Between V1
and V2, and again between V2 and V3, nearly every object moved to a new index
entry. Between V1 and V2 every date also moved by exactly two hours. All checks
below were run on read-only copies, and the maps are never committed.

**The cause is the OCAD App.** Opening File › OCAD App Data Exchange makes
OCAD save the map first, and that save renumbers every object, drops the undo
entries and moves every date from local time to UTC. OCAD's exchange folder
(`%APPDATA%\OCAD\OCAD 2018\Tmp\_OcadAppExchangeData`) still holds Bymarkabasen's
export from 4 June, twelve minutes after V1 was released. Against V1 its
22 447 objects are all unchanged in content, 21 044 are under another entry,
and every date is two hours earlier (summer time, UTC+2). V2 then has the
export's dates, but renumbered once more by the import back.
`point_line_area_app_exchange` is the same save made in November: one hour
(UTC+1). Map › Optimize / Repair renumbers too, without touching dates.

| Comparison | Matched by entry alone | Now |
|---|---|---|
| V1 → V2 | 23 415 / 0 / 22 447 | **1 288 / 889 / 320** |
| V2 → V3 | 19 465 / 4 119 / 19 096 | **472 / 364 / 103** |
| V3 → working file | 0 / 0 / 1 | 0 / 0 / 1 (a 2.8 km² *Forbudt område*, 709.1) |

How the new counts were checked:

- **The counts add up, and nothing identical is listed as gone and back.**
  `RealMapTests` checks both on every pair.
- **Release dates.** Every added or edited object was last changed after the
  older release, and no deleted object was created after the newer one. The
  only exceptions are 6 imported objects in V2 → V3, from a batch dated 31
  August that differs between the two files. They are listed as added, not as
  edits. The first version of the matcher did pair them as edits, which is why
  a creation-date pair now needs a later modification date.
- **The change overlay, object by object.** Boxes are too coarse here: one new
  forbidden area spans a third of the map. So every red pixel was tested against
  the actual geometry instead, with curves flattened. Each pixel had to lie on
  (within 1.5 mm) or inside an object that has no identical twin in the other
  file, or on a symbol whose definition changed. This check reads the raw files
  and the mask, and never asks the matcher:

  | Pair | Red pixels | Explained | Left |
  |---|---|---|---|
  | V1 → V2 | 204 420 | 99.55 % | 923 px in specks, at most 20 per 5 mm square |
  | V2 → V3 | 389 950 | 99.995 % | 19 px |

  A missed object would leave a solid patch, and none is left.
- **What the table rightly leaves out.** In V1 → V2, 19 % of the red pixels sit
  outside every row's box. Almost all of them (37 816 of 38 432) lie on objects
  whose symbol changed, not the object:
  - symbol 708.0 (out-of-bounds boundary) had status *hidden* in V1 and *normal*
    in V2, so an identical line appears;
  - 508.10, 101.2 and 711 changed definition.

  Those are symbol changes, the next slice of the diff. A symbol's status is
  byte 11 of its record. Bytes 10 and 20–22 differ in every symbol and change
  nothing drawn.
