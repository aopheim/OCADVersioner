/* Two synced Leaflet maps whose tiles come from somewhere else.
 *
 * Both panes show the ground, in Web Mercator - Leaflet's own CRS, and the grid
 * of the web maps laid underneath - so two maps, whatever their scale, rotation
 * or place on their paper, are drawn where they lie on the ground, over the same
 * background, at the same zoom. leaflet.sync keeps the panes on the same view
 * without knowing anything about the layers.
 *
 * Maps that cannot be put on the ground - one of the pair has no georeference -
 * are shown on their paper instead, one millimetre to one Web Mercator metre, and
 * without a background. Which it is, where a map lies and every pixel of every
 * tile are worked out in .NET; this module only asks, and draws what it is given.
 *
 * This module knows about Leaflet and nothing else. The tile source is passed in;
 * it does not import the .NET runtime, read files, or know what a project version
 * is. Composition happens in mapWorkspace.js.
 */

import { createTilePump } from './tilePump.js';
import { createBackground } from './backgroundMap.js';

const TILE_SIZE = 256;

/* Shared by the maps and the tile layers - see the note where the layer is built.
 * 24 is about 1 cm per pixel at Trondheim: far past any web map, which is then
 * stretched, and enough for a 1:4000 map at 16 pixels to the millimetre - or for
 * a map on its paper, at 1 mm to the metre. */
const MIN_ZOOM = 0;
const MAX_ZOOM = 24;

/** The background picked until the mapper picks another. */
const DEFAULT_BACKGROUND = 'osm';

/** Which pane. The names are the contract with the C# MapSide enum. */
export const SIDES = ['left', 'right'];

function freshStats() {
    return { drawn: 0, stale: 0, outside: 0, noMap: 0, detached: 0, totalMs: 0, maxMs: 0 };
}

/** A rectangle in Web Mercator metres, {minX, minY, maxX, maxY}, as Leaflet bounds. */
function latLngBounds(area) {
    const crs = L.CRS.EPSG3857;
    return L.latLngBounds(crs.unproject(L.point(area.minX, area.minY)),
                          crs.unproject(L.point(area.maxX, area.maxY)));
}

/**
 * The square of ground one tile covers, as .NET wants it: its top-left corner
 * in Web Mercator metres and the metres each pixel spans.
 */
function canvasOf(map, coords, size) {
    const crs = map.options.crs;
    const nw = crs.project(map.unproject(coords.scaleBy(size), coords.z));
    const se = crs.project(map.unproject(coords.scaleBy(size).add(size), coords.z));
    return { minX: nw.x, maxY: nw.y, unitsPerPixel: (se.x - nw.x) / size.x, maxX: se.x, minY: se.y };
}

/**
 * Creates two synced maps over the given elements.
 *
 * @param leftEl  element for the left map
 * @param rightEl element for the right map
 * @param source what .NET answers, as plain functions:
 *        render(key, onGround, minX, maxY, unitsPerPixel, pixels) => RGBA, empty
 *          where the map is not, or null when it is no longer held;
 *        canShowOnGround(keys) => whether these maps can all go on the ground;
 *        extent(keys, onGround) => {minX, minY, maxX, maxY} they cover, or null;
 *        background: {sources, tiles}, the web maps on offer and
 *          (sourceId, minX, maxY, metresPerPixel, pixels) => [{url, size, transform}]
 * @returns a handle with show, stats and dispose.
 */
export function createSyncedMaps(leftEl, rightEl, source) {
    const pump = createTilePump();

    /* One shared counter set per viewport rather than per layer: a layer is
     * replaced every time a side is reloaded, and its numbers would go with it. */
    const stats = freshStats();

    const makeMap = el => L.map(el, {
        crs: L.CRS.EPSG3857,
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

    /* Whether the panes show the ground or the maps' paper. Decided by the pair
     * on show: both georeferenced, the ground; otherwise both on paper. */
    let onGround = false;

    /* The web map under each pane. The choice is shared - both panes lie on the
     * same ground, so the same map lies under both - while how strongly each
     * pane's OCAD map is drawn over it is the pane's own. Kept here rather than on
     * the layer because a new layer replaces the old one on every show, and the
     * mapper's choice should survive that. */
    const backgrounds = {};
    const opacities = { left: 1, right: 1 };
    const allSources = source.background.sources.map(s => s.id);
    for (const side of SIDES) {
        backgrounds[side] = createBackground(maps[side], {
            minZoom: MIN_ZOOM,
            maxZoom: MAX_ZOOM,
            sources: source.background.sources,
            initialSource: DEFAULT_BACKGROUND,
            onOpacity: value => {
                opacities[side] = value;
                layers[side]?.setOpacity(value);
            },
            onChoose: id => {
                for (const other of SIDES)
                    if (other !== side) backgrounds[other].select(id);
            }
        });
    }

    /** Tells both backgrounds whether there is ground to lay a web map on. */
    function offerBackgrounds() {
        for (const side of SIDES)
            backgrounds[side].setMap(onGround ? allSources : null, source.background.tiles);
    }

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

    /* How much of the view has been fitted since the last refit: nothing, one
     * map, or both. The second map's first arrival fits again, to the two of
     * them; after that a show never moves the view - it would fight the
     * mapper's own pan and zoom. */
    let fittedMaps = 0;

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
        if (!map) { stats.detached++; job.done(null, tile); return; }

        /* Leaflet prunes tiles as soon as the view moves past them, but a job
         * already queued would still be rendered - at hundreds of milliseconds
         * each, that is the difference between responsive and unusable. Drop
         * anything pruned.
         *
         * Only check _tiles. Also testing tile.isConnected discards everything,
         * because the element is not in the document yet at this point. */
        const key = layer._tileCoordsToKey(coords);
        if (!layer._tiles[key]) { stats.stale++; job.done(null, tile); return; }

        const canvas = canvasOf(map, coords, size);
        const e = layer._ocadExtent;

        // Skipped without asking .NET when the map cannot reach this tile at all.
        if (!e || canvas.maxX < e.minX || canvas.minX > e.maxX || canvas.maxY < e.minY || canvas.minY > e.maxY) {
            stats.outside++;
            job.done(null, tile);
            return;
        }

        /* Measured here rather than asked of .NET afterwards: wall time around the
         * boundary includes marshalling, and needs no state on the other side. */
        const started = performance.now();
        const bytes = source.render(layer._ocadKey, onGround, canvas.minX, canvas.maxY, canvas.unitsPerPixel, size.x);
        const ms = performance.now() - started;

        if (!bytes) {
            // Only happens when the map behind this layer is no longer held.
            stats.noMap++;
        } else if (bytes.length === 0) {
            // Inside the map's rectangle on the ground, but past its turned paper.
            stats.outside++;
        } else {
            stats.drawn++;
            stats.totalMs += ms;
            stats.maxMs = Math.max(stats.maxMs, ms);
            /* Transparent past the paper's edge, so a background under a faded
             * map shows there rather than tile-shaped blocks of white. */
            tile.getContext('2d').putImageData(new ImageData(new Uint8ClampedArray(bytes), size.x, size.y), 0, 0);
        }
        job.done(null, tile);
    }

    /* ---------------------------------------------------------------- layer */

    const OcadTileLayer = L.GridLayer.extend({
        initialize(mapKey, options) {
            L.GridLayer.prototype.initialize.call(this, options);
            this._ocadKey = mapKey;
            this._ocadExtent = null;

            /* How many tiles this layer has asked for and how many have finished,
             * so that show() can say when the pane is drawn. */
            this._ocadAsked = 0;
            this._ocadDone = 0;
            this._ocadCheck = null;
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
                this._ocadCheck?.();
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

    /* A change mask over one pane: an image drawn on the newer map's paper, put
     * in place on each canvas by a transform .NET works out from that map's
     * placement - turned and scaled with the map, wherever it lies. */
    const MaskLayer = L.GridLayer.extend({
        initialize(image, placeOn, options) {
            L.GridLayer.prototype.initialize.call(this, options);
            this._image = image;
            this._placeOn = placeOn;
        },

        createTile(coords, done) {
            const size = this.getTileSize();
            const tile = document.createElement('canvas');
            tile.width = size.x;
            tile.height = size.y;

            const canvas = canvasOf(this._map, coords, size);
            const transform = this._placeOn(onGround, canvas.minX, canvas.maxY, canvas.unitsPerPixel, size.x);
            this._image.then(img => {
                if (img && transform) {
                    const ctx = tile.getContext('2d');
                    // Each mask pixel is a cell of change, not a photograph: kept sharp.
                    ctx.imageSmoothingEnabled = false;
                    ctx.setTransform(...transform);
                    ctx.drawImage(img, 0, 0);
                }
                done(null, tile);
            });
            return tile;
        }
    });

    /* --------------------------------------------------------------- public */

    /** The view a set of maps covers, in the coordinates now shown. */
    function extentOf(keys) {
        return keys.length ? source.extent(keys, onGround) : null;
    }

    const handle = {
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
         * @returns {Promise<void>} resolved when the pane is drawn, or when the
         *          pane is handed to another map and these tiles are abandoned
         */
        show(side, mapKey) {
            const map = paneFor(side);
            const other = SIDES.find(s => s !== side);

            // Drop queued work for the layer being replaced, rather than letting
            // the pump grind through tiles for a map that is no longer shown.
            const outgoing = layers[side];
            if (outgoing) {
                pump.remove(tag => tag === outgoing);
                outgoing._ocadCheck = null;
                outgoing.remove();
            }
            layers[side] = null;

            // Those dropped tiles are what somebody was waiting for.
            settle(side);

            /* Ground or paper is the pair's to decide. When it changes, the other
             * pane's map moves to the other coordinates too: its tiles are drawn
             * again, and the view is fitted afresh - the old one is somewhere else
             * entirely now. */
            const keys = [mapKey, layers[other]?._ocadKey].filter(Boolean);
            const ground = source.canShowOnGround(keys);
            if (ground !== onGround) {
                onGround = ground;
                fittedMaps = 0;
                if (layers[other]) {
                    layers[other]._ocadExtent = extentOf([layers[other]._ocadKey]);
                    layers[other].redraw();
                }
                offerBackgrounds();
            } else if (!outgoing) {
                offerBackgrounds();
            }

            /* Always re-measure before adding a layer. The ResizeObserver catches
             * later changes, but the first show often happens before the browser
             * has laid the component out, and a map that still believes it is 0x0
             * decides no tiles are in view and draws nothing at all - silently,
             * with no error and a perfectly healthy-looking layer. */
            for (const s of SIDES) maps[s].invalidateSize({ animate: false, pan: false });

            /* Fit on one map only - leaflet.sync mirrors it to the other. Fitting
             * both makes sync bounce the view back and forth, which prunes tiles
             * mid-flight - measured at 113 stale tiles for every one drawn. */
            if (fittedMaps < keys.length) {
                const area = extentOf(keys);
                if (area) map.fitBounds(latLngBounds(area), { animate: false });
                fittedMaps = keys.length;
            }

            /* minZoom/maxZoom must be repeated here. GridLayer keeps its *own* zoom
             * limits, defaulting to 0 and 18, and when the map's zoom rounds past
             * them Leaflet sets _tileZoom to undefined and creates no tiles at all -
             * no error, no warning, an attached layer and an empty map. A 1:4000
             * map wants zooming well past 18. */
            const layer = new OcadTileLayer(mapKey, {
                tileSize: TILE_SIZE,
                noWrap: true,
                minZoom: MIN_ZOOM,
                maxZoom: MAX_ZOOM,
                opacity: opacities[side]
            });
            layer._ocadExtent = extentOf([mapKey]);
            layers[side] = layer;

            return new Promise(resolve => {
                pending[side] = resolve;

                layer._ocadCheck = () => {
                    if (layer._ocadDone >= layer._ocadAsked) {
                        layer._ocadCheck = null;
                        settle(side);
                    }
                };

                layer.addTo(map);

                /* Leaflet creates the tiles for the current view inside addTo, so
                 * the count is final by this line. A pane that asked for none -
                 * laid out at zero height, or scrolled off the map - has nothing
                 * to wait for and is settled here. */
                layer._ocadCheck();
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
         * @param placeOn (onGround, minX, maxY, unitsPerPixel, pixels) =>
         *        [a, b, c, d, e, f] from image pixels to a canvas's, or null
         */
        setOverlay(side, url, placeOn) {
            const map = paneFor(side);
            clearOverlay(side);

            const image = new Promise(resolve => {
                const img = new Image();
                img.onload = () => resolve(img);
                img.onerror = () => resolve(null);
                img.src = url;
            });
            overlays[side] = new MaskLayer(image, placeOn, {
                pane: 'overlayPane',
                tileSize: TILE_SIZE,
                minZoom: MIN_ZOOM,
                maxZoom: MAX_ZOOM,
                className: 'changeMask'
            }).addTo(map);
        },

        clearOverlays() {
            for (const side of SIDES) clearOverlay(side);
        },

        /**
         * Hides the map on one pane, leaving any overlay on top of it, and shows
         * it on every other. Null shows every map.
         *
         * Hides Leaflet's tile pane rather than removing the layers, so turning
         * the map back on is instant - the tiles are still there. The change mask
         * sits in the overlay pane and is not touched.
         */
        hideMap(side) {
            for (const each of SIDES)
                maps[each].getPane('tilePane').style.visibility = each === side ? 'hidden' : '';
        },

        /**
         * Lets the next show fit the view again, as the very first one did.
         *
         * For a different project: its maps can lie anywhere, so keeping the last
         * project's zoom and centre could leave both panes looking at nothing.
         */
        refit() {
            fittedMaps = 0;
        },

        /**
         * The words the background menus show, in the page's language.
         * @param labels {background, none, opacity, noGeoreference, offline}
         */
        setBackgroundLabels(labels) {
            for (const side of SIDES) backgrounds[side].setLabels(labels);
        },

        stats,

        dispose() {
            pump.dispose();
            resizeObserver.disconnect();
            for (const side of SIDES) {
                // Anything still waiting for this pane never will be.
                settle(side);
                clearOverlay(side);
                backgrounds[side].dispose();
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
    globalThis.__ocad = { maps, layers, stats, pump, get onGround() { return onGround; } };

    return handle;
}
