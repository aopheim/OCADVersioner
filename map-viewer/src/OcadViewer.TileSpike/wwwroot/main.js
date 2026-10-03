// Boots the .NET runtime and hands its exports to the page.
//
// Nothing Blazor is involved. This is the whole integration surface that the
// Angular app would need if it kept Leaflet and swapped only its layer.

import { dotnet } from './_framework/dotnet.js';

const { getAssemblyExports, getConfig, runMain } = await dotnet
    .withDiagnosticTracing(false)
    .create();

const config = getConfig();
const exports = await getAssemblyExports(config.mainAssemblyName);

window.ocad = exports.OcadViewer.TileSpike.Interop;

// Fonts must be registered before any map is parsed: the OCAD importer asks
// whether a face is installed while it reads text symbols.
const FONTS = [
    'Roboto-Regular.ttf', 'Roboto-Bold.ttf', 'Roboto-Italic.ttf', 'Roboto-BoldItalic.ttf',
    'RobotoCondensed-Regular.ttf', 'RobotoCondensed-Bold.ttf',
    'RobotoCondensed-Italic.ttf', 'RobotoCondensed-BoldItalic.ttf',
];

for (const name of FONTS) {
    const response = await fetch(`fonts/${name}`);
    const bytes = new Uint8Array(await response.arrayBuffer());
    window.ocad.StageFont(name, bytes);
}
const faces = window.ocad.InstallFonts();

await runMain();

window.dispatchEvent(new CustomEvent('ocad-ready', { detail: { faces } }));
