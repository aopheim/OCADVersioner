# Replace ocad2geojson with PurplePen's C# OCAD parser and renderer, behind a Leaflet tile layer

> **Status: Stages 0–3 complete and the architecture is settled. Stage 4 is next.**
> The extracted parser and renderer run in WebAssembly and draw real maps correctly.
> The viewer will be a **Leaflet tile layer inside the existing Angular app**, fed by
> a plain .NET WebAssembly module — not a Blazor app. See *Architecture* below.
>
> **To resume:**
> ```bash
> dotnet build map-viewer/MapViewer.slnx -c Release                 # everything
> dotnet run --project map-viewer/src/OcadViewer.RenderCli -c Release -- <map.ocd> -o out.png
> dotnet publish map-viewer/src/OcadViewer.TileSpike -c Release -o <dir>   # the tile module
> node map-viewer/tools/serve.mjs <dir>/wwwroot 5002                # serve it
> pwsh map-viewer/tools/sync-purplepen.ps1 -PurplePenPath <path> -Check    # vendor drift
> ```
> Requires the .NET 10 SDK and the `wasm-tools` workload. A benchmark map is at
> `ocad-versioner/src/assets/tests/large_map.ocd` (untracked — 9.8 MB, and
> `angular.json` ships all of `src/assets` to production, so do not commit it there).


## Context

OCADVersioner is a version-control tool for orienteering map files in OCAD format — an Angular 17 SPA with no backend, deployed to Azure Static Web Apps. It reads `.ocd` files straight off disk via the File System Access API and treats a `Releases/V<n>/` folder tree as its version history.

Rendering today goes through the npm package `ocad2geojson-test`: `ocadToSvg()` builds **one giant inline SVG element** for the whole map, which is handed to `L.svgOverlay` in Leaflet ([ocad-map-viewer.component.ts:169](ocad-versioner/src/app/components/ocad-map-viewer/ocad-map-viewer.component.ts:169)). Every symbol becomes a live DOM node, and two of these are on screen at once for the side-by-side comparison. On large maps this is slow — the browser is laying out and hit-testing tens of thousands of SVG nodes. Each `.ocd` is also parsed **twice** per version switch: once in the web worker for GeoJSON diffing, once on the main thread for SVG.

The fix is to stop treating the map as DOM. PurplePen (`C:\repos\PurplePen`) is a mature desktop orienteering application that already has a complete, high-fidelity OCAD parser and renderer in C#, and — critically — it is already factored behind a platform-neutral drawing abstraction. The exploration below found the extraction is far cheaper than expected.

**Outcome intended:** the Angular app keeps its viewport, its synced side-by-side maps and its whole diff workflow, but stops building a giant SVG per map. Instead a Leaflet tile layer draws the map through PurplePen's renderer running in WebAssembly, at full symbol fidelity and with the work split into tiles that arrive progressively. Nothing else about the app changes.

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
| Runtime | **Plain .NET WebAssembly module** exporting `[JSExport]` methods — no Blazor |
| Viewer | **Leaflet tile layer** inside the existing Angular app |
| Synced side-by-side views | **Hard requirement.** `leaflet.sync`, unchanged |
| Fidelity | **Full** — all symbol types, patterns, hatching, glyphs, text |
| Scope | **Rendering only.** The version list, diff table and folder picker stay as they are |
| Hosting | Must remain a static site |

### Why Leaflet rather than our own viewport

The original plan had us build a `ViewportController` and, later, a hand-rolled tile
cache. That is now dropped. Three things decided it:

1. **Leaflet was never the bottleneck.** The problem was `ocadToSvg` building one
   giant DOM tree per map — tens of thousands of live SVG nodes, two on screen at
   once. Swapping the *layer* for rendered tiles fixes that without removing Leaflet.
2. **`leaflet.sync` is layer-agnostic.** It only calls `setView`, `getCenter` and
   `getZoom`; it has no idea what is underneath. The spike confirmed it works with a
   custom canvas `GridLayer` with no changes at all.
3. **`L.GridLayer` is the tile pyramid** that Stage 5 was going to hand-build —
   lifecycle, pruning, zoom transitions, keeping coarser tiles on screen while finer
   ones load. All free, and all battle-tested.

Keeping Leaflet also preserves the Angular app's version list, diff table and folder
picker untouched, and leaves the door open to an OSM or aerial underlay now that
georeferencing resolves.

The cost is that we do not control the frame loop, which matters because WebAssembly
is single-threaded — see *Architecture*.

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
- **You will need to supply a real, large `.ocd`** (a full forest map, ideally the one that is slow today) to benchmark against. This is the single most important input for judging whether the rewrite achieved its goal.

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
  ocad-versioner\                       # Angular app — gains a tile layer, loses ocadToSvg
  THIRD-PARTY-NOTICES.md                # served by the app, not just committed
  map-viewer\                           # .NET root
    MapViewer.slnx
    Directory.Build.props
    Directory.Packages.props            # CPM: versions + audit suppressions
    PLAN.md
    tools\
      sync-purplepen.ps1                # vendor sync + drift check
      serve.mjs                         # static server for the published module
    vendor\
      UPSTREAM.md                       # commit SHA, SHA256 manifest, patch log
      LICENSE-*.txt
      PurplePen\{Graphics2D,MapModel,Map_SkiaStd,DotSpatial.Projections,fonts}\
    src\
      Graphics2D\  MapModel\  DotSpatial.Projections\  Map_SkiaStd\   # thin csprojs
      OcadViewer.Core\        # loader, renderer, font pack — no UI, no hosting
      OcadViewer.RenderCli\   # desktop .ocd -> PNG, for baselines
      OcadViewer.TileSpike\   # THE DELIVERABLE: [JSExport] tile module + demo page
      OcadViewer.Wasm\        # Blazor harness — see below
    test\
      Test_MapModel\          # name is load-bearing (InternalsVisibleTo)
```

**`OcadViewer.TileSpike` is the thing that ships.** It started as a spike and proved
the architecture; rename it (`OcadViewer.TileRenderer`) when Stage 4 begins. Its demo
page stays useful as a harness that runs the renderer without Angular in the way.

**`OcadViewer.Wasm` (Blazor) is no longer the product direction**, but keep it: its
`/smoke` page is the fastest way to tell whether Skia, fonts, text metrics and
blending work in a browser at all, and that diagnostic has already earned its keep
twice. Its viewer page can be deleted once Stage 5 lands.

**Skip `PurplePenCore` entirely.** Its `MapDisplay` adds course objects and print
areas we don't need, and drags in 22 embedded PNGs, `symbols.xml`, six `.resx`
localisation sets and a DI package. `Map.Draw` already handles OCAD templates itself
via `RenderOptions.renderTemplates`.

## Architecture

```
Angular (unchanged)          JavaScript glue              .NET WebAssembly
──────────────────────       ───────────────────────      ────────────────────
version list, diff table     L.GridLayer.createTile       LoadMap(name, bytes)
folder picker, settings  ──► tile queue + scheduler   ──► RenderTile(x,y,w,h,px)
two L.Map + leaflet.sync     putImageData into canvas     Georeference()
```

The module exports `[JSExport]` methods and is loaded by `import('./main.js')`, which
boots the runtime and hands back the exports. Nothing about it is Blazor-specific, and
Angular needs no .NET knowledge beyond those few calls.

**Coordinates.** Maps are drawn in OCAD paper millimetres, so Leaflet uses
`L.CRS.Simple` and paper coordinates *are* Leaflet coordinates: `lng = paper X`,
`lat = paper Y`. Tile world rects come from `map.unproject`, which keeps the maths to
four lines and makes `leaflet.sync` work untouched. Georeferencing is available
(`Georeference()` returns the proj4 string) if a real CRS and an OSM underlay are
wanted later.

**Pixels, not PNG.** `RenderTile` returns raw RGBA straight into `ImageData` and
`putImageData`. No PNG encode, no async decode, and the boundary cost is negligible:
a 256×256 tile is 262 KB and the measured round trip was 204.8 ms against 204.1 ms
of actual rendering — roughly **0.7 ms of marshalling**.

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

### Settings

Start with `usePatternBitmaps = false` (glyph stamping — avoids `CreatePatternBrush`
and offscreen surfaces) and `blendOverprintedColors = true`. Both should be toggles so
they can be A/B measured.

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

### Stage 4 — Harden the tile layer (2–3 days) — **NEXT**

Promote the spike to a real component, standalone, before Angular is involved.

- Rename `OcadViewer.TileSpike` → `OcadViewer.TileRenderer`.
- Fix the three single-thread traps above: `fadeAnimation: false`, staleness check,
  and a pump with a `setTimeout` fallback plus a watchdog that clears the latch.
- Give `RenderTile` a cancellation path. `Map.Draw` takes a `throwOnCancel` callback
  invoked after every colour layer (`Map.cs:2184`); wire it to a flag JS can set, so
  a tile abandoned mid-render stops instead of finishing.
- Error handling across the boundary: a parse failure must surface as a message, not
  a dead runtime.
- Package the JS as a small ES module with a real API (`createOcadLayer(map, opts)`),
  not globals, so Angular can import it.

**Measure first, and this is the gating number:** drive a *zoomed-in* viewport end to
end through Leaflet — not `RenderTile` called directly — and record time-to-first-tile
and time-to-complete. The spike measured the renderer; this measures the system.

**Done when:** pan and zoom on `large_map.ocd` feel usable, tiles arrive
progressively, and nothing deadlocks when the tab is hidden and restored.

### Stage 5 — Replace `ocadToSvg` in the Angular app (3–4 days)

The point of the whole exercise. The version list, diff table, folder picker and
settings are untouched.

- Publish the .NET module into the Angular build. Its output is static files; copy
  the bundle into `ocad-versioner/src/assets/ocad-renderer/` as a build step, and add
  the folder to `angular.json` assets.
- Replace the body of
  [ocad-map-viewer.component.ts](ocad-versioner/src/app/components/ocad-map-viewer/ocad-map-viewer.component.ts):
  drop `svgOverlay` and `OcadReaderService.getSvgFromOcadObject`, add the tile layer.
  `leaflet.sync` and the two-map structure stay exactly as they are.
- Load the module once and share it across both maps; parse each version once.
- `staticwebapp.config.json` in the Angular app needs the WebAssembly MIME types and
  a `navigationFallback.exclude` for the renderer folder — see *Deployment*.

**Done when:** the existing app, with its real diff workflow, renders both versions
through the tile layer, and is measurably better than the SVG overlay on
`large_map.ocd`. Measure time-to-first-paint and interaction smoothness against the
current build on the same file.

### Stage 6 — Tune (2–4 days)

Measure, in this order:

- **AOT compilation.** Stage 3 showed cost is dominated by managed work in
  `SymDef`/`SymPath`, which is exactly what AOT targets. The original plan ranked
  this last on the assumption rasterisation dominated; that was wrong. It roughly
  doubles download and adds 10–30 min to publish, so it is a real trade.
- **Cold-start parse.** Successive parses of the same file measured 2496 → 2163 →
  1637 ms, against 6220 ms for the first parse in the Blazor app. Most of that gap
  looks like JIT warm-up. Confirm it, because it changes what "parse is slow" means
  and whether AOT or a warm-up parse is the better fix.
- Tile size (256 vs 512), `usePatternBitmaps` on/off, whether DotSpatial earns its
  ~600 KB, whether the trimmer roots can come off.

### Stage 7 — Deploy (1 day)

Fold the module build into the existing Angular workflow — see *Deployment*.

### Later, once the viewer has proven itself

**Move the diff to C#.** Today `ocad2geojson` parses each `.ocd` to GeoJSON for
`json-diff-service`, and the C# module parses the same file again for rendering —
two parsers, two parses, two memory footprints. Since the C# side already has a full
object model, it could produce the diff directly and `ocad2geojson` could be dropped
entirely. That also removes the last AGPL-3.0 dependency. Out of scope until the
renderer is in production, but it is where this naturally goes.

**File System Access API.** Unchanged by any of this; the Angular app already has it.

## WASM specifics

**Emscripten version match — verified, and it is good news.** SkiaSharp 3.119.4's targets select `3.1.56/{st|mt}[,simd]` archives for net9.0+, and .NET 10.0.102's `wasm-tools` manifest ships Emscripten **3.1.56**. Exact match; ABI mismatch is off the table. The targets also force `WasmBuildNative=true` (so native relinking, and the workload, are mandatory) and carry a link-order workaround for `dotnet/runtime#109289` — evidence the path is actively maintained.

**Payload estimate:** ~19–24 MB uncompressed, **~6–9 MB Brotli** first load; warm load ≈ 0. Mitigations in order of value: `InvariantGlobalization=true` (−1.4 MB ICU, safe — the vendored code uses only `InvariantCulture`, and `CodePagesEncodingProvider` is ICU-independent); trimming **with explicit `TrimmerRootAssembly` for `MapModel` and `DotSpatial.Projections`** (neither was ever trim-annotated); **AOT off initially** (roughly doubles download, adds 10–30 min publish); no `.pdb`. Skip lazy assembly loading — both candidate assemblies are needed at parse time, so it is not actually lazy.

**Fonts.** `SkiaFontManager.AddFontFile` is path-based (`EnhancedTypeface.cs:788`), `SKFontManager.Default` is empty on WASM, and fonts must be registered **before parsing** — `OcadImport.cs:2192` calls `TextFaceIsInstalled` during import. Ship the 8 Roboto/RobotoCondensed faces PurplePen already uses, install them into MEMFS at startup, and `SetDefaultFontFamilyName("Roboto")`. Register `Arial`→Roboto and `Helvetica`→Roboto aliases explicitly so weight/slant variants resolve; ship metric-compatible Liberation Sans (~700 KB) if Arial fidelity matters.

## Deployment

The renderer ships **inside the existing Angular app**, so there is no second Static
Web App and no second domain. What changes is that the Angular workflow has to build
a .NET project first.

```yaml
# added to the existing azure-static-web-apps-*.yml, before the SWA deploy step
- uses: actions/setup-dotnet@v4
  with: { dotnet-version: '10.0.x' }
- uses: actions/cache@v4            # wasm-tools is ~1.5 GB
  with:
    path: |
      ~/.dotnet
      ~/.nuget/packages
    key: wasm-${{ hashFiles('map-viewer/Directory.Packages.props') }}
- run: dotnet workload install wasm-tools
- run: >
    dotnet publish map-viewer/src/OcadViewer.TileRenderer -c Release
    -o ocad-versioner/src/assets/ocad-renderer
```

Oryx still builds the Angular app as it does today; it never sees the .NET project,
only the static files it produced. Expect the native link to add 3–10 minutes to a
cold build, so cache aggressively.

`ocad-versioner/src/staticwebapp.config.json` currently rewrites **everything** to
`/index.html` with no exclusions. That must change, or a missing runtime asset
returns HTML with a 200 and the module fails with a confusing error:

```json
{
  "navigationFallback": {
    "rewrite": "/index.html",
    "exclude": ["/assets/ocad-renderer/*", "/assets/*", "/img/*.{png,jpg,gif}", "/css/*"]
  },
  "mimeTypes": {
    ".wasm": "application/wasm",
    ".dat": "application/octet-stream",
    ".blat": "application/octet-stream",
    ".pdb": "application/octet-stream",
    ".ttf": "font/ttf"
  }
}
```

**`.wasm` → `application/wasm` matters twice.** Since .NET 8, `WasmEnableWebcil` means
managed assemblies also ship as `.wasm`, not just the runtime.

**Verify compression empirically.** Azure Static Web Apps does its own dynamic
compression and does *not* do `.br`-companion negotiation. Check the
`Content-Encoding` header on `dotnet.native.wasm` in DevTools once deployed. If it is
absent, the native module ships uncompressed; mitigate with a boot-resource hook that
fetches the `.br` companion and decodes it client-side.

**Watch the bundle.** `src/assets` ships wholesale to production. The renderer belongs
there; 9.8 MB test maps do not.

## Risks, ranked

| # | Risk | Status | Next check |
|---|---|---|---|
| 1 | **Interactive performance through Leaflet.** Per-tile cost is known and culling works, but the system has not been driven end to end while panning and zooming. | Open — the gating unknown. | Stage 4: time-to-first-tile and time-to-complete on a zoomed-in viewport. |
| 2 | **Single-thread stalls.** Leaflet's animations, the tile pump and the renderer all contend for one thread; a bare rAF pump already deadlocked once. | Known and understood; three specific mitigations are in *Architecture*. | Stage 4: hide and restore the tab, pan hard, confirm no stall. |
| 3 | **Worst case is still the whole map.** Fully zoomed out, every object is drawn however it is tiled — ~3.2 s of work for `large_map.ocd`. | Inherent. Tiles make it progressive, not cheaper. | Stage 5: is progressive fill acceptable to look at, or is a placeholder needed? |
| 4 | **Payload** ~5 MB brotli, 1.4 MB of it fonts. Now added to the Angular bundle rather than a separate app. | Measured. | Stage 6: TTF→WOFF2 would recover ~700 KB; check SWA actually compresses. |
| 5 | **Trimming breaks MapModel/DotSpatial** — neither was ever trim-annotated. | Rooted via `TrimmerRootAssembly`, so it cannot bite yet. Georeferencing resolving in the browser is evidence the embedded EPSG table survives. | Stage 6: remove roots, re-run the map corpus. |
| 6 | **Text fidelity vs. OCAD.** Maps name Arial and Times; we substitute Roboto, so glyph advances differ. | Open. | Stage 5 visual diff. Fix is metric-compatible faces (Liberation Sans / Arimo), ~700 KB. |
| 7 | **No automated fidelity test.** Renders have only ever been checked by eye. | Open, and now blocked: the desktop CLI fails under a Windows application control policy (`0x800711C7`), so baselines cannot be regenerated on this machine. | Needs the policy relaxed, or baselines generated elsewhere. |
| 8 | **CI cost** — 1.5 GB workload plus a native link now in the Angular pipeline. | Open. | Stage 7: cache `~/.dotnet` and `~/.nuget/packages`; measure a cold and warm build. |
| 9 | **Two parsers in the bundle** — `ocad2geojson` for diffing, C# for rendering. Each map is parsed twice. | Accepted for now. | Revisit once the renderer ships; see *Later*. |
| 10 | **LGPL-2.1 obligation** from shipping DotSpatial IL. | A decision, not a bug. | Record in `THIRD-PARTY-NOTICES.md`. |

### Resolved

- `SKTypeface.FromFile` on WebAssembly — works unmodified; the real trap was fonts
  served as empty 200s (see *Fonts*).
- Emscripten/SkiaSharp ABI match — .NET 10.0.102 ships Emscripten 3.1.56, exactly
  what SkiaSharp 3.119.4 wants.
- `System.Drawing.Drawing2D` for `browser-wasm` — compiles and publishes untouched.
- `Assembly.GetCallingAssembly` for the embedded EPSG table — resolves under trimming.
- JS↔WASM marshalling cost — ~0.7 ms for a 262 KB tile.
- Whether `leaflet.sync` works with a custom layer — yes, with no changes.

## Verification

- **Parser:** ported `OcadImportTests` against `TestFiles/ocadimport/` confirms OCAD
  6–12 and 2018 parse. Still outstanding — see Risk 7.
- **Fidelity:** Stage 1 golden PNGs against the same maps rendered in the browser.
  This is what catches WebAssembly-specific font and rasterisation drift, and it is
  currently blocked by the application control policy on the desktop CLI.
- **Tile seams:** adjacent tiles must line up exactly. A half-pixel error in the
  world rect shows as hairlines along every tile edge, and is easiest to spot on
  large area fills and long contours.
- **Against the current app:** the same `.ocd` open in the current build and the new
  one. Symbol shapes, colours and layering must match, and the synced views must
  still track each other exactly.
- **Performance, the actual goal:** on `large_map.ocd`, measure time-to-first-tile,
  time-to-complete and interaction smoothness, zoomed out *and* zoomed in, against
  the current SVG overlay. These numbers decide whether the migration continues.

## Out of scope

The Angular app keeps its version list, diff table, folder picker, settings and
`Releases/V<n>/` convention. The GeoJSON diff pipeline and `json-diff-service` are
untouched and `ocad2geojson-test` stays a dependency for now — moving the diff to C#
is sketched under *Later*, not scheduled.

No Blazor app ships. `OcadViewer.Wasm` stays in the solution as a diagnostic harness
only.

## Attribution (required)

PurplePen has no root LICENSE file — the BSD-3 text lives in every source header. Serving a WebAssembly module to a browser **is** a binary redistribution, so clause 2 applies. Create `vendor/LICENSE-PurplePen.txt` (verbatim from `InputOutput.cs:1-33`, © 2006–2008 Peter Golde), a root `THIRD-PARTY-NOTICES.md` covering PurplePen (BSD-3), DotSpatial.Projections (LGPL-2.1), SkiaSharp/HarfBuzzSharp (MIT), native Skia (BSD-3) and HarfBuzz, ImageSharp 2.1.3 (Apache-2.0) and Roboto (Apache-2.0) — and **serve it** from the app footer, not just commit it. Note clause 3: do not name the viewer anything using "Purple Pen" or "Peter Golde". The notice is served from the Angular app, which is where the module now ships.
