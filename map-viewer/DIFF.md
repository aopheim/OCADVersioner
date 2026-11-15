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
| Object identity | The object's **entry in OCAD's object index** | OCAD keeps an object in its index entry for as long as it exists. The Angular app matched GeoJSON ids that `ocad2geojson` regenerates on every read ([ocad2geojson#25](https://github.com/perliedman/ocad2geojson/issues/25)). |
| Entry reused by OCAD | Same entry, different creation date → **deleted + added** | The creation date is what tells the old object from the new one. |
| Symbol changed, same entry | **Edited**, with the old and new symbol | It is the same object. A new entry is a new object, and is deleted + added. |
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
  OcadDiffService          the rules in the table above
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
rotation) are made by patching a copy's bytes in the test (`OcadBytes`). OCAD-drawn
fixtures for these would be better, and are worth adding when someone has OCAD to
hand: an area changed from 403 to 406, a rotated point, and a blank map with the
same symbol set.

The comparison tests build snapshots from the object index alone
(`Fixture.Snapshot`, at the fixtures' 1:15 000), because that is all the
comparison reads. Only `ParsedSnapshotTests` and `MapStoreSnapshotTests` load
PurplePen, for symbol names, scale and the store.

**The Windows application control policy described in `PLAN.md` gets in the way
here.** It blocks freshly built assemblies (`0x800711C7`), and its verdicts are
not stable. On the day this was written:
- the Release build of `Graphics2D.dll` was blocked from the start;
- the Debug build ran all 37 tests, then a few hours later its `MapModel.dll` was
  blocked;
- later still, the freshly built `OcadViewer.Diff.dll` itself was blocked, which
  stops every test in this suite.

So the last changes here (object bounds, index-only snapshots) compile and were
verified in the browser, where WebAssembly is not subject to the policy, but
their unit tests have not been run. `OcadViewer.ArchitectureTests` reads
assemblies without loading them and still ran (60/60).

```bash
dotnet test map-viewer/tests/OcadViewer.Diff.Tests                       # Debug, when the policy allows
dotnet test map-viewer/tests/OcadViewer.ArchitectureTests -c Release
```

## Not yet done

- **Symbols, colours, georeferencing and scale, templates and settings.** Next PRs.
  They come from the parsed `Map`, which already reads all of them. The interface
  will grow a result per category rather than overloading `ObjectDiff`.
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
