/* Two synced Leaflet maps whose tiles come from somewhere else.
 *
 * Maps are drawn in OCAD paper millimetres, so Leaflet uses L.CRS.Simple and paper
 * coordinates are Leaflet coordinates directly: lng = paper X, lat = paper Y. Tile
 * world rects come from map.unproject, which keeps the maths to four lines and lets
 * leaflet.sync work without knowing anything about the layer.
 *
 * This module knows about Leaflet and nothing else. The tile renderer and the
 * scheduler are passed in; it does not import the .NET runtime, read files, or
 * know what a project version is. Composition happens in mapWorkspace.js.
 */

import { createTilePump } from './tilePump.js';

const TILE_SIZE = 256;

// Shared by the maps and the tile layers - see the note where the layer is built.
const MIN_ZOOM = -6;
const MAX_ZOOM = 4;

/** Which pane. The names are the contract with the C# MapSide enum. */
export const SIDES = ['left', 'right'];

function freshStats() {
    return { drawn: 0, stale: 0, outside: 0, noMap: 0, detached: 0, totalMs: 0, maxMs: 0 };
}

/**
 * Creates two synced maps over the given elements.
 *
 * @param leftEl  element for the left map
 * @param rightEl element for the right map
 * @param renderTile (key, minX, minY, width, height, pixels) => Uint8Array|null
 * @returns a handle with show, stats and dispose.
 */
export function createSyncedMaps(leftEl, rightEl, renderTile) {
    const pump = createTilePump();

    /* One shared counter set per viewport. Per-layer counters looked tidier but
     * reset every time a side was reloaded, and orphaned layers swallowed their
     * own numbers - which hid a bug where every tile took the detached path. */
    const stats = freshStats();

    const makeMap = el => L.map(el, {
        crs: L.CRS.Simple,
        minZoom: MIN_ZOOM,
        maxZoom: MAX_ZOOM,
        zoomSnap: 0,
        attributionControl: false,

        /* Not cosmetic. Leaflet fades tiles in from a requestAnimationFrame loop,
         * and a 200-1000 ms blocking render starves it, so the fade never finishes
         * and tiles sit at partial opacity. The symptom is a washed-out map whose
         * tile canvases contain perfectly correct pixels. */
        fadeAnimation: false
    });

    const maps = { left: makeMap(leftEl), right: makeMap(rightEl) };

    // Leaflet refuses to sync maps with no view; the first show replaces this.
    for (const side of SIDES) maps[side].setView([0, 0], 0);
    maps.left.sync(maps.right);
    maps.right.sync(maps.left);

    /* Leaflet measures its container once, at construction. Inside a component the
     * element is often still 0x0 at that point, and a map that believes it is zero
     * pixels wide decides no tiles are in view and silently draws nothing. Watch for
     * the real size and tell Leaflet when it changes.
     *
     * Only react to an actual size change. Calling invalidateSize unconditionally
     * re-enters the observer - invalidateSize touches layout, which fires the
     * observer, which calls it again - and the resulting loop pins the view so the
     * map silently refuses to zoom. */
    const lastSize = new WeakMap();
    const resizeObserver = new ResizeObserver(entries => {
        for (const entry of entries) {
            const el = entry.target;
            const w = el.clientWidth, h = el.clientHeight;
            if (w === 0 || h === 0) continue;

            const previous = lastSize.get(el);
            if (previous && previous.w === w && previous.h === h) continue;
            lastSize.set(el, { w, h });

            for (const side of SIDES) {
                if (maps[side].getContainer() === el)
                    maps[side].invalidateSize({ animate: false, pan: false });
            }
        }
    });
    for (const side of SIDES) resizeObserver.observe(maps[side].getContainer());

    const layers = { left: null, right: null };
    const overlays = { left: null, right: null };
    let fitted = false;

    /* ------------------------------------------------------------- rendering */

    function drawTile(job) {
        const { layer, coords, size, tile } = job;
        const map = layer._map;

        // The layer was detached while this job sat in the queue - which happens
        // every time a side is reloaded. Counted, because silently returning here
        // once hid a bug where every single tile took this path.
        if (!map || !layer._ocadBounds) { stats.detached++; job.done(null, tile); return; }

        /* Leaflet prunes tiles as soon as the view moves past them, but a job
         * already queued would still be rendered - at hundreds of milliseconds
         * each, that is the difference between responsive and unusable. Drop
         * anything pruned.
         *
         * Only check _tiles. Also testing tile.isConnected discards everything,
         * because the element is not in the document yet at this point. */
        const key = layer._tileCoordsToKey(coords);
        if (!layer._tiles[key]) { stats.stale++; job.done(null, tile); return; }

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
            job.done(null, tile);
            return;
        }

        /* Measured here rather than asked of .NET afterwards. The renderer used to
         * expose the stopwatch it kept inside the render call, which excluded
         * marshalling and left the number sitting in a static that only meant
         * anything until the next call. Wall time around the boundary is both
         * honest and stateless. */
        const started = performance.now();
        const bytes = renderTile(layer._ocadKey, minX, minY, maxX - minX, maxY - minY, size.x);
        const ms = performance.now() - started;

        if (bytes) {
            stats.drawn++;
            stats.totalMs += ms;
            stats.maxMs = Math.max(stats.maxMs, ms);
            tile.getContext('2d').putImageData(
                new ImageData(new Uint8ClampedArray(bytes), size.x, size.y), 0, 0);
        } else {
            // Only happens when the map behind this layer is no longer held.
            stats.noMap++;
        }
        job.done(null, tile);
    }

    /* ---------------------------------------------------------------- layer */

    const OcadTileLayer = L.GridLayer.extend({
        initialize(mapKey, options) {
            L.GridLayer.prototype.initialize.call(this, options);
            this._ocadKey = mapKey;
            this._ocadBounds = null;
        },

        createTile(coords, done) {
            const size = this.getTileSize();
            const tile = document.createElement('canvas');
            tile.width = size.x;
            tile.height = size.y;
            pump.enqueue(() => drawTile({ tile, coords, size, done, layer: this }), this);
            return tile;
        }
    });

    /* --------------------------------------------------------------- public */

    const handle = {
        /**
         * Shows the named map on one side.
         * @param side 'left' or 'right'
         * @param mapKey the name the tile source holds it under
         * @param bounds {minX, minY, maxX, maxY} in paper millimetres
         */
        show(side, mapKey, bounds) {
            if (!SIDES.includes(side))
                throw new Error(`Unknown side "${side}".`);

            // Drop queued work for the layer being replaced, rather than letting
            // the pump grind through tiles for a map that is no longer shown.
            const outgoing = layers[side];
            if (outgoing) {
                pump.remove(tag => tag === outgoing);
                outgoing.remove();
            }

            /* Always re-measure before adding a layer. The ResizeObserver catches
             * later changes, but the first show often happens before the browser
             * has laid the component out, and a map that still believes it is 0x0
             * decides no tiles are in view and draws nothing at all - silently,
             * with no error and a perfectly healthy-looking layer. */
            for (const s of SIDES) maps[s].invalidateSize({ animate: false, pan: false });

            /* Fit only on the first show, and only on one map - leaflet.sync
             * mirrors it to the other. Re-fitting on every show fights the user's
             * own zoom, and fitting both maps makes sync bounce the view back and
             * forth, which prunes tiles mid-flight. The spike saw 113 stale
             * against 1 drawn before the view was allowed to settle. */
            if (!fitted) {
                maps[side].fitBounds(
                    L.latLngBounds(L.latLng(bounds.minY, bounds.minX),
                                   L.latLng(bounds.maxY, bounds.maxX)),
                    { animate: false });
                fitted = true;
            }

            /* minZoom/maxZoom must be repeated here. GridLayer keeps its *own* zoom
             * limits, defaulting to minZoom 0, and when the map's zoom rounds below
             * them Leaflet sets _tileZoom to undefined and creates no tiles at all -
             * no error, no warning, an attached layer and an empty map. A large map
             * fits at a negative zoom, so this bites exactly on the files that
             * matter most: 628 mm fitted into a 314 px pane is zoom -1. */
            const layer = new OcadTileLayer(mapKey, {
                tileSize: TILE_SIZE,
                noWrap: true,
                minZoom: MIN_ZOOM,
                maxZoom: MAX_ZOOM
            });
            layer._ocadBounds = { ...bounds };
            layer.addTo(maps[side]);
            layers[side] = layer;
        },

        /** Which map each side is showing, by name. */
        showing() {
            return { left: layers.left?._ocadKey ?? null, right: layers.right?._ocadKey ?? null };
        },

        /**
         * Puts an image over the map on one side, replacing any previous one.
         *
         * Used for the change mask. It sits above the tiles and below Leaflet's
         * controls, and panning and zooming move it with the map for free -
         * which is the whole reason the mask is computed once over the map's own
         * rectangle rather than per tile.
         *
         * @param url an object URL; the caller keeps ownership and revokes it
         * @param bounds {minX, minY, maxX, maxY} in paper millimetres
         */
        setOverlay(side, url, bounds) {
            if (!SIDES.includes(side))
                throw new Error(`Unknown side "${side}".`);

            clearOverlay(side);

            overlays[side] = L.imageOverlay(
                url,
                L.latLngBounds(L.latLng(bounds.minY, bounds.minX),
                               L.latLng(bounds.maxY, bounds.maxX)),
                { interactive: false, className: 'changeMask' }
            ).addTo(maps[side]);
        },

        clearOverlays() {
            for (const side of SIDES) clearOverlay(side);
        },

        stats,

        dispose() {
            pump.dispose();
            resizeObserver.disconnect();
            for (const side of SIDES) {
                clearOverlay(side);
                layers[side]?.remove();
                maps[side].remove();
            }
        }
    };

    function clearOverlay(side) {
        overlays[side]?.remove();
        overlays[side] = null;
    }

    // Debugging handle. The maps are otherwise unreachable from the console, and
    // "no tiles appeared" is almost always a view or sizing question. The probes
    // in PLAN.md use it.
    globalThis.__ocad = { maps, layers, stats, pump };

    return handle;
}
