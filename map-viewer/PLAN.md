# Replace the Angular app with a Blazor app that renders OCAD maps with PurplePen's renderer

> **Status: Stages 0–5, 7 and 8 complete, plus the parity cleanup. Stage 6
> part-done (AOT). Stage 9 is next.**
> **End goal: a Blazor WebAssembly app that replaces the Angular app entirely.**
> It is built side by side with Angular, which keeps serving users until Blazor
> reaches parity, and is then retired. The map viewer inside it is a **Leaflet tile
> layer** driven through JS interop, so `leaflet.sync` gives the synced side-by-side
> views unchanged. See *Architecture*.
>
> The C# diff is **deferred, not scheduled** — see *The diff — deferred*.

## Resuming in a new session

Requires the .NET 10 SDK and the `wasm-tools` workload (`dotnet workload install wasm-tools`).

```bash
dotnet build map-viewer/MapViewer.slnx -c Release                       # everything
dotnet run --project map-viewer/src/OcadViewer.Wasm -c Release          # the app, :5001
pwsh map-viewer/tools/sync-purplepen.ps1 -PurplePenPath <path> -Check   # vendor drift
```

`/` is the workspace and `/about` carries the third-party notices. Those, plus
`/not-found`, are the whole app: it is the Angular app's feature set and nothing
else.

**The `/smoke` diagnostic page is gone**, removed in the parity cleanup along with
the rest of the build scaffolding. It was nine checks over Skia, fonts, text
metrics and blending, and it was the only thing pulling in `SkiaSharp.Views.Blazor`
— a package the app does not otherwise need, since tiles are rendered offscreen
and pushed through `putImageData` rather than drawn by a Blazor canvas component.

What it used to prove is now provable from the console in one paste, against the
running app and the real render path:

```js
const { getTileInterop } = await import('/js/dotnetExports.js');
const interop = await getTileInterop();
const bytes = new Uint8Array(await (await fetch('/probe.ocd')).arrayBuffer());
const info = JSON.parse(interop.LoadMap(0, 'probe.ocd', bytes));
const px = interop.RenderTile(0, info.minX, info.minY,
                              (info.maxX - info.minX) / 8,
                              (info.maxY - info.minY) / 8, 256);
// px.length === 262144 and a typed array; count non-white pixels to prove it drew.
```

Copy an `.ocd` into the served `wwwroot` as `probe.ocd` first, and delete it after.
A pass means Skia's native library, HarfBuzz, the staged fonts, the `[JSExport]`
boundary and DotSpatial's embedded EPSG table are all intact under trimming.

### Things that will cost you an hour if you do not know them

**Never rebuild while a dev server is running.** Blazor fingerprints its assets,
so a rebuild leaves the running server's `index.html` pointing at files that no
longer exist. The page goes blank with a 404 on `dotnet.*.js`, which looks like a
code fault and is not. Stop the server, build, start it again. The same applies to
`dotnet publish`, which shares `obj/` with the build.

**To check UI changes quickly**, do not publish normally — AOT is on and a publish
takes about an hour:

```bash
dotnet publish map-viewer/src/OcadViewer.Wasm -c Release -p:RunAOTCompilation=false -o <dir>
node map-viewer/tools/serve.mjs <dir>/wwwroot 5004
```

A plain `dotnet build` does **not** copy static files into `bin`, so only a publish
gives a servable bundle.

**The folder picker cannot be automated.** `showDirectoryPicker` opens a native
dialog, so anything behind "Open project folder" — the version list, comparison
header, diff tabs — needs a human to verify. There is a test project with two
releases and realistic metadata at
`C:\Users\adria\AppData\Local\Temp\ocad-testproject`; recreate it from the recipe
in *Test assets* if the temp directory has been cleared.

**`OcadViewer.RenderCli` does not run on this machine.** A Windows application
control policy blocks the freshly built assemblies (`0x800711C7`). It builds fine
and the same code runs fine inside WebAssembly. This is why golden-image baselines
cannot be regenerated here — see Risk 9.

**Chromium only.** The File System Access API does not exist in Firefox or Safari.
The Angular app has the same limitation, so it is not a regression.

### Benchmark map

`ocad-versioner/src/assets/tests/large_map.ocd` — 9.8 MB, OCAD 2018, deliberately
**untracked**: `angular.json` ships all of `src/assets` to production, so committing
it there would add 9.8 MB to every user's download. Every timing in this document
was measured on it.


## Context

OCADVersioner is a version-control tool for orienteering map files in OCAD format — an Angular 17 SPA with no backend, deployed to Azure Static Web Apps. It reads `.ocd` files straight off disk via the File System Access API and treats a `Releases/V<n>/` folder tree as its version history.

Rendering today goes through the npm package `ocad2geojson-test`: `ocadToSvg()` builds **one giant inline SVG element** for the whole map, which is handed to `L.svgOverlay` in Leaflet ([ocad-map-viewer.component.ts:169](ocad-versioner/src/app/components/ocad-map-viewer/ocad-map-viewer.component.ts:169)). Every symbol becomes a live DOM node, and two of these are on screen at once for the side-by-side comparison. On large maps this is slow — the browser is laying out and hit-testing tens of thousands of SVG nodes. Each `.ocd` is also parsed **twice** per version switch: once in the web worker for GeoJSON diffing, once on the main thread for SVG.

The fix is to stop treating the map as DOM. PurplePen (`C:\repos\PurplePen`) is a mature desktop orienteering application that already has a complete, high-fidelity OCAD parser and renderer in C#, and — critically — it is already factored behind a platform-neutral drawing abstraction. The exploration below found the extraction is far cheaper than expected.

**Outcome intended:** a Blazor WebAssembly app that replaces the Angular one entirely — same workflow, same `Releases/V<n>/` folders, but the map drawn by PurplePen's renderer into Leaflet tiles instead of one giant SVG per map. It is built alongside the Angular app, which keeps serving users until the Blazor app reaches parity and is then deleted.

## Key findings that shape the plan

These came out of exploring both repos and they change the shape of the work substantially:

- **PurplePen's map code is already portable.** `Graphics2D` and `MapModel` both target plain `net10.0`. Neither references `System.Drawing.Common`, WinForms, WPF, or any P/Invoke. They use only `PointF`/`RectangleF`/`SizeF`/`Color` from `System.Drawing.Primitives`, which is in the cross-platform shared framework and works in WASM.
- **There is already a rendering abstraction.** [IGraphicsTarget.cs](C:/repos/PurplePen/src/MapModel/Graphics2D/IGraphicsTarget.cs) (247 lines) is a clean retained-resource drawing interface. Six backends implement it today.
- **One of those backends is already portable**: `Map_SkiaStd` (net10.0, SkiaSharp + HarfBuzz + ImageSharp) — no Windows dependencies at all. SkiaSharp ships an official WASM build. This means *we do not write a renderer*.
- **All the hard OCAD fidelity logic sits above the abstraction.** Dash patterns, hatching, area patterns, glyph placement, area borders, text-on-path and overprint blending are implemented in `SymDef.cs` (4,675 lines) and `SymPath.cs` (2,111 lines), emitting plain paths. Any backend gets them for free.
- **The adapter is tiny.** PurplePen's own Avalonia port bridges model to Skia canvas in **57 lines** (`src/AvPurplePen/CacheableMapDisplay.cs`). That file is essentially the shape of what we write.
- **Licensing improves.** PurplePen's MapModel is **BSD 3-Clause** (Peter Golde). The current `ocad2geojson` is **AGPL-3.0**. This swap is a licensing simplification, not a complication.
- **`DotSpatial.Projections` (LGPL 2.1) is narrowly coupled but not optional.** Only `MapModel/MapProjection.cs` touches it — but `OcadImport.cs:272` calls into it at compile time for pseudo-Mercator maps, so it must be vendored, not dropped.

Rough sizes: `Graphics2D` ~2.4k lines, `MapModel` ~27.7k lines (of which ~18.6k is the minimal parse+render set), `Map_SkiaStd` ~3.5k lines. **We port ~600–1,500 lines of new code; the rest is taken as-is.**

## Confirmed decisions

| Decision | Choice |
|---|---|
| End state | **Blazor WebAssembly app. The Angular app is retired.** |
| Migration | **Side by side.** Angular keeps serving users until Blazor reaches parity |
| Map viewport | **Leaflet**, hosted in Blazor through JS interop |
| Synced side-by-side views | **Hard requirement.** `leaflet.sync`, unchanged |
| Rendering | PurplePen's renderer in WebAssembly, drawn into Leaflet tiles |
| Fidelity | **Full** — all symbol types, patterns, hatching, glyphs, text |
| Hosting | Must remain a static site |

### Why Leaflet, and why that does not mean keeping Angular

Two separate questions got conflated once already, so to be explicit:

**Leaflet owns the viewport** because it is a better viewport than one we would
write. The original plan had us build a `ViewportController` and later a hand-rolled
tile cache; both are dropped.

1. **Leaflet was never the bottleneck.** The problem was `ocadToSvg` building one
   giant DOM tree per map — tens of thousands of live SVG nodes, two on screen at
   once. Swapping the *layer* for rendered tiles fixes that without removing Leaflet.
2. **`leaflet.sync` is layer-agnostic.** It only calls `setView`, `getCenter` and
   `getZoom`. The spike confirmed it works with a custom canvas `GridLayer` with no
   changes at all, which satisfies the synced-view requirement for free.
3. **`L.GridLayer` is the tile pyramid** that would otherwise be hand-built —
   lifecycle, pruning, zoom transitions, keeping coarser tiles while finer ones load.

**Blazor owns the application.** Leaflet is a JavaScript library, and Blazor hosts
JavaScript libraries perfectly well; the viewport choice says nothing about which
framework wraps it. Everything the Angular app does — version list, diff table,
folder picker, settings, translations — moves to Blazor.

The one genuine cost of Leaflet is that we do not own the frame loop, which matters
because WebAssembly is single-threaded. See *The hard part: one thread*.

## The exact call pattern to reuse

PurplePen's own Skia test harness already does precisely what Stage 1 needs — [RenderingUtil.cs:46](C:/repos/PurplePen/src/MapModel/Map_Skia.Tests/RenderingUtil.cs:46). This is the template for our render call:

```csharp
var grTarget = new Skia_BitmapGraphicsTarget(w, h, false, CmykColor.FromCmyk(0,0,0,0), mapArea, true, null, intensity);
grTarget.PushAntiAliasing(antiAlias);

RenderOptions renderOpts = renderOptions;
renderOpts.usePatternBitmaps      = usePatternBitmaps;
renderOpts.renderTemplates        = RenderTemplateOption.MapAndTemplates;
renderOpts.blendOverprintedColors = useOverprinting;
renderOpts.minResolution          = mapArea.Width / (float)bitmapSize.Width;   // drives detail culling

using (map.Read())
    map.Draw(grTarget, mapArea, renderOpts, null);
```

Two things to carry forward: `minResolution` is derived from zoom and is what makes rendering scale-aware, and `map.Draw` must stay **color-layer-major** (it iterates `SymColor`s, not objects) or OCAD layering breaks.

## Fonts in WebAssembly — settled, but not the way it looked

A browser gives Skia no system fonts, so every face a map asks for must be supplied by us, and registration must happen **before parsing**: `OcadImport.cs:2192` calls `TextFaceIsInstalled` during import, and text symbol layout measures glyphs as it reads.

`SkiaFontManager.AddFontFile` ([EnhancedTypeface.cs:788](C:/repos/PurplePen/src/MapModel/Map_SkiaStd/EnhancedTypeface.cs:788)) registers by **file path** — `Path.GetFullPath`, `File.Exists`, then `SKTypeface.FromFile`, with no stream overload. The plan assumed that could not work in a browser and budgeted for a fallback `ITextMetrics` built on `SKTypeface.FromStream`.

**It works unmodified.** The .NET WebAssembly runtime runs on Emscripten's in-memory filesystem and Skia is linked into the same module, so writing font bytes to a virtual path and registering that path is enough. `FontPack.InstallFromHttpAsync` does exactly that. No upstream patch is needed and `vendor/` stays byte-identical.

### The trap that cost the most time

The fonts have to be **physically under `wwwroot`**. Declaring them as a `Content` item with `Link` metadata pointing into `vendor/` copies the file to the output directory but never registers it as a *static web asset* — so the request returns an **empty 200**, and the app writes zero-byte font files.

That failure does not look like a missing font. `mmap` on a zero-length MEMFS file yields null `contents` and aborts the entire runtime with `Cannot read properties of null (reading 'length')` — indistinguishable from a fundamental "Skia cannot mmap on WebAssembly" incompatibility. It is not. It is an empty file.

The `StageVendorFonts` target in `OcadViewer.Wasm.csproj` copies the vendored fonts into `wwwroot/fonts` before the static web asset pipeline runs; that folder is generated and git-ignored.

### Verify the right thing

A check that asserts "text measured to some number" passes happily while rendering the **wrong font**. During Stage 2 every font check passed while text was actually being drawn in Skia's built-in `Noto Mono`; the only reason it surfaced was a check that asserted the *resolved family name*. Assert identity, not mere success.

### What is shipped, and the fidelity gap

Eight faces (Roboto and Roboto Condensed, regular/bold/italic/bold-italic), with `Arial`, `Helvetica` and `Arial Narrow` registered as aliases onto the same files so a requested weight and slant survives the substitution, and `Roboto` as the fallback family.

Real OCAD maps name Arial and Times New Roman. Roboto's glyph advances differ, so text will not match OCAD exactly. If that becomes visible, ship metric-compatible faces (Liberation Sans / Arimo) — roughly a 4-file, 700 KB addition. Fonts are already 1.4 MB of the ~5 MB payload; converting TTF to WOFF2 would recover about half of that.

## Test assets

> Paths of the form `C:/repos/PurplePen/...` below refer to an upstream PurplePen
> working copy. The same files are vendored under `map-viewer/vendor/PurplePen/`.

- PurplePen: [TestFiles/marymoor.ocd](C:/repos/PurplePen/src/MapModel/TestFiles/marymoor.ocd) (599 KB) and `Lake Sammamish.ocd` (391 KB) are the largest real maps available; `TestFiles/ocadimport/` has version-specific files (`nonrenderable6/9/12.ocd`, `missingfont6/9.ocd`) for parser coverage across OCAD 6–12.
- OCADVersioner: `ocad-versioner/src/assets/tests/*.ocd` are small synthetic fixtures (1 knoll, point/line/area) — fine for correctness, useless for performance.
- `ocad-versioner/src/assets/tests/large_map.ocd` (9.8 MB, OCAD 2018) is the
  benchmark. Untracked on purpose — see *Resuming in a new session*.

### Recreating the test project folder

The app opens a folder, not a file, so manual testing needs a project laid out the
way the Angular app expects. This builds one with two releases and realistic
metadata:

```bash
P="$TEMP/ocad-testproject"
mkdir -p "$P/Releases/V1" "$P/Releases/V2"
cp ocad-versioner/src/assets/tests/large_map.ocd "$P/mymap.ocd"
# any two .ocd files will do for the releases:
cp ocad-versioner/src/assets/tests/point_line_area.ocd "$P/Releases/V1/V1.ocd"
cp ocad-versioner/src/assets/tests/1knoll.ocd          "$P/Releases/V2/V2.ocd"

cat > "$P/Releases/V1/V1.json" <<'JSON'
{"numberOfAddedSymbols":12,"numberOfDeletedSymbols":3,"numberOfEditedSymbols":7,"versionCreatedAt":"2026-09-01T09:15:00.000Z","versionNumber":1,"description":"First survey pass","title":"Spring survey"}
JSON
cat > "$P/Releases/V2/V2.json" <<'JSON'
{"numberOfAddedSymbols":5,"numberOfDeletedSymbols":0,"numberOfEditedSymbols":2,"versionCreatedAt":"2026-09-20T14:40:00.000Z","versionNumber":2,"description":"After clearing","title":"Autumn update"}
JSON
```

That JSON is exactly the shape the Angular app writes, which is what makes it a
useful test rather than a convenient one.

---

## Vendoring strategy: verbatim file copy + our own thin csprojs

> **Reversal.** An earlier draft of this plan proposed a git submodule. That is wrong, and the correction is load-bearing:
> - **PurplePen is 5.4 GB on disk with a 3.7 GB `.git`.** As a submodule, every CI clone pays that.
> - **`src/MapModel` is not self-contained** — `MapModel.csproj:67` references `..\..\DotSpatialProjectionsLite\`, outside the directory. So a subtree of it doesn't work either.
> - **MSBuild walks *up* from each csproj**, so submodule projects would bind to upstream's `src/Directory.Packages.props`, giving the build graph two competing central-package-management roots.

The decisive insight: **the source files need zero modification, but the project files need substantial modification.** So vendor sources verbatim and own the build.

`vendor/` holds **only** upstream `.cs`/`.ds`/`.csv`/`.xml`/`.lla`/`.snk`/`.ttf`, byte-for-byte, and **no `.csproj`**. Our own thin csprojs live in a parallel `src/` tree and pull sources in via explicit `<Compile Include="..\..\vendor\..." />`. Syncing is then a scripted, checksummed file copy — `git diff --stat vendor/` shows exactly what upstream changed — rather than a merge.

**Compile every vendored `.cs`; exclude nothing** (including `OcadExport.cs` and the `OpenMapper*` trio). `InputOutput.cs` references them, so excluding requires editing source. Let the IL trimmer do size reduction instead. This keeps the sync story at "copy files, no edits".

### Build-integration fixes required in the thin csprojs

All verified against the upstream files:

1. **Central package management.** Upstream csprojs use versionless `PackageReference`. Our `Directory.Packages.props` must pin: `SkiaSharp` 3.119.4, `SkiaSharp.HarfBuzz` 3.119.4, `SixLabors.ImageSharp` `[2.1.3]` (exact — licence-motivated), `System.Text.Encoding.CodePages` 10.0.8, plus `SkiaSharp.Views.Blazor` 3.119.4. **Also copy the seven `NuGetAuditSuppress` entries** (`Directory.Packages.props:63-69`) or the ImageSharp pin trips NuGet audit and fails publish.
2. **Keep `Prune="false"`** on `System.Text.Encoding.CodePages` (`MapModel.csproj:71`). On .NET 10 the SDK otherwise prunes it as in-framework, but `CodePagesEncodingProvider` is *not* in the browser-wasm framework.
3. **Hoist `AllowUnsafeBlocks` to the top-level PropertyGroup.** Upstream sets it only inside per-configuration groups (`MapModel.csproj:27,33,39`).
4. **Keep `GenerateAssemblyInfo=false` and copy the `Properties/AssemblyInfo.cs` files.** `MapModel/Properties/AssemblyInfo.cs:17` has `[assembly: InternalsVisibleTo("Test_MapModel")]` — so **our test project must be named exactly `Test_MapModel`**. Both AssemblyInfo files reference `VersionNumber` from `Graphics2D/VersionNumber.cs`, so that file cannot be dropped.
5. **Preserve `<DefineConstants>TEST</DefineConstants>`** — upstream defines it in all three configurations.
6. **Drop `CodeAnalysisRuleSet` entirely.** Two of the three paths don't even exist upstream (`AllRules.ruset` is a typo); no analyzers are referenced, so it is silently inert today.
7. **Set `LogicalName` explicitly** on the embedded projection resource. `Projections/AuthorityCodeHandler.cs:75` looks it up by the hardcoded string `"PurplePen.MapModel.Projections.AuthorityCodeToProj4.ds"`; because the source lives outside the project directory, SDK-derived naming would be wrong. Keep `RootNamespace=PurplePen.MapModel` too.
8. **In the DotSpatial csproj, delete the `OutputPath`/`DocumentationFile` overrides** (`..\bin\Debug\` etc. — they write outside the project dir), but **keep `<Compile Remove="DeflateStreamUtility.cs" />`** or it double-defines.
9. **Don't copy `Directory.Build.props`** — it only sets net48-era binding redirects.

Leave the two dead `using System.Drawing.Drawing2D;` lines (`OcadImport.cs:45`, `OcadExport.cs:44`) alone. They resolve today via an in-box facade, but **may not resolve for `browser-wasm`**, whose framework set differs. If publish fails, record the deletion in the patch log rather than treating it as a free edit.

### Sync procedure — `tools/sync-purplepen.ps1`

Takes a PurplePen path and git ref; copies an **explicit hardcoded file manifest** (not a glob — a new upstream file should surface as a conscious decision); records SHA256 of every file plus the upstream commit into `vendor/UPSTREAM.md`; a `-Check` mode runs in CI and fails if `git diff vendor/` is dirty, catching accidental local edits. `UPSTREAM.md` also carries a **patch log** for any unavoidable deviation, so the next sync re-applies it.

Upstream pin for the initial copy: `petergolde/PurplePen` @ `91bdbd59`.

## Projections: vendor DotSpatial, do not drop it

> **Reversal.** An earlier draft said to drop `DotSpatial.Projections`. That was wrong.

`OcadImport.cs:272` calls `ProjectionUtil.CalcGridScaleFactor(...)` for pseudo-Mercator maps (`RealWorldGridAndZone == 64000 || == 65000`) — a **compile-time** reference, not a runtime-optional one. Dropping `MapProjection.cs` breaks the build of `OcadImport.cs`, so it would require modifying upstream source, defeating the "as original as possible" goal. Pseudo-Mercator is common in real maps, so stubbing it to return `1.0` is a genuine fidelity loss.

Cost is ~600 KB Brotli. Vendor it. Its coupling is narrow — only `internal class ProjectionUtil` (`MapProjection.cs:10-137`) touches DotSpatial; the rest of that file is a dependency-free lookup table. Copy only the source, the `.snk`, `Nad/null.lla`, `XML/{gcs.csv,pcs.csv,datums.xml}` and the `.resx` (≈640 KB embedded) — **not** the 34 MB `Nad/` directory, which is not embedded.

**LGPL-2.1 note:** shipping its IL to browsers is distribution. With the source vendored in a public repo and a reproducible build, this is satisfied. Set `IsTrimmable=false` plus a trimmer root so the shipped assembly stays unmodified. If OCADVersioner ever goes private, the source and build instructions must still be offered to recipients — put that in `THIRD-PARTY-NOTICES.md`.

## Solution layout

```
C:\repos\OCADVersioner\
  ocad-versioner\                       # Angular app — frozen, then deleted at Stage 9
  THIRD-PARTY-NOTICES.md                # served by the app, not just committed
  map-viewer\                           # .NET root
    MapViewer.slnx
    Directory.Build.props
    Directory.Packages.props            # CPM: versions + audit suppressions
    PLAN.md
    tools\
      sync-purplepen.ps1                # vendor sync + drift check
      serve.mjs                         # static server for a published module
    vendor\
      UPSTREAM.md                       # commit SHA, SHA256 manifest, patch log
      LICENSE-*.txt
      PurplePen\{Graphics2D,MapModel,Map_SkiaStd,DotSpatial.Projections,fonts}\
    src\
      Graphics2D\  MapModel\  DotSpatial.Projections\  Map_SkiaStd\   # thin csprojs
      OcadViewer.Core\        # loader, renderer, font pack — no UI, no hosting
      OcadViewer.RenderCli\   # desktop .ocd -> PNG, for baselines
      OcadViewer.Wasm\        # THE DELIVERABLE: the Blazor app
    test\                     # not created yet — see Risk 9
```

**`OcadViewer.Wasm` is the product**, and after the parity cleanup it is *only* the
product: the Angular app's feature set minus the diff, with nothing left over from
building it.

**`OcadViewer.TileSpike` is deleted.** Its tile layer, scheduler and coordinate
maths live in `wwwroot/js/leafletViewport.js` now, which is what it existed to produce,
and its `[JSExport]` boundary is `TileInterop.cs`.

**Skip `PurplePenCore` entirely.** Its `MapDisplay` adds course objects and print
areas we don't need, and drags in 22 embedded PNGs, `symbols.xml`, six `.resx`
localisation sets and a DI package. `Map.Draw` already handles OCAD templates itself
via `RenderOptions.renderTemplates`.

## Architecture

```
Blazor WebAssembly (the app)      JavaScript interop            .NET (same runtime)
────────────────────────────      ──────────────────────        ───────────────────
version list, diff table          L.GridLayer.createTile        LoadMap(name, bytes)
folder picker, settings       ──► tile queue + scheduler    ──► RenderTile(x,y,w,h,px)
MapViewer.razor hosts two divs    two L.Map + leaflet.sync      Georeference()
```

Blazor owns the page and the application state. A small JS module creates the two
Leaflet maps, joins them with `leaflet.sync`, and installs a `GridLayer` whose
`createTile` calls back into .NET for pixels. Leaflet never knows it is inside a
Blazor app, and Blazor never has to reimplement a viewport.

**Getting pixels across the boundary.** The spike used `[JSExport]`, which marshals
`byte[]` as a real typed array and measured ~0.7 ms for a 262 KB tile. Blazor
WebAssembly supports the same attribute with `<WasmEnableJSExports>true</...>`, so
the fast path carries over. **Verify this early in Stage 4** — it is the one piece
of the spike that does not transfer unchanged.

Avoid the obvious alternative, `DotNetObjectReference` plus `invokeMethodAsync`: it
serialises `byte[]` as base64 inside JSON, which would add roughly 350 KB of string
and a JSON parse per tile. If `[JSExport]` turns out not to work inside Blazor, use
`IJSUnmarshalledRuntime`-style shared memory or fall back to PNG, and measure the
cost rather than assuming it.

**Coordinates.** Maps are drawn in OCAD paper millimetres, so Leaflet uses
`L.CRS.Simple` and paper coordinates *are* Leaflet coordinates: `lng = paper X`,
`lat = paper Y`. Tile world rects come from `map.unproject`, which keeps the maths to
four lines. Georeferencing is available (`Georeference()` returns the proj4 string)
if a real CRS and an OSM underlay are wanted later.

**Pixels, not PNG.** `RenderTile` returns raw RGBA straight into `ImageData` and
`putImageData`. No PNG encode, no async decode.

### The hard part: one thread

WebAssembly is single-threaded, so a tile render blocks everything, Leaflet's own
animations included. The spike hit all three consequences; any implementation has to
handle them:

1. **`fadeAnimation: false` is mandatory.** Leaflet fades tiles in from a
   `requestAnimationFrame` loop. A 200–1000 ms blocking render starves it, the fade
   never completes, and tiles sit at partial opacity. The symptom is a washed-out map
   whose tile canvases contain perfectly correct pixels — easy to misdiagnose.
2. **Stale tiles must be dropped before rendering.** Leaflet prunes tiles as the view
   moves, but a job already queued would still be rendered at hundreds of
   milliseconds each. Check `layer._tiles[layer._tileCoordsToKey(coords)]` and skip
   if it is gone. Do *not* also test `tile.isConnected` — that discards everything.
3. **A bare rAF pump deadlocks.** When the page is hidden the callback stops firing,
   and a `pumping` latch then never clears, so the queue stalls permanently even
   after the page is shown again. Needs a `setTimeout` fallback and a watchdog.

Also: settle the view *before* attaching layers. `fitBounds` on one map makes
`leaflet.sync` move the other, which moves the first; tiles queued during that churn
are pruned before the renderer reaches them. The spike saw 113 stale against 1 drawn
until the view was allowed to settle first.

### What makes it fast enough

Render cost tracks **world area**, not pixel count. One 256 px tile over progressively
smaller slices of `large_map.ocd`:

| Tile covers | Width | Render |
|---|---|---|
| whole map | 629 mm | 3166 ms |
| 1/2 | 314 mm | 1132 ms |
| 1/4 | 157 mm | 408 ms |
| 1/8 | 79 mm | 177 ms |
| 1/16 | 39 mm | 85 ms |

Stage 3 measured the pixel axis and found it almost flat — 16× the pixels cost 1.2×
the time. This is the axis that moves, and it moves roughly linearly, which is exactly
what a tile layer exploits: each tile draws only the objects inside its own bounding
box. A zoomed-in viewport of six tiles at 1/8 scale is about a second in total, and it
arrives progressively rather than as one long freeze.

Fully zoomed out is the worst case and always will be: every object is visible, so the
work is the whole map however it is sliced. Tiles make it *progressive and
interruptible*, not cheaper.

### But cost also tracks map density, which is why averages mislead

The table above was measured on slices centred on the map, and it flattered the
result. The same 128 mm tile, rendered at five different places on `large_map.ocd`:

| Where | Render |
|---|---|
| centre | 870 ms |
| north-west | 511 ms |
| south-west | 397 ms |
| south-east | 214 ms |
| north-east | 122 ms |

A **7× spread** at identical tile size and resolution. Cost is driven by how many
objects fall inside the tile, so a viewport over dense terrain — a settlement, a
contour-heavy hillside — costs several times one over open forest or water.

Two consequences. Any single "ms per tile" figure is meaningless without saying where
it was measured; quote a range and the location. And the tile queue should expect wide
variance, so a fixed per-frame budget will not hold — one tile can take 4 seconds.

This also resolves what Stage 4 first recorded as an unexplained gap: zooming in
through Leaflet appeared to buy only 2.2× where direct calls suggested ~8×. It was not
a bug. The direct samples happened to sit on sparse ground and the Leaflet viewport sat
on dense ground.

### Settings

Start with `usePatternBitmaps = false` (glyph stamping — avoids `CreatePatternBrush`
and offscreen surfaces) and `blendOverprintedColors = true`. Both should be toggles so
they can be A/B measured.

*Measured later, and reversed: `usePatternBitmaps = true` is now the default. See
the Stage 6 note below.*

`minResolution` is paper-mm per destination pixel and must account for
`devicePixelRatio`, or a 2× display culls features it should not. For a tile that is
simply `worldWidth / tilePixels`.

**Y-axis:** OCAD paper coordinates are Y-up, screen is Y-down. Use `inverted: true`
and mirror `RenderingUtil.GetTransform` rather than deriving your own.

**File loading:** write the picked file into the in-memory filesystem under `/maps/`
and use the vendored `Skia_FileLoader("/maps")` unchanged — its `SearchForFile`
already does the fallback OCAD templates need. Zero new code.

## Staged implementation

### Stage 0 — Vendor and compile — **DONE**

`tools/sync-purplepen.ps1` copies 181 files (4.2 MB) from a PurplePen working copy into `vendor/PurplePen/`, then rewrites `vendor/UPSTREAM.md` with the commit SHA and a SHA-256 manifest. `-Check` re-verifies and exits non-zero on drift; wire it into CI.

```bash
dotnet build map-viewer/MapViewer.slnx -c Release
```

Four vendored projects build clean, zero warnings. Every build-integration fix listed above was needed except one: `System.Drawing.Drawing2D` resolves fine, including for `browser-wasm`, so the two dead `using` lines stay untouched and `vendor/` is byte-identical to upstream.

### Stage 1 — Desktop renderer and baselines — **DONE**

`OcadViewer.RenderCli` parses a `.ocd` and writes a PNG:

```bash
dotnet run --project map-viewer/src/OcadViewer.RenderCli -c Release -- <map.ocd> -o out.png -w 2000
```

Verified across OCAD **6, 9, 11, 12 and 2018**. `marymoor.ocd` renders at full fidelity — hatching, area patterns, dash patterns, point glyphs, curved text-on-path, embedded bitmap logo. Norwegian characters render correctly, which exercises the legacy codepage path.

Desktop timings at 2000 px, full map extent:

| Map | Format | Parse | Render |
|---|---|---|---|
| `large_map.ocd` (9.8 MB) | OCAD 2018 | 2575 ms | 4285 ms |
| `marymoor.ocd` | OCAD 9 | 1017 ms | 579 ms |
| `Lake Sammamish.ocd` | OCAD 6 | 357 ms | 480 ms |

**Still outstanding from this stage:** the golden-image test project (`test/Test_MapModel` — the name is load-bearing for `InternalsVisibleTo`) and committed baseline PNGs. Renders were verified by eye, not by a regression test.

### Stage 2 — Skia in Blazor WebAssembly — **DONE**

```bash
dotnet workload install wasm-tools      # Emscripten 3.1.56, matches SkiaSharp 3.119.4 exactly
dotnet build map-viewer/src/OcadViewer.Wasm -c Release
```

`Pages/Home.razor` is a smoke test running eight checks through the **vendored** `Skia_GraphicsTarget`, not raw SkiaSharp. All pass: font manager initialises with no system fonts, in-memory filesystem is writable, bundled faces download and register, requested families resolve to the faces we shipped, text measures through `ITextMetrics`, Skia renders offscreen, and overprint blending darkens overlaps correctly.

Published payload: **15 MB raw, ~5 MB brotli** first load, 1.4 MB of it fonts.

Build times: first native link ~1m35s, a relink when native inputs change ~2m25s, incremental C# ~11 s.

`?steps=N` on the smoke page runs only the first N probes. Keep it: a native abort kills the runtime before Blazor renders anything, so bisecting by step is the only way to attribute one.

### Stage 3 — Parse and render a real map in the browser — **DONE**

`Pages/Viewer.razor` takes a file, writes it into the in-memory filesystem under
`/maps/` so the vendored `Skia_FileLoader` resolves templates the same way it does
on the desktop, parses it, renders fit-to-canvas, and reports timings. A render-width
selector (1000/2000/4000 px) exists so browser and desktop can be compared at
identical settings, and "Download PNG" exports the render for pixel diffing.

Verified in the browser: `large_map.ocd` (9.3 MB, OCAD 2018), `marymoor.ocd`
(OCAD 8, text-heavy with an embedded bitmap logo and inset map) and
`point_line_area.ocd` all parse and render correctly, matching the desktop output
by eye.

**Georeferencing works under WebAssembly and IL trimming.** `large_map.ocd` reports
`OCAD grid 63005 · +proj=utm +zone=32 +ellps=GRS80 +towgs84=0,0,0,0,0,0,0 +units=m +no_defs`,
which proves `AuthorityCodeHandler` resolves its embedded resource by hardcoded name
through `Assembly.GetCallingAssembly` — the thing Risk 6 worried about — and that
DotSpatial survives trimming with its root in place.

#### Timings — `large_map.ocd`, the number the project turns on

| | Desktop | Browser | Ratio |
|---|---|---|---|
| Parse | 2575 ms | ~6220 ms | **2.4×** |
| Render @ 2000 px | 4285 ms | 7856 ms | **1.8×** |

Slower than desktop, but by a constant factor, not a catastrophic one.

#### Render cost is per-object, not per-pixel

Same map, same browser, only the output resolution changed:

| Render width | Megapixels | Render |
|---|---|---|
| 1000 px | 0.75 | 7553 ms |
| 2000 px | 3.0 | 7856 ms |
| 4000 px | 12.0 | 9082 ms |

**16× the pixels costs 1.2× the time.** Nearly all the cost is managed-side work in
`SymDef`/`SymPath` — bezier flattening, dash splitting, hatch and pattern generation
— not Skia rasterisation. Three consequences, and they revise the plan:

1. **A low-resolution backdrop tile is not meaningfully cheaper.** The two-class
   render in `CachedDrawing.cs` (fast low-res under sharp high-res) assumes
   resolution drives cost. Here it does not. Do not port that design on faith.
2. **Bounding-box culling is the lever that matters.** Full extent is the worst
   case because every object is visible; zooming in should drop the cost roughly
   in proportion to how many objects fall outside the view. Stage 4 must measure
   render time *zoomed in*, not just at full extent.
3. **AOT compilation now looks far more promising than the plan assumed.** It
   targets exactly the managed-side cost that dominates here. Worth measuring
   early in Stage 5 rather than last.

#### Not yet done

- **Pixel diffing against the desktop baseline.** Comparison was by eye. The
  desktop CLI is currently blocked on this machine (see below), so generating a
  fresh baseline is not possible right now.
- **Pseudo-Mercator.** `ProjectionUtil.CalcGridScaleFactor` only runs for
  `RealWorldGridAndZone` 64000/65000. `large_map.ocd` is grid 63005, so that
  specific branch is still unproven; it needs a map in EPSG 3857.
- **An OCAD 6/8 map with non-ASCII text.** Norwegian characters render correctly
  in `large_map.ocd`, but that is OCAD 2018, which uses Unicode strings. The
  legacy `CodePagesEncodingProvider` path for OCAD 6–8 Delphi strings is still
  only exercised on the desktop.

#### Blocker: Windows Application Control

`dotnet run --project src/OcadViewer.RenderCli` now fails with
`0x800711C7 — an application control policy has blocked this file` when loading
the freshly built `Graphics2D.dll`. This is a machine policy (Smart App Control or
WDAC), not a code problem: the same assemblies build fine and run fine inside
WebAssembly. Until it is resolved, the desktop CLI and therefore golden-image
baselines are unavailable. Options are to allow the `map-viewer` output directory
in the policy, or to turn Smart App Control off.

#### Re-creating the local test maps

The viewer takes files through a file input, so driving it from a test needs the
maps served over HTTP. They are deliberately **not** committed — `wwwroot/testmaps/`
is git-ignored, and anything left there would be included in `dotnet publish`:

```bash
mkdir -p map-viewer/src/OcadViewer.Wasm/wwwroot/testmaps
cp ocad-versioner/src/assets/tests/large_map.ocd map-viewer/src/OcadViewer.Wasm/wwwroot/testmaps/
# rebuild, then in the page console:
#   fetch('/testmaps/large_map.ocd') -> File -> DataTransfer -> input.files -> dispatch 'change'
```

Delete the folder when finished.

### Stage 4 — Leaflet tile viewer inside Blazor — **DONE**

Bring the spike's tile layer into `OcadViewer.Wasm` and make it robust.

- **First, verify `[JSExport]` works inside a Blazor WebAssembly project** with
  `<WasmEnableJSExports>true</WasmEnableJSExports>`, and that a `byte[]` still
  crosses as a typed array rather than base64. Everything else depends on it, and
  it is an hour's work to find out.
- Port `OcadViewer.TileSpike/wwwroot/app.js` into a proper ES module under the
  Blazor app, with a real API (`createOcadMaps(leftEl, rightEl, opts)`), invoked
  from a `MapViewer.razor` component.
- Fix the three single-thread traps from *The hard part: one thread*:
  `fadeAnimation: false`, the staleness check, and a pump with a `setTimeout`
  fallback plus a watchdog that clears the latch.
- Give `RenderTile` a cancellation path. `Map.Draw` takes a `throwOnCancel` callback
  invoked after every colour layer (`Map.cs:2184`); wire it to a flag JS can set so
  an abandoned tile stops instead of finishing.
- Vendor Leaflet and `leaflet.sync` into the Blazor app's `wwwroot/lib` as a build
  step, rather than depending on the Angular app's `node_modules`.
- Replace the Blazor viewer page's current single-render canvas with the two synced
  maps.

**Measure, and this is the gating number:** drive a *zoomed-in* viewport end to end
through Leaflet and record time-to-first-tile and time-to-complete. The spike
measured the renderer; this measures the system.

**Done when:** two synced maps in the Blazor app pan and zoom usably on
`large_map.ocd`, tiles arrive progressively, and nothing deadlocks when the tab is
hidden and restored.

### Stage 5 — File access and version management — **DONE**

The first slice of Angular functionality to move.

- File System Access API through JS interop: `showDirectoryPicker`, enumerate
  `Releases/V<n>/`, read `V<n>.ocd` and `V<n>.json`. Mirrors
  `project-directory-selector.component.ts` and `ocad-file-system-service.ts`.
- The `fileHandleTree` and release-list logic from `ocad-versioner.provider.ts`.
- Version list and "create version" (copy the `.ocd`, write the metadata JSON).
- Keep the `Releases/V<n>/` convention byte-identical so the two apps can be used
  against the same folder during the migration.

**Done when:** the Blazor app can open a real project folder, list versions, show
any version in the viewer, and create a new one that the Angular app also reads.

### Stage 6 — Tune the renderer — **AOT DONE, rest outstanding**

Pulled forward from its original position because Stage 4 measured the viewer as too
slow to be usable, and that question had to be settled before building more on top.

#### AOT compilation: enabled, and it is the whole story

`RunAOTCompilation` is on for publish (dev builds are unaffected). Measured on
`large_map.ocd`, 9.3 MB, OCAD 2018:

| | Without AOT | With AOT | |
|---|---|---|---|
| Parse | ~6200 ms cold | **381 ms** | 16× |
| Tile, 128 mm, dense centre | 870 ms | **235 ms** | 3.7× |
| Tile, 128 mm, sparse north-east | 122 ms | **28 ms** | 4.4× |
| Tile, whole map at 629 mm | 3166 ms | **958 ms** | 3.3× |
| Viewport through Leaflet, fitted, both panes | 2569 ms/tile, 20.5 s | **407 ms/tile, 3.3 s** | 6.3× |
| Viewport through Leaflet, zoomed in 2 | 1166 ms/tile, 14.0 s | **228 ms/tile, 2.7 s** | 5.1× |

This is what Stage 3 predicted: cost is dominated by managed work in `SymDef` and
`SymPath` — bezier flattening, dash splitting, hatch generation — and AOT removes the
JIT from exactly that. The original plan ranked AOT last on the assumption that Skia
rasterisation dominated. That assumption was wrong, and the measurement is worth more
than the reasoning was.

It also settles the cold-start question Stage 3 left open. The desktop CLI's 2575 ms
parse was itself a cold, JIT-compiled figure; AOT's 381 ms is what the parser actually
costs. "Parse is 2.4× slower in the browser than on the desktop" was never a browser
problem — it was JIT warm-up on both sides.

**What it costs.** Payload goes from 5 MB to **8 MB brotli** (the native module alone
goes 1.93 → 6.88 MB), and a publish takes roughly an hour — 81 assemblies through
clang, then wasm-opt. `MapModel` alone is several minutes. Dev builds are unaffected,
but CI needs the cache and the `paths:` filter, and a cold CI publish is now an hour,
not ten minutes.

#### Where this leaves interactivity

A zoomed-in viewport across both panes now fills in about 2.7 seconds, tiles arriving
progressively at four or five a second, with the slowest single tile under a second.
That is usable. It is not yet *smooth*, and the worst case — fitted to extent over
dense terrain — still has individual tiles near 900 ms.

#### Still to measure

- **Tile size.** 128 px instead of 256 would quarter the work per tile, giving faster
  first paint and finer-grained cancellation, at the cost of four times as many calls.
  The boundary is ~0.7 ms a call, so that trade should be strongly favourable.
- ~~**`usePatternBitmaps`.** Untested in either direction.~~ **Measured, and it is
  the single biggest render lever.** A temporary `SetRenderOptions` export, with the
  cases interleaved round-robin so JIT drift cancels, on a dense 128 mm tile of
  `large_map.ocd`: baseline 485 ms; pattern bitmaps **291 ms (0.60×)**;
  anti-aliasing off 428 ms (0.88×); templates off 434 ms (0.89×); overprint blending
  off 459 ms (0.95×); all four off 283 ms (0.58×). So pattern bitmaps account for
  essentially the whole available saving and the other three are noise beside it. At
  fitted extent the win falls to 0.81×, because `minResolution` is then ~2 mm/px and
  most patterns are culled before they are drawn.

  Why: with stamping, `AreaSymDef.DrawPatternRows` loops over the lattice of the
  *whole area object's* bounding box and calls `Glyph.Draw` per cell — managed work
  that repeats on every tile the object touches, with Skia clipping away most of it.
  With brushes, `CreatePatternBrushes` rasterises one small tile of the pattern per
  (pattern, colour) pair and `SKShader` repeats it in native code. The brush cache
  lives on the graphics target, which is new per tile, so the rasterisation cost is
  paid once per tile rather than once per map — still far cheaper than the lattice.

  Not pixel-identical: 9.6% of pixels differ, 3% substantially, all of it stipple
  phase in vegetation and rough-open. `UsePatternBitmaps` stays a setting, and
  `RenderCli --no-pattern-bitmaps` renders the stamped reference to diff against.
- **Trimmer roots.** Can `MapModel` and `DotSpatial.Projections` come off the root
  list now, and what does that recover?
- **Fonts.** TTF→WOFF2 would recover roughly 700 KB of the payload, which matters
  more now that AOT has added 3 MB.
- **DotSpatial.** Whether it still earns its ~600 KB.

### Stage 7 — Reproduce the Angular workspace UI — **DONE**

The diff in C# is **deferred indefinitely** — see *The diff* below. This stage
instead brings the Blazor app up to the Angular app's shape, so it is a stable
thing to look at and extend rather than a bare viewer.

The layout to match, from `ocad-versioner.component.html`:

```
 progress bar (2 px, only while loading)
 OCADVersioner                                   [about] [settings]
 project directory selector
 ┌──────────────┬──────────────────────────────────────────────┐
 │ version list │          V3  ↔  V2   (history icon)          │
 │              │          two synced maps                     │
 │              │  [ Added 12 | Updated 7 | Deleted 3 ]        │
 │ create       │          diff table                          │
 │ version      │                                              │
 └──────────────┴──────────────────────────────────────────────┘
```

Components, all new except where noted:

- **Layout shell** — two columns, `col-md-2` and `col-md-10`, and a 2 px progress
  bar that only appears while something is loading.
- **Version list** — a richer version of what Stage 5 built: title, short date,
  description, and the three symbol counts with plus / pencil / trash icons.
  Selecting a version drives the comparison.
- **Create version** — exists; restyle to match, with the symbol counts coming
  from the current diff rather than the zeros Stage 5 writes.
- **Comparison header** — "V3 ↔ V2", with a book icon when looking at history
  rather than the working file.
- **Diff tabs** — Added / Updated / Deleted, each with a count badge that is
  hidden when the count is zero.
- **Diff table** — columns differ by tab. Added and Deleted show length in metres
  and area in square metres; Updated shows movement in metres, length difference
  and area difference as percentages. Both show created and last-edited dates.

**The diff table is built but not fed.** All of it sits behind an `IDiffSource`
that currently returns nothing and renders an explicit "diff not available yet"
state. Nothing fakes data, because fake rows in a table like this are indis-
tinguishable from real ones at a glance and tend to outlive their welcome.

**Out of scope here**, and still in Stage 8: the settings modal, the About page
and the en/no translations. Strings are hard-coded English for now, but kept in
one place so swapping in a translation layer later is mechanical.

**Done when:** the Blazor app presents the same workspace as the Angular app —
open a folder, pick versions, see them side by side, switch diff tabs — with the
table honestly empty.

### The diff — deferred

Originally planned as the next stage and a 5–8 day port of
`json-diff-service.ts`. Dropped, because that logic is going to be expanded, and
porting something before rewriting it means doing the work twice.

Two things worth recording for whenever it is picked up again.

**Object identity does not survive the parser.** The diff matches features on
`id` + `creationDate`. OCAD stores both per object and MapModel reads them —
`OcadFileFormat.cs:1717-1721` has `ServerObjectId`, `CreationDate` and
`ModificationDate` — but `OcadImport` never puts them on the `Symbol` objects, so
they are discarded. Purple Pen has no use for them when setting courses. Any
C# diff needs roughly ten lines carrying those three fields through, recorded in
`vendor/UPSTREAM.md`'s patch log and ideally pushed upstream.

**There is a cheaper route than porting.** `json-diff-service.ts` is a static
class with no real Angular dependency. It could be extracted to a plain JS module
and called from Blazor alongside `ocad2geojson`, giving a working diff with no
port at all. The costs are keeping the AGPL-3.0 `ocad2geojson` dependency and
parsing every file twice, once in JS and once in C#. That is the option to weigh
first if a working diff is wanted before the expansion is designed.

### The change overlay — **DONE**, and it needs no diff

What the diff was mostly wanted for — *where did this map change since last
season* — turns out not to need a diff at all. Both versions are already drawn by
the same renderer; rendering them over the same rectangle at the same size and
comparing the pixels answers the question directly.

`MapStore.RenderChangeMask` returns one byte per pixel: 0 unchanged, 1 appeared,
2 vanished, 3 altered. Both versions are drawn onto opaque white, so "no ink" is
exactly `#FFFFFF` and the direction is readable wherever one side is blank;
where both sides have ink it can see the change but not which way, which is
`Altered`.

- **No object identity, no georeferencing, no `ocad2geojson`.** It works on maps
  the diff could never have handled, and it reports what a person would actually
  see as different.
- **Exact comparison, no tolerance.** Identical geometry renders to identical
  pixels — area-pattern phase is anchored to the map, not the tile — so there is
  no noise to filter. The corollary is that redefining a symbol lights up every
  object using it. Accepted deliberately; `Altered` is where that would show.
- **One pass per comparison**, at 4 px per paper millimetre, capped at 4096 on
  the long edge. Resolution is nearly free (the same area at 128/256/512 px took
  389/389/435 ms — the cost is geometry, not rasterising), so the mask is sharp
  enough to zoom into. Storigarden, 347 × 302 mm, comes out 1390 × 1210 and takes
  **about 1.2 s on the dev build**, measured over three version pairs.
- **The filters are free.** Added/edited and deletions are two independent
  toggles that repaint the same bytes in JavaScript; only changing the
  *comparison* costs another pass. Verified: four filter changes, one render.
- **One colour, by choice.** The buttons say which kinds are showing, so the
  picture does not have to. Note the tint currently collides with the purple
  OCAD uses for overprint and boundaries — see the screenshots in the commit
  that added this.

What it cannot do is name an object, explain a change, or link to a table row. A
moved object appears as vanished at the old position and appeared at the new one
with nothing tying them together. That is the honest boundary of the technique,
and the reason the diff above is deferred rather than cancelled.

### Stage 8 — Remaining parity — **DONE**

- **Settings** — language and EPSG, persisted to `localStorage` under the same
  `appSettings` key and the same camelCase shape the Angular app writes, so both
  apps read one another's settings during the migration. `Georeferencing` is
  round-tripped untouched rather than parsed: it only matters to the diff, which
  does not exist here yet, and rewriting a shape we do not use would be a way to
  corrupt it. The Angular app's duplicated `epsg-index` bundle is not repeated.
- **Localisation**, en and no, read at startup from `i18n/{lang}.json` — the
  Angular app's own translation files, staged into `wwwroot` by the
  `StageTranslations` target rather than copied, so there is one set of strings
  for both apps. A missing key renders as the key itself, which makes gaps
  visible instead of blank.
- **About page and the third-party notices link.**
- **Application Insights telemetry and the global error handler** — see below.

#### Telemetry

`TelemetryService` wraps the vendored SDK through `wwwroot/js/appInsights.js`, and
`TelemetryLoggerProvider` forwards `Error` and `Critical` logs to it. There is no
`ErrorHandler` to override as in Angular: Blazor reports an unhandled component
exception through `ILogger`, so hooking the logging pipeline catches the same class
of failure.

**Off unless a key is configured.** The key lives in `wwwroot/appsettings.json`
under `Telemetry:ConnectionString`, empty in the repo. With no key the SDK script is
never even fetched, so local builds make no third-party requests — verified in the
browser, no request to `lib/appinsights/` and none to `js/appInsights.js`.

Three things worth knowing:

- **The provider is registered before the container exists**, so it takes an
  `IServiceProvider` assigned after `builder.Build()`. Calling
  `BuildServiceProvider()` inside the provider instead compiles and runs and
  reports nothing: the logger gets a second `TelemetryService` that was never
  initialised. The registration order in `Program.cs` is load-bearing.
- **A .NET stack arrives with `parsedStack` empty.** The SDK's stack parser only
  recognises JavaScript frames, so the portal shows the raw stack string rather
  than a structured frame list. The whole trace is there and legible.
- **Verify it against a local stub, not the real endpoint.** Point
  `IngestionEndpoint` at a localhost server that logs request bodies and set a
  dummy instrumentation key; nothing leaves the machine and the payload is
  readable. A captured unhandled component exception arrives as `ExceptionData`,
  severity 4, carrying the .NET type name, the message, the raw stack and the
  logger category and event id as properties.

**Stage 9 must add the key to the Blazor workflow**, the way the Angular workflow
injects `APPINSIGHTSINSTRUMENTATIONKEY_PROD` at build time — otherwise the deployed
app is silent and nothing says so.

**Still outstanding:** the Norwegian translations for the strings the Angular app
never had — the About page's rendering and telemetry paragraphs, and "Refresh".
They stay English until someone who speaks Norwegian writes them; inventing them
would put bad Norwegian into a file the Angular app also reads.

### Parity cleanup — **DONE**

`OcadViewer.Wasm` is meant to be the Angular app's stand-in: its features, minus
the diff, and nothing else. Everything built along the way to prove something, and
then kept out of habit, is gone.

Removed, with what each was for:

| Removed | Was for | Why it went |
|---|---|---|
| `OcadViewer.TileSpike` | proving `[JSExport]` marshals `byte[]` as a typed array | proven; the code it produced is in `leafletViewport.js` |
| `Pages/Smoke.razor` | nine Skia/font/blending checks | not an Angular feature, and the only thing needing `SkiaSharp.Views.Blazor` |
| `SkiaSharp.Views.Blazor` | the smoke page's `SKCanvasView` | tiles render offscreen and go through `putImageData`; no Blazor canvas is involved |
| `FontPack.Diagnose` | the smoke page's font checks | its only caller |
| `probeBoundary` | Stage 4's marshalling measurement | measured: ~0.7 ms for 262 KB |
| `mapSize` / `MapSizeAsync` | reading a version's file size | never called |
| tile stats wrappers, `SetRenderOptions` | Stage 6 A/B measurement | no caller; see below |
| the cancellation path | abandoning a render when the view moves | cannot work — see below |

**`SkiaSharp.NativeAssets.WebAssembly` now has to be referenced explicitly.** It
used to arrive through `SkiaSharp.Views.Blazor`. Without it the app builds and then
dies at runtime with a `TypeInitializationException` inside `SkiaFontManager`,
which reads like a font problem and is not.

**The cancellation path was dead by construction, not by accident.** `RenderTile`
set a flag that `Map.Draw` checked after every colour layer, and JS could clear it
by calling `CancelRender`. But WebAssembly is single-threaded: while a render
blocks, no JavaScript runs, so nothing can ever set the flag mid-render. The only
moment JS could set it is before the render starts — which is exactly what the tile
pump's staleness check already does, and that one works. If renders ever move to a
worker, this becomes worth having again; until then it was ceremony.

**Stage 6's remaining A/B levers went with it.** `SetRenderOptions` (for
`usePatternBitmaps`) and the tile stats counters had no callers. Re-adding them is
about twenty lines: a `[JSExport]` that replaces the static `MapRenderSettings`,
and a method on the tile-layer handle returning the `stats` object that is still
maintained inside `leafletViewport.js` and still reachable at `globalThis.__ocad.stats`.

### Stage 9 — Deploy side by side, then retire Angular (2–3 days) — **NEXT**

Both apps live at once; Angular stays the default until Blazor is trusted. See
*Deployment*.

Retirement is a deliberate, reversible sequence:

1. Deploy Blazor to its own Static Web App and use it alongside Angular.
2. Run both against the same project folders for a period — the `Releases/V<n>/`
   format is shared, so this is low-risk.
3. Switch the production domain to the Blazor app, keeping Angular reachable.
4. Delete `ocad-versioner/` and its workflows, drop `ocad2geojson-test`, and fold
   the third-party notices into the Blazor app.

**Done when:** `ocad-versioner/` is gone and nothing references it.

## Architecture, and the tests that hold it in place

An adversarial review of this branch found the dependency direction sound and the
orthogonality poor, concentrated almost entirely in one place. What follows is
what the shape is now, and what enforces it.

**Projects.** One chain pointing inward, plus a domain project that depends on
nothing:

```
Graphics2D ← MapModel ← Map_SkiaStd ← OcadViewer.Core ← RenderCli
                                           ↑
                   OcadViewer.Versioning ← OcadViewer.Wasm ← ArchitectureTests
```

`OcadViewer.Versioning` is new. It holds what a release is called, what its
metadata looks like on disk and which version compares against which — rules that
are a contract with the Angular app, and that previously lived in a Razor page, a
service that needed a browser, and a JavaScript module, in two languages. Having
them in one dependency-free project is what makes them testable at all.

**Modules.** The JavaScript has the same shape, which the compiler cannot check:

```
tilePump.js ← leafletViewport.js ←┐
                                  ├─ mapWorkspace.js ← OcadMapPair.razor
dotnetExports.js ← mapTiles.js ←──┤
projectFolder.js ←────────────────┘
```

`mapWorkspace.js` is the composition root and the only module the C# talks to.
`leafletViewport.js` no longer imports the .NET runtime, so the viewport can be
loaded and reasoned about without Blazor underneath it.

**Maps are addressed by name.** The slot index — 0 and 1, meaning the same thing
in the page, the component, the Leaflet module and the interop — is gone.
`MapStore` keeps parsed maps under their version name, so showing V3 beside V2 and
then V2 beside V1 parses V1 alone. Measured in the browser over open-plus-four-
switches: **6 parses where the old design did 10**, and selecting the oldest
release parses nothing at all. Capacity is three, not two: both panes plus the one
being swapped in, because at two the incoming map evicted one the other pane was
still drawing and 8 tiles came back empty.

**Map bytes no longer cross the boundary three times.** They were read in
JavaScript, marshalled into .NET, handed back out to JavaScript and marshalled in
again — for data the page never looked at. The workspace reads and parses them
without C# ever seeing them.

**What the tests enforce** (`tests/OcadViewer.ArchitectureTests`, 58 of them):

- the allowed project-reference table, that the graph is acyclic, that the domain
  project depends on nothing, that only the browser host references browser
  packages, and that no vendored wrapper depends on our code;
- the same, read from the compiled assemblies' metadata rather than the project
  files, so a dependency arriving transitively is caught too;
- the allowed JavaScript import graph, that only one module knows there is a .NET
  runtime, that only one touches the File System Access API, and that no module
  keeps coordination state at module scope;
- that each decision stated once stays stated once: the release-naming rule, the
  JSON shape shared with Angular, the JS teardown idiom, the language
  subscription, the pane names;
- the project-folder rules themselves, including that timestamps are written the
  way the browser writes them.

The assemblies are read with `System.Reflection.Metadata` and never loaded, which
also keeps the suite runnable where policy refuses unsigned binaries.

## WASM specifics

**Emscripten version match — verified, and it is good news.** SkiaSharp 3.119.4's targets select `3.1.56/{st|mt}[,simd]` archives for net9.0+, and .NET 10.0.102's `wasm-tools` manifest ships Emscripten **3.1.56**. Exact match; ABI mismatch is off the table. The targets also force `WasmBuildNative=true` (so native relinking, and the workload, are mandatory) and carry a link-order workaround for `dotnet/runtime#109289` — evidence the path is actively maintained.

**Payload estimate:** ~19–24 MB uncompressed, **~6–9 MB Brotli** first load; warm load ≈ 0. Mitigations in order of value: `InvariantGlobalization=true` (−1.4 MB ICU, safe — the vendored code uses only `InvariantCulture`, and `CodePagesEncodingProvider` is ICU-independent); trimming **with explicit `TrimmerRootAssembly` for `MapModel` and `DotSpatial.Projections`** (neither was ever trim-annotated); **AOT off initially** (roughly doubles download, adds 10–30 min publish); no `.pdb`. Skip lazy assembly loading — both candidate assemblies are needed at parse time, so it is not actually lazy.

**Fonts.** `SkiaFontManager.AddFontFile` is path-based (`EnhancedTypeface.cs:788`), `SKFontManager.Default` is empty on WASM, and fonts must be registered **before parsing** — `OcadImport.cs:2192` calls `TextFaceIsInstalled` during import. Ship the 8 Roboto/RobotoCondensed faces PurplePen already uses, install them into MEMFS at startup, and `SetDefaultFontFamilyName("Roboto")`. Register `Arial`→Roboto and `Helvetica`→Roboto aliases explicitly so weight/slant variants resolve; ship metric-compatible Liberation Sans (~700 KB) if Arial fidelity matters.

## Deployment

The two apps are deployed independently and run side by side until Angular is
retired. That keeps the migration reversible at every step and means a Blazor
problem never takes the live app down.

**A third Static Web App for Blazor.** The existing two SWAs (`icy-pond` for `main`,
`thankful-coast` for `develop`) keep building Angular with Oryx exactly as they do
now, untouched. Blazor gets its own, built in GitHub Actions and deployed prebuilt,
because **Oryx cannot build .NET 10 Blazor WebAssembly** and has no `wasm-tools`.

```yaml
# .github/workflows/azure-static-web-apps-mapviewer.yml
on:
  push:
    branches: [main, develop]
    paths: ['map-viewer/**', '.github/workflows/azure-static-web-apps-mapviewer.yml']

jobs:
  build_and_deploy:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.0.x' }
      - uses: actions/cache@v4          # wasm-tools is ~1.5 GB
        with:
          path: |
            ~/.dotnet
            ~/.nuget/packages
          key: wasm-${{ hashFiles('map-viewer/Directory.Packages.props') }}
      - run: dotnet workload install wasm-tools
      - run: dotnet publish map-viewer/src/OcadViewer.Wasm -c Release -o publish
      - name: Inject the telemetry key          # without this the app is silent
        run: |
          jq --arg k "${{ secrets.APPINSIGHTSINSTRUMENTATIONKEY_PROD }}" \
             '.Telemetry.ConnectionString = $k' \
             publish/wwwroot/appsettings.json > tmp && mv tmp publish/wwwroot/appsettings.json
      - uses: Azure/static-web-apps-deploy@v1
        with:
          azure_static_web_apps_api_token: ${{ secrets.AZURE_STATIC_WEB_APPS_API_TOKEN_MAPVIEWER }}
          repo_token: ${{ secrets.GITHUB_TOKEN }}
          action: upload
          app_location: 'publish/wwwroot'
          api_location: ''
          output_location: ''
          skip_app_build: true          # load-bearing: keep Oryx away
```

`skip_app_build: true` with `app_location` pointing at an already-published
`wwwroot` is the load-bearing part. The `paths:` filter keeps Angular-only commits
from paying for a 3–10 minute native link.

**Three things the committed workflow does that the sketch above does not**, all
found while writing it:

- **`DOTNET_INSTALL_DIR` is pinned into the workspace.** `setup-dotnet` installs
  into `/usr/share/dotnet`, and `dotnet workload install` puts wasm-tools inside
  that same root — so caching `~/.dotnet`, as the sketch did, would have cached
  nothing at all and quietly reinstalled 1.5 GB on every run.
- **`deployment_environment` splits develop off.** One Static Web App serving two
  branches means a develop push lands on production. Non-main pushes now go to a
  named environment. Note the expression is written
  `develop && 'develop' || ''` rather than the other way round: `''` is falsy in
  an Actions expression, so the natural-looking `main && '' || 'develop'` yields
  `'develop'` on main.
- **`skip_deploy_on_missing_secrets: true`**, as the Angular workflows have, so
  the file can land before the Azure resource exists instead of failing every
  push until it does.

**Still needs a person**, and nothing deploys until both exist: the third Static
Web App in Azure, and two repository secrets —
`AZURE_STATIC_WEB_APPS_API_TOKEN_MAPVIEWER` (rename to match the Azure-generated
name, the way the other two are named) and the existing
`APPINSIGHTSINSTRUMENTATIONKEY_PROD`.

The app's own `wwwroot/staticwebapp.config.json` already carries what is needed:

- **`navigationFallback.exclude` containing `/_framework/*`** — without it a missing
  framework asset returns `index.html` with a 200, and Blazor fails with a cryptic
  integrity error instead of a clean 404.
- `mimeTypes` for `.wasm` → `application/wasm` (since .NET 8, `WasmEnableWebcil`
  means managed assemblies also ship as `.wasm`), plus `.dat`, `.blat`, `.pdb`.
- `index.html` `no-cache`; `/_framework/*` immutable long-cache.

**Verify compression empirically.** Azure Static Web Apps does its own dynamic
compression and does *not* do `.br`-companion negotiation. Check the
`Content-Encoding` header on `dotnet.native.wasm` in DevTools once deployed. If it is
absent, the native module ships uncompressed; mitigate with a `loadBootResource` hook
that fetches the `.br` companion and decodes it client-side.

At Stage 9 step 3, the production domain moves to the Blazor SWA. The Angular
workflows and SWAs are deleted last, once nothing points at them.

## Risks, ranked

| # | Risk | Status | Next check |
|---|---|---|---|
| 1 | **Interactive performance through Leaflet.** Per-tile cost is known and culling works, but the system has not been driven end to end while panning and zooming. | Open — the gating unknown. | Stage 4: time-to-first-tile and time-to-complete on a zoomed-in viewport. |
| 2 | **`[JSExport]` inside Blazor.** The 0.7 ms boundary was measured in a bare WebAssembly module. If Blazor forces `invokeMethodAsync`, every tile pays base64 plus a JSON parse. | Open, and everything leans on it. | Stage 4, first task. Fallbacks: unmarshalled interop, or PNG with measured cost. |
| 3 | **Single-thread stalls.** Leaflet's animations, the tile pump and the renderer contend for one thread; a bare rAF pump already deadlocked once. | Understood; three mitigations are in *The hard part*. | Stage 4: hide and restore the tab, pan hard, confirm no stall. |
| 4 | **Porting the diff is the biggest unknown piece of work.** `json-diff-service` plus its fixtures is the real specification of what this app does. | Open. | Stage 7: port the fixtures first, then make them pass. |
| 5 | **Worst case is still the whole map.** Fully zoomed out, every object is drawn however it is tiled — ~3.2 s for `large_map.ocd`. | Inherent. Tiles make it progressive, not cheaper. | Stage 4: is progressive fill acceptable to look at? |
| 6 | **Payload** ~5 MB brotli, 1.4 MB of it fonts, and it grows as the app gains features. | Measured. | Stage 6: TTF→WOFF2 recovers ~700 KB; confirm SWA compresses. |
| 7 | **Trimming breaks MapModel/DotSpatial** — neither was ever trim-annotated. | Rooted via `TrimmerRootAssembly`. Georeferencing resolving in the browser is evidence the embedded EPSG table survives. | Stage 6: remove roots, re-run the corpus. |
| 8 | **Text fidelity vs. OCAD.** Maps name Arial and Times; we substitute Roboto, so glyph advances differ. | Open. | Stage 4 visual diff. Fix is metric-compatible faces (Liberation Sans / Arimo), ~700 KB. |
| 9 | **No automated fidelity test.** Renders have only ever been checked by eye. | Open and **blocked**: the desktop CLI fails under a Windows application control policy (`0x800711C7`), so baselines cannot be regenerated on this machine. | Needs the policy relaxed, or baselines generated elsewhere. |
| 10 | **Two apps to keep working during the migration.** Both read the same `Releases/V<n>/` folders. | Accepted, and deliberate. | Keep the on-disk format byte-identical; that is what makes it safe. |
| 11 | **CI cost** — 1.5 GB workload plus a native link. | Open. | Stage 9: cache `~/.dotnet` and `~/.nuget/packages`; measure cold and warm. |
| 12 | **LGPL-2.1 obligation** from shipping DotSpatial IL. | A decision, not a bug. | Record in `THIRD-PARTY-NOTICES.md`. |

### Resolved

- `SKTypeface.FromFile` on WebAssembly — works unmodified; the real trap was fonts
  served as empty 200s (see *Fonts*).
- Emscripten/SkiaSharp ABI match — .NET 10.0.102 ships Emscripten 3.1.56, exactly
  what SkiaSharp 3.119.4 wants.
- `System.Drawing.Drawing2D` for `browser-wasm` — compiles and publishes untouched.
- `Assembly.GetCallingAssembly` for the embedded EPSG table — resolves under trimming.
- JS↔WASM marshalling cost in a bare module — ~0.7 ms for a 262 KB tile.
- Whether `leaflet.sync` works with a custom layer — yes, with no changes.
- Whether Skia, fonts, text metrics and overprint blending work in Blazor — yes,
  all eight smoke checks pass.

## Verification

- **Parser:** ported `OcadImportTests` against `TestFiles/ocadimport/` confirms OCAD
  6–12 and 2018 parse. Still outstanding — see Risk 9.
- **Fidelity:** Stage 1 golden PNGs against the same maps rendered in the browser.
  This is what catches WebAssembly-specific font and rasterisation drift, and it is
  currently blocked by the application control policy on the desktop CLI.
- **Tile seams:** adjacent tiles must line up exactly. A half-pixel error in the
  world rect shows as hairlines along every tile edge, and is easiest to spot on
  large area fills and long contours.
- **Against the Angular app:** the same `.ocd` open in both apps side by side. This
  is what the side-by-side migration is for. Symbol shapes, colours and layering
  must match, the diff results must agree, and the synced views must still track.
- **Performance, the actual goal:** on `large_map.ocd`, measure time-to-first-tile,
  time-to-complete and interaction smoothness, zoomed out *and* zoomed in, against
  the current SVG overlay. These numbers decide whether the migration continues.

## Out of scope

Not changed by this work, and not planned:

- The `Releases/V<n>/` on-disk format. It is deliberately kept byte-identical so
  both apps can run against the same folders during the migration.
- Any server component. The app stays a static site with no backend.
- PurplePen's course-setting features. Only the parser and renderer are vendored;
  `PurplePenCore` is skipped entirely.
- Editing maps. This is a viewer and a differ.

The Angular app is **frozen, not forked**: no new features there from Stage 5 on,
because every one of them would have to be built twice. Bug fixes only, until
Stage 9 deletes it.

## Attribution (required)

PurplePen has no root LICENSE file — the BSD-3 text lives in every source header. Serving a WebAssembly module to a browser **is** a binary redistribution, so clause 2 applies. Create `vendor/LICENSE-PurplePen.txt` (verbatim from `InputOutput.cs:1-33`, © 2006–2008 Peter Golde), a root `THIRD-PARTY-NOTICES.md` covering PurplePen (BSD-3), DotSpatial.Projections (LGPL-2.1), SkiaSharp/HarfBuzzSharp (MIT), native Skia (BSD-3) and HarfBuzz, ImageSharp 2.1.3 (Apache-2.0) and Roboto (Apache-2.0) — and **serve it** from the app footer, not just commit it. Note clause 3: do not name the viewer anything using "Purple Pen" or "Peter Golde". The notice is served from the Blazor app; while both apps are live, both serve it.
