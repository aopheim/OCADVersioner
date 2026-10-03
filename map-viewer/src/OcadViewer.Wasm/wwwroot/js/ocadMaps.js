/* Two synced Leaflet maps whose tiles are drawn by the .NET renderer.
 *
 * Maps are drawn in OCAD paper millimetres, so Leaflet uses L.CRS.Simple and paper
 * coordinates are Leaflet coordinates directly: lng = paper X, lat = paper Y. Tile
 * world rects come from map.unproject, which keeps the maths to four lines and lets
 * leaflet.sync work without knowing anything about the layer.
 */

import { getTileInterop } from './ocadRuntime.js';

const TILE_SIZE = 256;

function freshStats() {
    return { drawn: 0, stale: 0, outside: 0, cancelled: 0, detached: 0, totalMs: 0, maxMs: 0 };
}

/* ---------------------------------------------------------------- scheduler */

/* WebAssembly is single threaded, so a tile render blocks everything - including
 * Leaflet's own animations. One tile per frame lets the browser paint what is
 * already done and keeps input alive between tiles.
 *
 * The pump deliberately does NOT rely on requestAnimationFrame alone. When the page
 * is hidden rAF stops firing, and a naive implementation latches "pumping" and then
 * deadlocks permanently, even after the page is shown again. The spike hit exactly
 * that. A timer fallback plus a watchdog is the fix. */
const queue = [];

/* One shared counter set. Per-layer counters looked tidier but reset every time a
 * slot was reloaded, and orphaned layers swallowed their own numbers - which hid a
 * bug where every tile was taking the detached path. */
const stats = freshStats();
let pumping = false;
let lastPumpAt = 0;
let watchdog = null;

function schedule() {
    if (!pumping) {
        pumping = true;
        nextTick();
    }
    startWatchdog();
}

let tickPending = false;

function nextTick() {
    if (tickPending) return;
    tickPending = true;
    lastPumpAt = performance.now();

    // Arm both: rAF keeps us in step with painting when the page is visible, the
    // timer keeps the queue moving when it is not. Whichever fires first clears
    // the flag and does the work; the other finds nothing to do.
    const run = () => {
        if (!tickPending) return;
        tickPending = false;
        pump();
    };
    requestAnimationFrame(run);
    setTimeout(run, 250);
}

function pump() {
    const job = queue.shift();
    if (!job) {
        pumping = false;
        stopWatchdog();
        return;
    }
    try {
        renderTile(job);
    } catch (err) {
        console.error('[ocad] tile render failed', err);
        job.done(err, job.tile);
    }
    nextTick();
}

/* If the page is hidden mid-queue the pump can stall. Restart it when the page
 * comes back, and never let the latch stay set with work outstanding. */
function startWatchdog() {
    watchdog ??= setInterval(() => {
        if (!queue.length) { stopWatchdog(); return; }
        if (performance.now() - lastPumpAt > 2000) {
            pumping = false;
            schedule();
        }
    }, 1000);
}

function stopWatchdog() {
    if (watchdog !== null) { clearInterval(watchdog); watchdog = null; }
}

document.addEventListener('visibilitychange', () => {
    if (!document.hidden && queue.length) { pumping = false; schedule(); }
});

/* ------------------------------------------------------------------ rendering */

function renderTile(job) {
    const { layer, coords, size, tile, done } = job;
    const map = layer._map;

    // The layer was detached while this job sat in the queue - which happens every
    // time a slot is reloaded. Counted, because silently returning here once hid a
    // bug where every single tile took this path.
    if (!map || !layer._ocadBounds) { stats.detached++; done(null, tile); return; }

    /* Leaflet prunes tiles as soon as the view moves past them, but a job already
     * queued would still be rendered - at hundreds of milliseconds each, that is
     * the difference between responsive and unusable. Drop anything pruned.
     *
     * Only check _tiles. Also testing tile.isConnected discards everything, because
     * the element is not in the document yet at this point. */
    const key = layer._tileCoordsToKey(coords);
    if (!layer._tiles[key]) { stats.stale++; done(null, tile); return; }

    const nwPoint = coords.scaleBy(size);
    const sePoint = nwPoint.add(size);
    const nw = map.unproject(nwPoint, coords.z);
    const se = map.unproject(sePoint, coords.z);

    const minX = nw.lng, maxX = se.lng;
    const minY = se.lat, maxY = nw.lat;        // lat decreases downwards
    const b = layer._ocadBounds;

    // Same bounding-box idea the renderer uses internally, one level up.
    if (maxX < b.minX || minX > b.maxX || maxY < b.minY || minY > b.maxY) {
        stats.outside++;
        done(null, tile);
        return;
    }

    const bytes = layer._interop.RenderTile(layer._slot, minX, minY, maxX - minX, maxY - minY, size.x);
    const ms = layer._interop.GetLastRenderMs();

    if (bytes) {
        stats.drawn++;
        stats.totalMs += ms;
        stats.maxMs = Math.max(stats.maxMs, ms);
        tile.getContext('2d').putImageData(
            new ImageData(new Uint8ClampedArray(bytes), size.x, size.y), 0, 0);
    } else {
        stats.cancelled++;
    }
    done(null, tile);
}

/* ---------------------------------------------------------------- the layer */

const OcadTileLayer = L.GridLayer.extend({
    initialize(slot, interop, options) {
        L.GridLayer.prototype.initialize.call(this, options);
        this._slot = slot;
        this._interop = interop;
        this._ocadBounds = null;
    },

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

/* ------------------------------------------------------------------- public */

/**
 * Creates two synced maps over the given elements.
 * @returns a handle with loadInto, fit, stats and dispose.
 */
export async function createOcadMaps(leftEl, rightEl) {
    const interop = await getTileInterop();

    const makeMap = el => L.map(el, {
        crs: L.CRS.Simple,
        minZoom: -6,
        maxZoom: 4,
        zoomSnap: 0,
        attributionControl: false,

        /* Not cosmetic. Leaflet fades tiles in from a requestAnimationFrame loop,
         * and a 200-1000 ms blocking render starves it, so the fade never finishes
         * and tiles sit at partial opacity. The symptom is a washed-out map whose
         * tile canvases contain perfectly correct pixels. */
        fadeAnimation: false
    });

    const maps = [makeMap(leftEl), makeMap(rightEl)];
    // Leaflet refuses to sync maps with no view; a load replaces this via fitBounds.
    maps.forEach(m => m.setView([0, 0], 0));
    maps[0].sync(maps[1]);
    maps[1].sync(maps[0]);

    /* Leaflet measures its container once, at construction. Inside a component the
     * element is often still 0x0 at that point, and a map that believes it is zero
     * pixels wide decides no tiles are in view and silently draws nothing. Watch for
     * the real size and tell Leaflet when it changes. */
    const resizeObserver = new ResizeObserver(() => {
        for (const m of maps)
            if (m.getContainer().clientWidth > 0) m.invalidateSize({ animate: false });
    });
    maps.forEach(m => resizeObserver.observe(m.getContainer()));

    const layers = [null, null];
    let fitted = false;

    return {
        /** Parses a file into a slot and shows it on that side. */
        async loadInto(slot, fileName, bytes) {
            const info = JSON.parse(interop.LoadMap(slot, fileName, bytes));
            if (!info.ok) throw new Error(info.error);

            const bounds = L.latLngBounds(
                L.latLng(info.minY, info.minX),
                L.latLng(info.maxY, info.maxX));

            // Drop any queued work for the layer being replaced, rather than letting
            // the pump grind through tiles for a map that is no longer shown.
            const outgoing = layers[slot];
            if (outgoing) {
                for (let i = queue.length - 1; i >= 0; i--)
                    if (queue[i].layer === outgoing) queue.splice(i, 1);
                outgoing.remove();
            }

            /* Fit only on the first load, and only on one map - leaflet.sync mirrors
             * it to the other. Re-fitting on every load fights the user's own zoom,
             * and fitting both maps makes sync bounce the view back and forth, which
             * prunes tiles mid-flight. The spike saw 113 stale against 1 drawn before
             * the view was allowed to settle. */
            if (!fitted) {
                // Make sure Leaflet has the real container size before fitting.
                maps.forEach(m => m.invalidateSize({ animate: false }));
                maps[slot].fitBounds(bounds, { animate: false });
                fitted = true;
                await new Promise(r => setTimeout(r, 50));
            }

            const layer = new OcadTileLayer(slot, interop, { tileSize: TILE_SIZE, noWrap: true });
            layer._ocadBounds = { minX: info.minX, minY: info.minY, maxX: info.maxX, maxY: info.maxY };
            layer.addTo(maps[slot]);
            layers[slot] = layer;

            return { ...info, georeference: interop.Georeference(slot) };
        },

        /** Abandons any in-flight render for a slot. */
        cancel(slot) { interop.CancelRender(slot); },

        stats() {
            return {
                ...stats,
                queued: queue.length,
                avgMs: stats.drawn ? +(stats.totalMs / stats.drawn).toFixed(1) : 0
            };
        },

        resetStats() { Object.assign(stats, freshStats()); },

        /** Re-fits the view to the loaded map. */
        fit(slot = 0) {
            const layer = layers[slot];
            if (!layer) return;
            const b = layer._ocadBounds;
            maps[slot].fitBounds(
                L.latLngBounds(L.latLng(b.minY, b.minX), L.latLng(b.maxY, b.maxX)),
                { animate: false });
        },

        dispose() {
            queue.length = 0;
            resizeObserver.disconnect();
            stopWatchdog();
            layers.forEach((l, i) => { l?.remove(); interop.UnloadMap(i); });
            maps.forEach(m => m.remove());
        }
    };
}
