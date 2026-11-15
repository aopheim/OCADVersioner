# The C# diff

> **Status: objects done, wired into the diff table.** Symbols, colours,
> georeferencing and templates are next, one PR each, behind the same interface.

The diff answers *which map objects were added, edited and deleted between two
versions*. It replaces the Angular app's `json-diff-service.ts`, which compares two
GeoJSON files produced by `ocad2geojson`. This one reads the `.ocd` files themselves.

Read this beside `PLAN.md`, whose section *The diff — deferred* records why it was
put off and what was known at the time. That section is still true about the
change overlay; this document supersedes it for the diff.

## Decisions

| Question | Decision | Why |
|---|---|---|
| Input | The `.ocd` bytes plus the parsed PurplePen `Map` | No `ocad2geojson`, no second parser, no AGPL dependency. |
| Object identity | Matched in three passes, most certain first (`ObjectMatcher`): **identical content in the same entry**, then **identical content in any entry**, then **the same creation date** | Within one OCAD file an object keeps its index entry. But a map rebuilt by copying its objects into another file gets every object a new entry, and can have every date shifted by whole hours. Bymarkabasen did both (see below), so the entry is evidence, not identity. The Angular app matched GeoJSON ids that `ocad2geojson` regenerates on every read ([ocad2geojson#25](https://github.com/perliedman/ocad2geojson/issues/25)). |
| Pairing by creation date | Same creation date to the millisecond, after any **whole-hour shift** shared by most identical objects. Then either the **same entry** (when most identical objects kept theirs), or **within 10 mm on paper and modified since** | An edit keeps the creation date. Imports stamp thousands of objects with one date, so the date alone is not enough. Among several candidates, the same symbol wins, then the same entry, then the nearest. |
| Different creation date | **Deleted + added**, whatever the entry | Creation dates are copied with an object, not invented, so a different date means a different object. |
| Symbol changed | **Edited**, with the old and new symbol | It is the same object, if the rules above pair it. |
| Deleted / hidden objects | **Ignored**; only OCAD's *normal* status counts | OCAD keeps deleted objects in the file for undo. An object that turns from normal to deleted is reported deleted. |
| What an edit carries | Point movement (m), line length change (%), area change (%), and **which properties changed**: symbol, geometry, angle, text | |
| Curves | Control points skipped; the chord between on-curve points is measured | Good for gentle curves, short on tight ones. Proper flattening (the Angular app's [#23](https://github.com/aopheim/OCADVersioner/issues/23)) is not done. |
| First release (no predecessor) | Every object is **added** | Same as the Angular app. |
| OCAD versions | 9, 10, 11, 12, 2018. OCAD 9 has no timestamps, so the entry alone pairs objects | OCAD 8 and older use a different index layout. OpenOrienteering maps are shown but not compared. |
| Vendored code | **Untouched** | PurplePen's importer reads index entries, status and timestamps, then throws them away. Rather than patch it, `OcadObjectIndexReader` reads the index itself, following PurplePen's `OcadFileFormat.cs` as the specification. |

## Shape

```
OcadViewer.Diff            references MapModel only - no renderer, no Skia, no host
  OcadObjectIndexReader    .ocd bytes -> every object: index entry, status, symbol,
                           kind, angle, text, created/modified, coordinates
  OcadMapSnapshot          one version: its normal objects by index entry, symbol
                           names from the Map, metres per OCAD unit from the scale
  IOcadDiffService         Compare(older?, newer) -> ObjectDiff
  ObjectMatcher            which old object each new one is: content, entry,
                           creation date (the table above)
  OcadDiffService          pairs -> edits with measures; the rest added/deleted
  ObjectDiff               Added / Edited / Deleted, each with ObjectSymbol, dates
                           and measures

OcadViewer.Core
  MapStore.Load            takes a snapshot beside every parsed map, while the
                           bytes are in hand; StoredMap.Snapshot (null when the
                           diff cannot read the file)

OcadViewer.Wasm
  LoadedMaps               the page's one MapStore, shared by the tile shim and
                           the diff, so a version is parsed once
  OcadDiffSource           IDiffSource: snapshots from the store -> table rows
  DiffTable.razor          gains a "Changes" column on the Edited tab, and a zoom
                           button per row that frames the object on both maps
```

Every result carries `PaperBounds`: the box around the object's coordinates in
paper millimetres (OCAD units ÷ 100, Y up, as PurplePen draws them). An edited
object's box covers where it was and where it is now. The row's zoom button
passes it to `mapWorkspace.zoomTo`, which pads it by 8 mm so a point gets a
sensible frame. Each row also carries the box as `data-bounds`, which is how the
table was checked against the change overlay (below).

The snapshot is taken at load, not at comparison, because only the load sees the
bytes. MapStore keeps maps by version name, so comparing V3 with V2 after V2 with
V1 reads and parses nothing new.

`ProjectDependencyTests` records the new edges (`Diff → MapModel`,
`Core → Diff`, `Wasm → Diff`, `Diff.Tests → Core, Diff`), and
`AssemblyDependencyTests.TheDiffKnowsNothingAboutRenderingOrTheBrowser` checks the
compiled assembly.

## Tests

`tests/OcadViewer.Diff.Tests` (NUnit) drives everything with real `.ocd` files in
`Fixtures/`, copied from `ocad-versioner/src/assets/tests` so they outlive the
Angular app. What each contains, as read from its object index:

| Fixture | Normal objects |
|---|---|
| `1knoll` | entry 1: knoll 109.0 |
| `point_line_area` | entries 3, 4, 5: contour 101.0, knoll 109.0, area 403.0. Entry 1's knoll is now deleted. |
| `point_line_area_moved` | the same three entries and creation dates, each edited |
| `point_line_area_deleted` | none; all five entries kept for undo |

Cases no OCAD-saved fixture shows (a reused index entry, a symbol swap, a
rotation, objects renumbered, dates shifted) are made by patching a copy's bytes
in the test (`OcadBytes`). `RenumberedObjectsTests` rebuilds the Bymarkabasen
situations in miniature this way: entries swapped, an object moved to an unused
entry, all dates two hours earlier, edits under a new number, a batch of objects
sharing one creation date. OCAD-drawn
fixtures for these would be better, and are worth adding when someone has OCAD to
hand: an area changed from 403 to 406, a rotated point, and a blank map with the
same symbol set.

The comparison tests build snapshots from the object index alone
(`Fixture.Snapshot`, at the fixtures' 1:15 000), because that is all the
comparison reads. Only `ParsedSnapshotTests` and `MapStoreSnapshotTests` load
PurplePen, for symbol names, scale and the store.

### Real maps, as a baseline that is never committed

`RealMapTests` runs the diff on real projects, release by release: Bymarkabasen
and Storigarden. The maps are copyrighted, so **they are never copied into this
repository**. The tests read them in place and only read them. They look in the
folder named by `OCAD_REAL_MAPS` (default `C:\repos`: `BIL-kart\Bymarkabasen`,
`ocad-storigarden`), and are ignored on any machine without them, CI included.
Each pair checks three things:

- the **baseline counts**, each checked by hand when recorded (below). A rule
  change that moves one has to explain why;
- **the counts add up**: added − deleted equals the change in object count;
- **nothing identical is reported as deleted and added**.

```bash
dotnet test map-viewer/tests/OcadViewer.Diff.Tests                       # Debug
dotnet test map-viewer/tests/OcadViewer.Diff.Tests --filter TestCategory=RealMaps
dotnet test map-viewer/tests/OcadViewer.ArchitectureTests -c Release
```

**The Windows application control policy described in `PLAN.md` gets in the way
here.** It blocks freshly built assemblies (`0x800711C7`), and its verdicts are
not stable. Over one day it blocked the Release `Graphics2D.dll`, then the Debug
`MapModel.dll`, then `OcadViewer.Diff.dll` itself. The next day it let the whole
suite run again. When it blocks, check in the browser instead (below), where
WebAssembly is not subject to the policy.

## Not yet done

- **Symbols, colours, georeferencing and scale, templates and settings.** Next PRs.
  They come from the parsed `Map`, which already reads all of them. The interface
  will grow a result per category rather than overloading `ObjectDiff`. The
  symbol diff must include a symbol shown or hidden (its status): in Bymarkabasen
  V1 → V2 that alone redrew a boundary line around the whole map.
- **Version metadata counts.** `ProjectService.CreateVersionAsync` still writes
  zero added/edited/deleted counts into `V<n>.json`. Filling them from this diff
  needs the working file and the newest release both parsed when a version is
  created.
- **Exact curve lengths**, see above.

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
entry. Between V1 and V2 every date also moved by exactly two hours. Both look
like a map rebuilt by copying its objects into another file. All checks below
were run on read-only copies, and the maps are never committed.

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
