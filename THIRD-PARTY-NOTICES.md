# Third-party notices

OCADVersioner redistributes the following third-party software. The Blazor map
viewer serves this file to the browser, because delivering a WebAssembly
application is a binary redistribution and several of these licences require the
notice to travel with it.

Full licence texts live in `map-viewer/vendor/`.

## Purple Pen — MapModel, Graphics2D, Map_SkiaStd

Copyright © 2006–2008 Peter Golde. Licensed under the **BSD 3-Clause** licence
(`map-viewer/vendor/LICENSE-PurplePen.txt`).

Source: <https://github.com/petergolde/PurplePen>

The OCAD file parser and the map renderer are unmodified Purple Pen source,
vendored under `map-viewer/vendor/PurplePen/`. The exact upstream commit and a
SHA-256 manifest of every file are recorded in `map-viewer/vendor/UPSTREAM.md`.

Note clause 3 of the licence: neither the name of Peter Golde, nor "Purple Pen",
nor the names of its contributors may be used to endorse or promote products
derived from this software without prior written permission. This project must
therefore not be named or marketed in a way that implies such an endorsement.

## DotSpatial.Projections

Licensed under the **GNU Lesser General Public License, version 2.1**
(`map-viewer/vendor/LICENSE-DotSpatial.txt`).

Complete corresponding source is vendored under
`map-viewer/vendor/PurplePen/DotSpatial.Projections/` and is built by
`map-viewer/src/DotSpatial.Projections/DotSpatial.Projections.csproj`. The
assembly is excluded from IL trimming (`IsTrimmable=false`) so that the shipped
binary remains an unmodified build of that source, which preserves the
recipient's ability to relink the application against a modified version of the
library as LGPL 2.1 §6 requires.

To rebuild it: install the .NET 10 SDK, then run
`dotnet build map-viewer/MapViewer.slnx -c Release`.

## SkiaSharp and HarfBuzzSharp

Copyright © Microsoft Corporation and the SkiaSharp contributors. Licensed under
the **MIT** licence.

These packages statically link the following native libraries into the published
WebAssembly module:

- **Skia** — copyright © Google Inc., BSD 3-Clause.
- **HarfBuzz** — "Old MIT" licence.

## SixLabors.ImageSharp 2.1.3

Licensed under **Apache License 2.0**.

The version is pinned to exactly `2.1.3` deliberately: later releases moved to
the Six Labors Split License. This pin is inherited from upstream Purple Pen.

## Roboto and Roboto Condensed

Copyright © Google Inc. Licensed under **Apache License 2.0**
(`map-viewer/vendor/LICENSE-Roboto.txt`).

Bundled with the viewer because a browser provides no system fonts to the
rendering engine.

## Leaflet

Copyright © 2010–2023 Volodymyr Agafonkin. Licensed under **BSD 2-Clause**
(`map-viewer/src/OcadViewer.Wasm/wwwroot/lib/leaflet/LICENSE-leaflet.txt`).

Provides the map viewport: pan, zoom, and the tile grid that the renderer draws
into.

## Leaflet.Sync

Copyright © 2013–2017 Bjørn Sandvik / Jan Pieter Waagmeester. Licensed under
**BSD 3-Clause**
(`map-viewer/src/OcadViewer.Wasm/wwwroot/lib/leaflet/LICENSE-leaflet.sync.txt`).

Keeps the two side-by-side maps in step.

## Leaflet-Geoman (free edition) 2.19.2

Copyright © 2017 Sumit Kumar. Licensed under the **MIT** licence
(`map-viewer/src/OcadViewer.Wasm/wwwroot/lib/leaflet-geoman/LICENSE-leaflet-geoman.txt`).

Draws and edits the regions on the merge screen.

## Bootstrap

Copyright © 2011–2024 The Bootstrap Authors. Licensed under the **MIT** licence
(<https://github.com/twbs/bootstrap/blob/main/LICENSE>; the notice is kept in the
header of each file under `map-viewer/src/OcadViewer.Wasm/wwwroot/lib/bootstrap/dist/`).

## Bootstrap Icons

Copyright © 2019–2024 The Bootstrap Authors. Licensed under the **MIT** licence
(`map-viewer/src/OcadViewer.Wasm/wwwroot/lib/bootstrap-icons/LICENSE-bootstrap-icons.txt`).

## Application Insights JavaScript SDK

Copyright © Microsoft Corporation. Licensed under the **MIT** licence
(`map-viewer/src/OcadViewer.Wasm/wwwroot/lib/appinsights/LICENSE-applicationinsights.txt`).

Vendored rather than loaded from a CDN, and only fetched at all when a
monitoring key is configured, so a local build makes no third-party requests.
