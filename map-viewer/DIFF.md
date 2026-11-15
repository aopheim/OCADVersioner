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
  DiffTable.razor          gains a "Changes" column on the Edited tab
```

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

**Run the suite in Debug on this machine.** The Windows application control policy
described in `PLAN.md` blocks the Release build of `Graphics2D.dll`
(`0x800711C7`), and this suite loads the vendored assemblies. The Debug build is
not blocked. `OcadViewer.ArchitectureTests` never loads them and runs either way.

```bash
dotnet test map-viewer/tests/OcadViewer.Diff.Tests                       # Debug
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
- **Checked in the browser by hand.** The folder picker cannot be automated, so the
  table has only been verified through the tests and a start-up check of the
  published app.
