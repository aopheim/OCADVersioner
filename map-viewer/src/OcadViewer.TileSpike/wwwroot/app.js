/* Leaflet tile layer backed by the .NET renderer running in WebAssembly.
 *
 * The map is drawn in OCAD paper millimetres, so the map uses L.CRS.Simple and
 * paper coordinates are used directly as Leaflet coordinates: lng = paper X,
 * lat = paper Y. That keeps the tile maths trivial and makes leaflet.sync work
 * without knowing anything about the layer.
 */

const TILE_SIZE = 256;

let mapBounds = null;          // paper extent of the loaded map
let layers = [];               // the two tile layers
const stats = { tiles: 0, totalMs: 0, maxMs: 0, skipped: 0, stale: 0 };

/* ---------------------------------------------------------------- scheduler */

/* WebAssembly is single threaded, so a tile render blocks everything including
 * Leaflet's pan animation. Rendering one tile per animation frame keeps the UI
 * able to breathe between tiles, and lets the browser paint what is already
 * done. This is the cooperative-slicing idea the plan calls for, at tile
 * granularity rather than colour-layer granularity. */
const queue = [];
let pumping = false;

function schedule() {
    if (pumping) return;
    pumping = true;
    requestAnimationFrame(pump);
}

function pump() {
    const job = queue.shift();
    if (!job) {
        pumping = false;
        onQueueDrained();
        return;
    }
    try {
        renderTile(job);
    } catch (err) {
        console.error('tile render failed', err);
        job.done(err, job.tile);
    }
    requestAnimationFrame(pump);
}

function renderTile(job) {
    const map = job.layer._map;
    if (!map || !mapBounds) {
        job.done(null, job.tile);
        return;
    }

    // Leaflet discards tiles as soon as the view moves past them, but a job
    // already in this queue would still be rendered - and at roughly half a
    // second each, rendering tiles nobody will see is the difference between
    // responsive and unusable. Drop anything the layer has pruned.
    const key = job.layer._tileCoordsToKey(job.coords);
    if (!job.layer._tiles[key]) {
        stats.stale++;
        job.done(null, job.tile);
        return;
    }

    const nwPoint = job.coords.scaleBy(job.size);
    const sePoint = nwPoint.add(job.size);
    const nw = map.unproject(nwPoint, job.coords.z);
    const se = map.unproject(sePoint, job.coords.z);

    const minX = nw.lng, maxX = se.lng;
    const minY = se.lat, maxY = nw.lat;   // lat decreases downwards

    // Skip tiles that cannot contain anything. Worth doing: it is the same
    // bounding-box idea the renderer uses internally, one level up.
    if (maxX < mapBounds.minX || minX > mapBounds.maxX ||
        maxY < mapBounds.minY || minY > mapBounds.maxY) {
        stats.skipped++;
        job.done(null, job.tile);
        return;
    }

    const bytes = window.ocad.RenderTile(minX, minY, maxX - minX, maxY - minY, job.size.x);
    const ms = window.ocad.GetLastRenderMs();

    stats.tiles++;
    stats.totalMs += ms;
    stats.maxMs = Math.max(stats.maxMs, ms);

    if (bytes) {
        const ctx = job.tile.getContext('2d');
        ctx.putImageData(new ImageData(new Uint8ClampedArray(bytes), job.size.x, job.size.y), 0, 0);
    }
    job.done(null, job.tile);
}

/* ---------------------------------------------------------------- the layer */

const OcadTileLayer = L.GridLayer.extend({
    createTile(coords, done) {
        const size = this.getTileSize();
        const tile = document.createElement('canvas');
        tile.width = size.x;
        tile.height = size.y;
        queue.push({ tile, coords, size, done, layer: this });
        schedule();
        return tile;
    }
});

/* ------------------------------------------------------------------- setup */

function makeMap(id) {
    return L.map(id, {
        crs: L.CRS.Simple,
        minZoom: -6,
        maxZoom: 4,
        zoomSnap: 0,
        attributionControl: false,

        // Essential, not cosmetic. Leaflet fades tiles in from a
        // requestAnimationFrame loop, and a tile render here blocks the main
        // thread for 200 ms to over a second. That starves the fade, which then
        // never finishes: tiles stay stuck at partial opacity and the map looks
        // washed out. Any main-thread tile renderer has to turn this off.
        fadeAnimation: false
    });
}

const mapLeft = makeMap('mapLeft');
const mapRight = makeMap('mapRight');

// Leaflet refuses to sync maps that have no view yet, so give both a provisional
// one; loading a map replaces it via fitBounds.
mapLeft.setView([0, 0], 0);
mapRight.setView([0, 0], 0);

// Exactly what the Angular app does today. leaflet.sync only calls setView,
// getCenter and getZoom, so it neither knows nor cares that the layer below is
// now canvas tiles from .NET rather than an inline SVG.
mapLeft.sync(mapRight);
mapRight.sync(mapLeft);

/* ------------------------------------------------------------------ loading */

function resetStats() {
    stats.tiles = 0;
    stats.totalMs = 0;
    stats.maxMs = 0;
    stats.skipped = 0;
    stats.stale = 0;
}

let drainedAt = 0;
let queueStartedAt = 0;

function onQueueDrained() {
    drainedAt = performance.now();
    report();
}

let lastLoad = null;

function report() {
    const el = document.getElementById('stats');
    if (!lastLoad) { el.textContent = 'no map loaded'; return; }

    const avg = stats.tiles ? (stats.totalMs / stats.tiles) : 0;
    const wall = queueStartedAt ? Math.round(drainedAt - queueStartedAt) : 0;

    el.textContent =
        `file          ${lastLoad.name} (${(lastLoad.bytes / 1048576).toFixed(1)} MB), OCAD ${lastLoad.version}\n` +
        `parse         ${lastLoad.parseMs} ms\n` +
        `georeference  ${window.ocad.Georeference()}\n` +
        `\n` +
        `tiles drawn   ${stats.tiles}   (outside map: ${stats.skipped}, stale: ${stats.stale})\n` +
        `tile render   avg ${avg.toFixed(1)} ms, slowest ${stats.maxMs.toFixed(1)} ms\n` +
        `sum of tiles  ${Math.round(stats.totalMs)} ms\n` +
        `wall clock    ${wall} ms from first tile queued to last tile drawn`;
}

async function loadFile(file) {
    const state = document.getElementById('state');
    state.textContent = `reading ${file.name}…`;

    const bytes = new Uint8Array(await file.arrayBuffer());

    state.textContent = `parsing ${file.name}…`;
    await new Promise(r => setTimeout(r, 0));   // let the message paint

    const info = JSON.parse(window.ocad.LoadMap(file.name, bytes));
    if (!info.ok) {
        state.textContent = info.error;
        return;
    }

    mapBounds = { minX: info.minX, minY: info.minY, maxX: info.maxX, maxY: info.maxY };
    lastLoad = { name: file.name, bytes: bytes.length, version: info.version, parseMs: info.parseMs };

    const bounds = L.latLngBounds(
        L.latLng(mapBounds.minY, mapBounds.minX),
        L.latLng(mapBounds.maxY, mapBounds.maxX));

    for (const layer of layers) layer.remove();
    layers = [];

    // Settle the view *before* attaching layers. fitBounds on one map makes
    // leaflet.sync move the other, which moves the first again; if tiles are
    // queued during that churn, Leaflet prunes them before the renderer reaches
    // them and almost every tile is discarded as stale. At ~700 ms a tile, that
    // is the difference between a map and a blank page.
    for (const map of [mapLeft, mapRight])
        map.fitBounds(bounds, { animate: false });
    await new Promise(r => setTimeout(r, 50));

    resetStats();
    queueStartedAt = performance.now();

    for (const map of [mapLeft, mapRight]) {
        const layer = new OcadTileLayer({ tileSize: TILE_SIZE, noWrap: true });
        layer.addTo(map);
        layers.push(layer);
    }

    state.textContent = `loaded ${file.name}`;
}

/* --------------------------------------------------------------- benchmark */

function benchmark() {
    resetStats();
    queueStartedAt = performance.now();
    for (const layer of layers) layer.redraw();
}

/* ------------------------------------------------------------------- wiring */

document.getElementById('file').addEventListener('change', e => {
    if (e.target.files.length) loadFile(e.target.files[0]);
});
document.getElementById('bench').addEventListener('click', benchmark);

window.addEventListener('ocad-ready', e => {
    document.getElementById('file').disabled = false;
    document.getElementById('bench').disabled = false;
    document.getElementById('state').textContent =
        `.NET ready, ${e.detail.faces} font faces registered`;
});
