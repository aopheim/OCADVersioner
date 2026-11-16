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
 * A paper-millimetre rectangle as Leaflet bounds.
 *
 * Paper coordinates are Leaflet coordinates under L.CRS.Simple - lng is X and
 * lat is Y - and everything above this module speaks {minX, minY, maxX, maxY}.
 * This is the one place that conversion happens.
 */
function latLngBounds(area) {
    return L.latLngBounds(L.latLng(area.minY, area.minX), L.latLng(area.maxY, area.maxX));
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

    /* One shared counter set per viewport rather than per layer: a layer is
     * replaced every time a side is reloaded, and its numbers would go with it. */
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
     * map silently refuses to zoom.
     *
     * "Changed" is measured against the size Leaflet believes, not against the
     * last size this observer saw. A pane can be hidden, and anything that
     * invalidates a hidden map - a show(), a window resize - leaves Leaflet
     * believing it is 0x0. When the pane reappears at its old size, a cache of
     * our own would call that "no change" and the map would draw nothing. */
    const resizeObserver = new ResizeObserver(entries => {
        for (const entry of entries) {
            const el = entry.target;
            const w = el.clientWidth, h = el.clientHeight;
            if (w === 0 || h === 0) continue;

            const side = SIDES.find(s => maps[s].getContainer() === el);
            if (!side) continue;

            const map = maps[side];
            const believed = map.getSize();
            if (believed.x === w && believed.y === h) continue;

            const wasHidden = believed.x === 0 || believed.y === 0;
            map.invalidateSize({ animate: false, pan: false });

            /* A hidden map is still synced, but leaflet.sync places it using its
             * 0x0 size, and invalidateSize({pan: false}) keeps that wrong pixel
             * origin - so it comes back looking at a different place from its
             * partner. Put it back where the visible map is. */
            if (wasHidden) {
                const partner = maps[SIDES.find(s => s !== side)];
                const partnerSize = partner.getSize();
                if (partnerSize.x > 0 && partnerSize.y > 0)
                    map.setView(partner.getCenter(), partner.getZoom(), { animate: false });
            }
        }
    });
    for (const side of SIDES) resizeObserver.observe(maps[side].getContainer());

    const layers = { left: null, right: null };
    const overlays = { left: null, right: null };

    /* Whoever is waiting for a pane to finish drawing. A show resolves when the
     * tiles it asked for are all through the pump - or, if the pane is handed to
     * another map first, when that happens, because the tiles it was waiting for
     * are dropped from the queue and will never report back. */
    const pending = { left: null, right: null };

    let fitted = false;

    function paneFor(side) {
        if (!SIDES.includes(side))
            throw new Error(`Unknown side "${side}".`);

        return maps[side];
    }

    function settle(side) {
        const resolve = pending[side];
        pending[side] = null;
        if (resolve) resolve();
    }

    /* ------------------------------------------------------------- rendering */

    function drawTile(job) {
        const { layer, coords, size, tile } = job;
        const map = layer._map;

        // The layer was detached while this job sat in the queue - which happens
        // every time a side is reloaded. Counted rather than silent, so that
        // every tile taking this path shows up in the stats as a bug.
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

        /* Measured here rather than asked of .NET afterwards: wall time around the
         * boundary includes marshalling, and needs no state on the other side. */
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

            /* Counted here rather than asked of Leaflet afterwards. Leaflet's own
             * loading flag says whether anything is outstanding, not how much,
             * and a progress bar needs the second number. */
            this._ocadAsked = 0;
            this._ocadDone = 0;
            this._ocadReport = null;
        },

        createTile(coords, done) {
            const size = this.getTileSize();
            const tile = document.createElement('canvas');
            tile.width = size.x;
            tile.height = size.y;

            this._ocadAsked++;

            // Counted once per tile, however it ends.
            let counted = false;
            const finished = (error, element) => {
                if (counted) return;
                counted = true;
                this._ocadDone++;
                this._ocadReport?.();
                done(error, element);
            };

            /* Every path through drawTile ends in done(), including the ones that
             * discard the tile - except a throw. The pump catches that and moves
             * on, and a tile that never reports leaves whoever waits for the pane
             * waiting for ever, with the whole page busy. So a failed tile counts
             * as finished, and is rethrown for the pump to log. */
            pump.enqueue(() => {
                try {
                    drawTile({ tile, coords, size, done: finished, layer: this });
                } catch (err) {
                    finished(err, tile);
                    throw err;
                }
            }, this);
            return tile;
        }
    });

    /* --------------------------------------------------------------- public */

    const handle = {
        /**
         * Roughly how many tiles a pane will ask for.
         *
         * Leaflet decides the real number when the layer is attached, which is
         * too late to weigh a load that has not started. The grid covers the
         * pane plus a tile of overhang, and that is close enough to say what
         * share of the wait the drawing is.
         */
        expectedTiles(side) {
            const element = paneFor(side).getContainer();
            return (Math.ceil(element.clientWidth / TILE_SIZE) + 1)
                 * (Math.ceil(element.clientHeight / TILE_SIZE) + 1);
        },

        /**
         * Shows the named map on one side, and waits for it to be on screen.
         *
         * Attaching the layer is instant; the tiles behind it are rendered one
         * per frame afterwards, which is most of the time a mapper waits and all
         * of the time anything is visibly happening. So this resolves when the
         * pane has drawn what it asked for, not when Leaflet has been told what
         * to draw.
         *
         * @param side 'left' or 'right'
         * @param mapKey the name the tile source holds it under
         * @param bounds {minX, minY, maxX, maxY} in paper millimetres
         * @param onTile called as tiles land, with (done, asked for)
         * @returns {Promise<void>} resolved when the pane is drawn, or when the
         *          pane is handed to another map and these tiles are abandoned
         */
        show(side, mapKey, bounds, onTile) {
            const map = paneFor(side);

            // Drop queued work for the layer being replaced, rather than letting
            // the pump grind through tiles for a map that is no longer shown.
            const outgoing = layers[side];
            if (outgoing) {
                pump.remove(tag => tag === outgoing);
                outgoing._ocadReport = null;
                outgoing.remove();
            }

            // Those dropped tiles are what somebody was waiting for.
            settle(side);

            /* Always re-measure before adding a layer. The ResizeObserver catches
             * later changes, but the first show often happens before the browser
             * has laid the component out, and a map that still believes it is 0x0
             * decides no tiles are in view and draws nothing at all - silently,
             * with no error and a perfectly healthy-looking layer. */
            for (const s of SIDES) maps[s].invalidateSize({ animate: false, pan: false });

            /* Fit only on the first show, and only on one map - leaflet.sync
             * mirrors it to the other. Re-fitting on every show fights the user's
             * own zoom, and fitting both maps makes sync bounce the view back and
             * forth, which prunes tiles mid-flight - measured at 113 stale tiles
             * for every one drawn. */
            if (!fitted) {
                map.fitBounds(latLngBounds(bounds), { animate: false });
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
            layers[side] = layer;

            return new Promise(resolve => {
                pending[side] = resolve;

                layer._ocadReport = () => {
                    if (onTile) onTile(layer._ocadDone, layer._ocadAsked);

                    if (layer._ocadDone >= layer._ocadAsked) {
                        layer._ocadReport = null;
                        settle(side);
                    }
                };

                layer.addTo(map);

                /* Leaflet creates the tiles for the current view inside addTo, so
                 * the count is final by this line - and reported now, so the bar
                 * learns the real number before the first tile lands. A pane that
                 * asked for none - laid out at zero height, or scrolled off the
                 * map entirely - is settled by the same check: nothing to wait for. */
                layer._ocadReport();
            });
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
            const map = paneFor(side);
            clearOverlay(side);

            overlays[side] = L.imageOverlay(
                url, latLngBounds(bounds),
                { interactive: false, className: 'changeMask' }
            ).addTo(map);
        },

        /**
         * Moves the view to frame a rectangle of paper.
         *
         * Only one map is told; leaflet.sync mirrors it to the other, and
         * fitting both would make them argue about the result.
         */
        fitTo(area) {
            maps[SIDES[0]].fitBounds(latLngBounds(area), { animate: false });
        },

        clearOverlays() {
            for (const side of SIDES) clearOverlay(side);
        },

        /**
         * Lets the next show fit the view again, as the very first one did.
         *
         * For a different project: its maps can sit anywhere on the paper, so
         * keeping the last project's zoom and centre could leave both panes
         * looking at empty paper.
         */
        refit() {
            fitted = false;
        },

        stats,

        dispose() {
            pump.dispose();
            resizeObserver.disconnect();
            for (const side of SIDES) {
                // Anything still waiting for this pane never will be.
                settle(side);
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
