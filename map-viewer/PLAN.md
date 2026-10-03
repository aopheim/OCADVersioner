# Replace ocad2geojson with PurplePen's C# OCAD parser + renderer, in a Blazor WASM viewer

> **Status: Stages 0–3 complete. Stage 4 (interactive viewport) is next.**
> The extracted parser and renderer run in Blazor WebAssembly and render real maps
> correctly. Parse is 2.4x and render 1.8x slower than desktop; render cost is
> per-object, not per-pixel, so bbox culling - not lower resolution - is the lever.
>
> **To resume:**
> ```bash
> dotnet build map-viewer/MapViewer.slnx -c Release        # everything
> dotnet run --project map-viewer/src/OcadViewer.RenderCli -c Release -- <map.ocd> -o out.png
> dotnet run --project map-viewer/src/OcadViewer.Wasm -c Release   # http://localhost:5001
> pwsh map-viewer/tools/sync-purplepen.ps1 -PurplePenPath <path> -Check   # vendor drift
> ```
> Requires the .NET 10 SDK and the `wasm-tools` workload. A benchmark map is at
> `ocad-versioner/src/assets/tests/large_map.ocd` (untracked — 9.8 MB, and
> `angular.json` ships all of `src/assets` to production, so do not commit it there).


## Context

OCADVersioner is a version-control tool for orienteering map files in OCAD format — an Angular 17 SPA with no backend, deployed to Azure Static Web Apps. It reads `.ocd` files straight off disk via the File System Access API and treats a `Releases/V<n>/` folder tree as its version history.

Rendering today goes through the npm package `ocad2geojson-test`: `ocadToSvg()` builds **one giant inline SVG element** for the whole map, which is handed to `L.svgOverlay` in Leaflet ([ocad-map-viewer.component.ts:169](ocad-versioner/src/app/components/ocad-map-viewer/ocad-map-viewer.component.ts:169)). Every symbol becomes a live DOM node, and two of these are on screen at once for the side-by-side comparison. On large maps this is slow — the browser is laying out and hit-testing tens of thousands of SVG nodes. Each `.ocd` is also parsed **twice** per version switch: once in the web worker for GeoJSON diffing, once on the main thread for SVG.

The fix is to stop treating the map as DOM. PurplePen (`C:\repos\PurplePen`) is a mature desktop orienteering application that already has a complete, high-fidelity OCAD parser and renderer in C#, and — critically — it is already factored behind a platform-neutral drawing abstraction. The exploration below found the extraction is far cheaper than expected.

**Outcome intended:** a standalone Blazor WebAssembly map viewer that parses `.ocd` with PurplePen's parser and renders it to a canvas with PurplePen's renderer, at full symbol fidelity and much better performance than the SVG overlay. The Angular app stays as-is for now; whether to migrate the rest of it is a later decision informed by this.

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
| Hosting model | Blazor **WebAssembly** — must remain a static site |
| Fidelity | **Full** — all symbol types, patterns, hatching, glyphs, text |
| Scope | **Viewer only** — Angular app is not replaced yet |
| Integration | **Standalone Blazor app**, own deployment route, no Angular coupling |
| File access | Plain `<input type="file">` first; File System Access API interop later |

---

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
  ocad-versioner\                       # existing Angular app — untouched
  THIRD-PARTY-NOTICES.md                # served by the app, not just committed
  map-viewer\                           # NEW .NET root
    MapViewer.slnx
    Directory.Build.props
    Directory.Packages.props            # CPM: versions + audit suppressions
    tools\sync-purplepen.ps1
    vendor\
      UPSTREAM.md                       # commit SHA, SHA256 manifest, patch log
      LICENSE-PurplePen.txt             # BSD-3, Peter Golde
      LICENSE-DotSpatial.txt            # LGPL-2.1
      LICENSE-Roboto.txt                # Apache-2.0
      PurplePen\{Graphics2D,MapModel,Map_SkiaStd,DotSpatial.Projections,fonts}\
    src\
      Graphics2D\  MapModel\  DotSpatial.Projections\  Map_SkiaStd\   # thin csprojs
      OcadViewer.Core\        # our glue: MapDocument, Viewport, TileCache, FontPack
      OcadViewer.RenderCli\   # Stage-1 desktop renderer
      OcadViewer.Wasm\        # Blazor WASM app
    test\
      Test_MapModel\          # name is load-bearing (InternalsVisibleTo)
```

**Skip `PurplePenCore` entirely.** Its `MapDisplay` adds course objects and print areas we don't need, and drags in 22 embedded PNGs, `symbols.xml`, six `.resx` localisation sets and a DI package. `Map.Draw` already handles OCAD templates itself via `RenderOptions.renderTemplates`.

## Threading: cooperative slicing, not `Task.Run`

`Map`'s `ReaderWriterLockSlim` (`Map.cs:882`, `LockRecursionPolicy.SupportsRecursion`) works as-is on single-threaded WASM — one thread means `TryEnterWriteLock(0)` always succeeds and the blocking paths are unreachable. **Do not touch `Map.cs`.**

But `Task.Run` will not help: on single-threaded WASM it queues onto the same thread, so a synchronous `Map.Draw` still freezes the tab for its full duration.

Use the API PurplePen already provides. `RenderOptions.colorBeginDrawExclusive` / `colorEndDrawInclusive` (`Map.cs:2776-2795`) slice a render by colour layer, and `Map.Draw` iterates colours in strict order (`Map.cs:2222-2236`). Render one colour layer per slice with `await Task.Yield()` between them: the UI stays responsive and the map fills in bottom-up, which reads as intentional progressive rendering. Wire the fourth `Map.Draw` parameter — `Action throwOnCancel` (`Map.cs:2184`, invoked after every colour) — to a `CancellationToken` so a new pan/zoom aborts the in-flight render, exactly as `CacheableMapDisplay.cs:44` does.

Web-worker multithreading (`WasmEnableThreads`, the `mt` Skia archives) needs COOP/COEP headers site-wide and is still experimental. Defer.

## Rendering strategy

Three designs, in increasing cost:

- **(A) Redraw the visible rect every frame.** Dead on arrival — a full `Map.Draw` takes 100 ms–3 s; you have 16 ms.
- **(B) Render once, transform during interaction, re-render sharp on settle** (~150 ms after last input). ~120 lines, and already fixes the stated problem. **Build this in Stage 4.**
- **(C) Multi-resolution tile cache** modelled on `CachedDrawing.cs`: a fast low-res backdrop plus a high-res exact render painted over it, with versioned invalidation and pruning. ~300 more lines. **Stage 5, only if measurement demands it.**

`CachedDrawing` uses real background threads; our version replaces that with the colour-layer slicing above — same cancellation semantics, cooperative scheduling.

`MapCanvas.Paint` must **never** call `Map.Draw` — it only blits cached images. All drawing happens in the async slice loop. That separation is what keeps the frame loop smooth.

**`minResolution` is paper-mm per destination pixel** and must include `devicePixelRatio`, or a 2× display culls features it should not:

```csharp
renderOpts.minResolution = viewport.VisibleWorldRect.Width
                         / (viewport.ViewportPixels.Width * devicePixelRatio);
```

For the low-res backdrop, use that tile's own coarser value — that is what makes it fast.

Start with `usePatternBitmaps = false` (glyph stamping, avoids `CreatePatternBrush` and offscreen surfaces on WASM) and `blendOverprintedColors = true`. Make both debug toggles.

**Y-axis:** OCAD paper coords are Y-up, screen is Y-down. Use `inverted: true` and mirror `RenderingUtil.GetTransform` rather than deriving your own — fastest way to guarantee output matches PurplePen.

**Input:** a small JS module with rAF-batched `pointer*`/`wheel` handlers calling into .NET via `DotNetObjectReference`. Do **not** use Blazor's `@onpointermove` — the per-event interop round-trip is too slow to pan smoothly.

**File loading:** write picked files to MEMFS under `/maps/` and use the vendored `Skia_FileLoader("/maps")` unchanged — its `SearchForFile` already does the fallback OCAD templates need. Zero new code.

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
### Stage 4 — Interactive viewport, design B (3–4 days) — **NEXT**

`ViewportController`, rAF-batched JS pointer input, render-once-then-transform on a
settle delay, cancellation through `Map.Draw`'s `throwOnCancel`, and colour-layer
slicing with `await Task.Yield()` so a multi-second render does not freeze the tab.

**First measurement, before building any of it:** render a *zoomed-in* rectangle of
`large_map.ocd` and compare against the 7.9 s full-extent figure. Stage 3 showed
cost is per-object, so this is what tells you whether bounding-box culling pays —
and the whole interactive design rests on it. If a 10% viewport is not roughly 10×
cheaper, rethink before writing the viewport code.

**Done when:** pan/zoom is smooth on a real map, measured head to head against the
current Angular/Leaflet viewer on the same file. If it is not visibly better, that
is important information; surface it before investing further.

### Stage 5 — Tile cache and tuning (3–5 days)

Revised by Stage 3's finding that render cost barely tracks resolution:

- **Measure AOT first, not last.** It targets the managed-side work that dominates
  here, so it is the most promising single lever. The plan originally deprioritised
  it on the assumption that rasterisation dominated; that assumption was wrong.
- **Do not port `CachedDrawing.cs`'s low-res backdrop on faith.** It assumes a
  coarse render is cheap. Measure a backdrop render before building around one.
- Then: `usePatternBitmaps` on/off; whether DotSpatial earns its ~600 KB; whether
  the trimmer roots can come off.

### Stage 6 — Deploy (1 day) · Stage 7 — File System Access API (2–3 days)

## WASM specifics

**Emscripten version match — verified, and it is good news.** SkiaSharp 3.119.4's targets select `3.1.56/{st|mt}[,simd]` archives for net9.0+, and .NET 10.0.102's `wasm-tools` manifest ships Emscripten **3.1.56**. Exact match; ABI mismatch is off the table. The targets also force `WasmBuildNative=true` (so native relinking, and the workload, are mandatory) and carry a link-order workaround for `dotnet/runtime#109289` — evidence the path is actively maintained.

**Payload estimate:** ~19–24 MB uncompressed, **~6–9 MB Brotli** first load; warm load ≈ 0. Mitigations in order of value: `InvariantGlobalization=true` (−1.4 MB ICU, safe — the vendored code uses only `InvariantCulture`, and `CodePagesEncodingProvider` is ICU-independent); trimming **with explicit `TrimmerRootAssembly` for `MapModel` and `DotSpatial.Projections`** (neither was ever trim-annotated); **AOT off initially** (roughly doubles download, adds 10–30 min publish); no `.pdb`. Skip lazy assembly loading — both candidate assemblies are needed at parse time, so it is not actually lazy.

**Fonts.** `SkiaFontManager.AddFontFile` is path-based (`EnhancedTypeface.cs:788`), `SKFontManager.Default` is empty on WASM, and fonts must be registered **before parsing** — `OcadImport.cs:2192` calls `TextFaceIsInstalled` during import. Ship the 8 Roboto/RobotoCondensed faces PurplePen already uses, install them into MEMFS at startup, and `SetDefaultFontFamilyName("Roboto")`. Register `Arial`→Roboto and `Helvetica`→Roboto aliases explicitly so weight/slant variants resolve; ship metric-compatible Liberation Sans (~700 KB) if Arial fidelity matters.

## Deployment

**A third Static Web App with its own workflow.** The existing two use Oryx with `app_location: /ocad-versioner`; **Oryx cannot build .NET 10 Blazor WASM** and certainly has no `wasm-tools`. So build in GitHub Actions and deploy prebuilt:

```yaml
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
- uses: Azure/static-web-apps-deploy@v1
  with:
    app_location: 'publish/wwwroot'
    skip_app_build: true          # load-bearing: keep Oryx away
```

Add `paths: ['map-viewer/**']` so Angular-only commits do not trigger a 3–10 minute native link.

`staticwebapp.config.json` in the Blazor `wwwroot` needs:

- **`navigationFallback.exclude` containing `/_framework/*`** — without it a missing framework asset returns `index.html` with a 200, and Blazor fails with a cryptic integrity error instead of a clean 404.
- `mimeTypes` for `.wasm` → `application/wasm` (**since .NET 8, `WasmEnableWebcil` means managed assemblies ship as `.wasm`, not `.dll`**), plus `.dat`, `.blat`, `.dll`, `.pdb`.
- `index.html` `no-cache`; `/_framework/*` immutable long-cache.

**Verify compression empirically.** SWA does its own dynamic compression and does *not* do `.br`-companion negotiation. In Stage 6, check the `Content-Encoding` header on `dotnet.native.wasm` in DevTools. If absent, the 8 MB native wasm ships raw — mitigate with a `loadBootResource` hook that fetches the `.br` companion and decodes it client-side.

## Risks, ranked

| # | Risk | Status | Cheap experiment |
|---|---|---|---|
| 1 | **Fonts do not resolve in the browser** | **Resolved in Stage 2.** File-backed typefaces work on WebAssembly; no upstream patch needed. The real trap was fonts being served as empty 200s — see the fonts section. | done |
| 2 | **Render perf in single-threaded WASM** — the entire point of the project | **Measured, still the main risk.** Browser is 2.4× desktop on parse, 1.8× on render: 6.2 s and 7.9 s for `large_map.ocd` at full extent. Usable only if culling and slicing bring the interactive case down. | Stage 4: measure a zoomed-in viewport. Cost is per-object, so culling is the lever. |
| 3 | **Payload** | **Resolved in Stage 2.** 15 MB raw, ~5 MB brotli, under the 6–9 MB estimate. Fonts are 1.4 MB of it; converting TTF→WOFF2 would recover roughly half. | done |
| 4 | **Trimming breaks MapModel/DotSpatial** (never annotated; hardcoded manifest-resource name) | Open. Both are rooted via `TrimmerRootAssembly`, so it cannot bite yet. | Stage 3+: remove the roots, run the Stage-1 corpus through the browser. |
| 5 | `System.Drawing.Drawing2D` may not resolve for browser-wasm | **Resolved.** It compiles and publishes for `browser-wasm` untouched. | done |
| 6 | `Assembly.GetCallingAssembly()` (`AuthorityCodeHandler.cs:75`) — inlining could silently break the EPSG lookup | **Resolved without AOT.** `large_map.ocd` resolves a full proj4 string in the browser under trimming. Re-check if AOT is turned on. | done for now |
| 7 | **Overprint blending** differs between desktop and WASM Skia | **Partly resolved.** `PushBlending(Darken)` produces a correctly darkened overlap in the browser. Not yet compared pixel-for-pixel on a real map. | Stage 3 golden-image diff on an overprinted map. |
| 8 | **CI cost** — 1.5 GB workload + native link per build | Open. Measured locally: first native link ~1m35s, incremental C# builds ~11 s, a relink when native inputs change ~2m25s. | Stage 6: cache `~/.dotnet` + `~/.nuget/packages`, and filter on `paths:`. |
| 9 | **LGPL-2.1 obligation** from shipping DotSpatial IL | Open — a decision, not a bug. | Short decision, recorded in `THIRD-PARTY-NOTICES.md`. |
| 10 | **Text fidelity vs. OCAD** | Open. Maps name Arial/Times; we substitute Roboto, so glyph advances differ from the desktop. | Stage 3 visual diff. Fix is to ship metric-compatible faces (Liberation Sans / Arimo). |

## Verification

- **Parser:** ported `OcadImportTests` against `TestFiles/ocadimport/` confirms OCAD 6–12 + 2018 parse.
- **Fidelity:** Stage 1 golden PNGs vs. the same maps rendered in the browser — this is what catches WASM-specific font and rasterisation drift.
- **Against the current app:** same `.ocd` open in both viewers; symbol shapes, colours and layering must match.
- **Performance, the actual goal:** on one real large map, measure parse time, first-render time and pan/zoom frame time in both viewers. **You need to supply that map** — the committed fixtures are small synthetic files, useless for this. These numbers decide whether the migration continues.

## Out of scope

The Angular app, the GeoJSON diff pipeline, `json-diff-service`, version creation and the diff table are untouched; `ocad2geojson-test` stays a dependency. Whether the diff moves to C# is deferred until the viewer proves out.

## Attribution (required)

PurplePen has no root LICENSE file — the BSD-3 text lives in every source header. A Blazor WASM app served to a browser **is** a binary redistribution, so clause 2 applies. Create `vendor/LICENSE-PurplePen.txt` (verbatim from `InputOutput.cs:1-33`, © 2006–2008 Peter Golde), a root `THIRD-PARTY-NOTICES.md` covering PurplePen (BSD-3), DotSpatial.Projections (LGPL-2.1), SkiaSharp/HarfBuzzSharp (MIT), native Skia (BSD-3) and HarfBuzz, ImageSharp 2.1.3 (Apache-2.0) and Roboto (Apache-2.0) — and **serve it** from the app footer, not just commit it. Note clause 3: do not name the viewer anything using "Purple Pen" or "Peter Golde".
