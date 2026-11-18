# End-to-end tests

A browser-driven suite that walks every page's happy path against the real
published bundle. Its job is a **regression gate on pull requests**: fast,
deterministic, and run on every push. Depth lives in the unit suites
(Core, Ocd, Merge, Architecture); this suite proves the pieces still meet in a
browser.

## Decisions

| Question | Decision | Why |
|---|---|---|
| Purpose | PR regression gate | Must stay a few minutes and never flake. |
| Target | Ephemeral local build, served fresh per run | Hermetic. There is no backend, so "the stack" is a folder of static files. |
| Runner | Playwright for .NET + NUnit 4 | Same language, test runner and central package management as the rest of `map-viewer`. Runs with `dotnet test`. |
| Build under test | Non-AOT `dotnet publish`, served by `tools/serve.mjs` | AOT costs up to an hour. A plain publish is still the real bundle - trimming, fingerprinted assets, SWA-style fallback routing - and `serve.mjs` already mimics Azure Static Web Apps. AOT-only bugs are out of scope here. |
| Project folder | `showDirectoryPicker` stubbed to return an OPFS directory | Playwright cannot drive the native folder picker. OPFS hands back a *real* `FileSystemDirectoryHandle`, so `projectFolder.js` runs unmodified, writes (create version) work, and each test context starts empty. |
| Fixtures | Built per test, in the browser | The test copies `testdata/*.ocd` into OPFS and lays out releases with `OcadViewer.Versioning.FolderLayout`, so fixtures cannot drift from the layout the app reads. Nothing extra is checked in. |
| Selectors | `data-testid` on the controls tests touch | Copy is localised (en/no) and the map is a canvas with no accessible text. Ids survive copy edits and translations. |
| Visual assertions | Structural only | Tiles loaded, overlay image present, change share shown, no console errors. No pixel baselines - they are GPU/font sensitive and churn. |
| Browsers | Chromium for every flow; one Firefox test | The workspace needs the File System Access API (Chromium only). Firefox checks the unsupported-browser message and that `/viewer` still works there. |
| Language | English pinned; one switch test | Translation key parity is already enforced by `ArchitectureTests.TranslationTests`. |
| CI | New `e2e` job in `ci.yml`, parallel to `build-and-test` | `build-and-test` stays fast and keeps its name (it is a required check). The E2E project is kept out of `MapViewer.slnx`, so that job neither builds Playwright nor runs these tests. NuGet packages and Playwright's browsers are cached. |

## User flows

Each flow is one happy path. Every test also asserts there were no console
errors and no `#blazor-error-ui`.

### About (`/about`)
1. **Loads** - title heading visible, links to the workspace and viewer work.
2. **Language flags** - clicking the Norwegian flag changes a known label;
   reload keeps Norwegian (localStorage). This is the language-switch test.

### Viewer (`/viewer`)
3. **One map** - switch to one-map mode, choose `point_line_area.ocd`:
   tiles load in the visible pane, the pane button names the file.
4. **Two maps and the overlay** *(scaffolded)* - choose
   `point_line_area.ocd` (older) and `point_line_area_moved.ocd` (newer):
   both panes draw, the changes toggle enables, turning it on shows the change
   mask on the newer pane and a non-zero share of the map.

### Workspace (`/`)
5. **Choose a folder** *(scaffolded)* - the start dialog opens by itself;
   with a project holding one `.ocd`, choosing the folder closes the dialog,
   shows the project name and draws both panes.
6. **Ambiguous working file** - a folder with two `.ocd` files in its root
   offers a dropdown; picking one settles it.
7. **Compare two versions** - a project with `Releases/V1` and `V2` lists
   both. Picking V1 in the left dropdown (`older-version`) and V2 in the right
   (`newer-version`) draws that pair, and the version list highlights both.
   Turning the overlay on shows the change mask; the map toggle (`map-toggle`)
   hides the newer map under it. (The diff table is off the page for now; its
   test comes back with it.)
8. **Create a version** - type a title and description, submit: `V2`
   appears at the top of the list, and OPFS now holds `Releases/V2/V2.ocd`
   (byte-identical to the working file) and `Releases/V2/V2.json` with the
   title and description.
9. **Refresh** - a release added to OPFS behind the app's back shows up after
   the refresh button.
10. **Change project** - the folder button reopens the start dialog; choosing
    a second OPFS project shows its name and maps.
11. **Settings** - open settings, enter an EPSG code, save; reopening (and
    reloading) shows it kept.

### Everywhere else
12. **Not found** - an unknown path renders the not-found page (via the SWA
    style fallback in `serve.mjs`).
13. **Firefox** - `/` shows the unsupported-browser message with its link to
    the About page's browser section; `/viewer` loads a map.

## Architecture

```
tests/OcadViewer.E2ETests/
  OcadViewer.E2ETests.csproj   Microsoft.Playwright.NUnit, refs OcadViewer.Versioning
  AppServer.cs                 [SetUpFixture]: finds the published wwwroot, starts serve.mjs on a free port
  AppPageTest.cs               base fixture: [Category("E2E")], English pinned, console errors fail
                               the test, trace kept on failure, wait helpers
  OpfsProject.cs               OPFS fixture builder (C# side), using FolderLayout
  Scripts/opfsPicker.js        init script: showDirectoryPicker -> OPFS project directory
  Repo.cs                      locates map-viewer, testdata and the embedded scripts
  Viewer/ViewerTests.cs        flow 4 (scaffolded)
  Workspace/WorkspaceTests.cs  flow 5 (scaffolded)
```

### The server
`AppServer` is a one-time setup fixture. It reads the published site from
`E2E_SITE` (CI) or defaults to `src/OcadViewer.Wasm/bin/Release/net10.0/publish/wwwroot`,
starts `node tools/serve.mjs <site> 0` so the OS picks a free port, reads the
port back from the server's first line, and polls until it answers. The server
is also killed on process exit, so an aborted run does not leave it behind.
`E2E_BASE_URL` skips all of that and points at an already running server.

### Waiting for the app
Startup downloads the WASM runtime and fonts, so tests never sleep; every
expectation waits up to 60 s for what it checks to appear. Work is done once the
`busy-bar` reports `data-busy="false"`. A pane has drawn once its
`.leaflet-tile-loaded` canvases are present.

### The folder stub
`opfsPicker.js` runs before any page script, and only in fixtures that
install it (so the Firefox test still sees an unsupported browser). It replaces
`window.showDirectoryPicker` with a function that returns
`navigator.storage.getDirectory()` → `e2e-projects/<name>`, where `<name>` is
read from `localStorage['e2e.project']` (set by `OpfsProject.PickNextAsync`
before clicking; with none set the picker acts as if the user closed it). The
same script defines `window.__e2eOpfs`, the only code that knows where test
projects live; `OpfsProject` writes through it. Fixture
files are written from C# by evaluating a small script that receives the bytes
as base64. Each Playwright test gets a fresh browser context, and with it an
empty OPFS.

### What the stub does not cover
The real permission prompt and the native picker. Both are browser UI, not
our code, and `projectFolder.js` treats the returned handle the same either way.

## Running locally

From `map-viewer` (about 2 minutes for the publish, seconds for the tests):

```bash
dotnet publish src/OcadViewer.Wasm -c Release -p:RunAOTCompilation=false -p:WasmStripILAfterAOT=false
dotnet build tests/OcadViewer.E2ETests -c Release
pwsh tests/OcadViewer.E2ETests/bin/Release/net10.0/playwright.ps1 install chromium
dotnet test tests/OcadViewer.E2ETests -c Release --no-build
```

On Windows without PowerShell 7, `powershell -File` runs the same script.
`HEADED=1` shows the browser. Traces of failed tests are written to
`tests/OcadViewer.E2ETests/bin/Release/net10.0/playwright-traces/` (CI uploads
them as the `playwright-traces` artifact); open one with
`playwright.ps1 show-trace <file>`.

## Not covered on purpose
- Drag-and-drop into the viewer and keyboard shortcuts (thin wrappers over the
  same code paths the buttons use).
- Pixel correctness of renders (the RenderCli and unit suites own the renderer).
- AOT-specific behaviour (would need the hour-long publish; a nightly job can be
  added later with the same tests and `E2E_SITE` pointed at the AOT output).
