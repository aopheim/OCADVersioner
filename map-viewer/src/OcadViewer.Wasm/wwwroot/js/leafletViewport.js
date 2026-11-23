/* Two synced Leaflet maps whose tiles come from somewhere else.
 *
 * Maps are drawn in OCAD paper millimetres, so Leaflet uses L.CRS.Simple and paper
 * coordinates are Leaflet coordinates directly: lng = paper X, lat = paper Y. Tile
 * world rects come from map.unproject, which keeps the maths to four lines and lets
 * the two panes be kept together without knowing anything about the layer.
 *
 * This module knows about Leaflet and nothing else. The tile renderer and the
 * scheduler are passed in; it does not import the .NET runtime, read files, or
 * know what a project version is. Composition happens in mapWorkspace.js.
 */

import { createTilePump } from './tilePump.js';
import { createBackground } from './backgroundMap.js';

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
 * A map's paper-to-ground transform if it can be used to line panes up: six
 * finite numbers, and paper that is not squashed to a line. Otherwise null.
 */
function usableGround(ground) {
    if (!Array.isArray(ground) || ground.length !== 6 || !ground.every(Number.isFinite))
        return null;
    const [a, b, , d, e] = ground;
    return a * e - b * d !== 0 ? ground : null;
}

/**
 * How to go from a point on one pane's paper to the same place on the other's.
 *
 * Each transform takes paper (x, y) to the same ground grid, so one pane's paper
 * goes to the other's through the ground: forward on its own, back through the
 * other's inverse. dz is the zoom to add for the same ground metres per pixel -
 * under L.CRS.Simple a pixel is 2^-zoom millimetres, so a map whose millimetre
 * covers twice the ground needs one zoom level less.
 *
 * With either map not georeferenced there is no ground to go through, and the
 * panes line up on the paper, as they always used to.
 *
 * @param left  [A, B, C, D, E, F] or null; x' = Ax + By + C, y' = Dx + Ey + F
 * @param right likewise
 * @returns {{left: Function, right: Function}} from that side's paper (x, y)
 *          to {x, y, dz} on the other side
 */
function alignmentOf(left, right) {
    if (!left || !right) {
        const same = (x, y) => ({ x, y, dz: 0 });
        return { left: same, right: same };
    }
    return { left: through(left, right), right: through(right, left) };
}

function through(from, to) {
    const [a, b, c, d, e, f] = from;
    const [A, B, C, D, E, F] = to;
    const det = A * E - B * D;

    // Paper millimetres on `to` per paper millimetre on `from`, on the ground.
    const ratio = Math.sqrt(Math.abs((a * e - b * d) / det));
    const dz = -Math.log2(ratio);

    return (x, y) => {
        const gx = a * x + b * y + c - C;
        const gy = d * x + e * y + f - F;
        return { x: (E * gx - B * gy) / det, y: (A * gy - D * gx) / det, dz };
    };
}

/** Whether Leaflet believes a map has an area to draw in. */
function visible(map) {
    const size = map.getSize();
    return size.x > 0 && size.y > 0;
}

/**
 * Runs fn with the map's 'viewprereset' listeners held back.
 *
 * Moving a map to a new zoom without an animation resets its view, and the reset
 * tells every tile layer to throw its tiles away and start again. For a map that
 * is following another through a pinch, that is a blank pane on every frame, and
 * renders at hundreds of milliseconds a tile that never get shown. Without the
 * event, the layers keep their tiles and scale them, as they do for an animated
 * zoom. The same trick leaflet.sync used.
 */
function quietly(map, fn) {
    const events = map._events;
    const held = events?.viewprereset;
    if (held) events.viewprereset = [];
    try {
        return fn();
    } finally {
        if (held) events.viewprereset = held;
    }
}

/**
 * Creates two synced maps over the given elements.
 *
 * @param leftEl  element for the left map
 * @param rightEl element for the right map
 * @param renderTile (key, minX, minY, width, height, pixels) => Uint8Array|null
 * @param background {sources, tiles}: the background maps on offer, and
 *        (key, sourceId, minX, maxY, mmPerPixel, pixels) => [{url, size, transform}]
 *        saying which web tiles to draw on a background canvas, and where
 * @returns a handle with show, stats and dispose.
 */
export function createSyncedMaps(leftEl, rightEl, renderTile, background) {
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

    // Every map needs a view before it can be asked for one; the first show replaces this.
    for (const side of SIDES) maps[side].setView([0, 0], 0);

    /* Each pane's paper on the ground, as the map's summary gave it, or null for
     * a map that is not georeferenced - and the alignment that follows from the
     * pair: how to go from one pane's paper to the other's. */
    const grounds = { left: null, right: null };
    let alignment = alignmentOf(null, null);

    /* Keeping the two panes on the same place.
     *
     * Not leaflet.sync. That copies a view from one map to the other unchanged,
     * which is right only when both versions sit at the same place on the paper -
     * and a mapper moves, rotates and rescales a map between versions. Here the
     * centre is carried through both georeferences, so the panes show the same
     * point on the ground, and the zoom is offset by the scale ratio, so they
     * show the same ground metres per pixel.
     *
     * Either map can lead: whichever moves, the other follows. `following` stops
     * a map that is being moved to match from moving the first one in turn, and
     * the tolerance stops two maps that already agree to within rounding from
     * nudging each other for ever after an animation ends on each. */
    let following = false;

    function follow(from, center = maps[from].getCenter(), zoom = maps[from].getZoom(), animate = false) {
        if (following) return;

        const to = SIDES.find(s => s !== from);
        const target = maps[to];
        const leader = maps[from];

        // A hidden pane is put right by the resize observer when it reappears.
        if (!visible(leader) || !visible(target)) return;
        // Mid-animation, a setView would cut it short; it catches up when it ends.
        if (target._animatingZoom) return;

        const { x, y, dz } = alignment[from](center.lng, center.lat);
        const goal = L.latLng(y, x);
        const goalZoom = target._limitZoom(zoom + dz);

        if (Math.abs(target.getZoom() - goalZoom) < 1e-6) {
            const off = target.project(goal, goalZoom).subtract(target.project(target.getCenter(), goalZoom));
            if (Math.abs(off.x) < 0.5 && Math.abs(off.y) < 0.5) return;
        }

        following = true;
        try {
            quietly(target, () => target.setView(goal, goalZoom, { animate }));
        } finally {
            following = false;
        }
    }

    /* Tells Leaflet a map's real size without the other map following.
     *
     * invalidateSize keeps the top-left corner where it was, so the centre moves
     * and the map fires 'move'. For a pane coming back from hidden that move is
     * from wherever it was left, and following it would drag the visible pane
     * there; whoever calls this decides who leads afterwards. */
    function remeasure(map) {
        const was = following;
        following = true;
        try {
            map.invalidateSize({ animate: false, pan: false });
        } finally {
            following = was;
        }
    }

    /* Which pane the mapper last laid a hand on. Only that one leads by its own
     * movement. Otherwise a pane that cannot zoom as far as its partner - a
     * 1:15000 version beside a 1:4000 one - would end its animation at its limit
     * and pull the pane the mapper is zooming back to match it. Moves made in
     * code, which no hand started, name their leader by calling follow. */
    let leading = null;

    for (const side of SIDES) {
        const el = maps[side].getContainer();
        for (const type of ['pointerdown', 'wheel', 'keydown', 'touchstart'])
            el.addEventListener(type, () => { leading = side; }, { capture: true, passive: true });

        const lead = (...args) => { if (leading === null || leading === side) follow(side, ...args); };

        /* 'move' covers drags, inertia, pans and the end of every zoom; 'zoom'
         * covers pinches, which move the view without a 'move' until they end.
         * 'zoomanim' starts the partner's own zoom animation alongside, so the
         * two zoom together instead of one jumping a quarter-second later. */
        maps[side].on('move zoom', () => lead());
        maps[side].on('zoomanim', e => lead(e.center, e.zoom, true));

        /* A follower animating its own zoom skips the leader's last word - the
         * leader often lands first - and would stay where its animation was
         * aimed, a pixel or so off. When it stops, it catches up once more. */
        maps[side].on('moveend', () => { if (leading && leading !== side) follow(leading); });
    }

    /* A web map under each pane, with its own menu. Per pane, because the two
     * maps can be different versions with different georeferences, and the
     * mapper may want aerial photos beside one and a street map beside the other. */
    const backgrounds = {};
    /* How strongly each pane's OCAD map is drawn, so a background shows through.
     * Kept here rather than on the layer because a new layer replaces the old one
     * on every show, and the mapper's choice should survive that. */
    const opacities = { left: 1, right: 1 };
    for (const side of SIDES) {
        backgrounds[side] = createBackground(maps[side], {
            minZoom: MIN_ZOOM,
            maxZoom: MAX_ZOOM,
            sources: background.sources,
            onOpacity: value => {
                opacities[side] = value;
                layers[side]?.setOpacity(value);
            }
        });
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
            remeasure(map);

            /* A hidden map is not followed while it is hidden, so it comes back
             * looking wherever it was left: put it back where the visible map is.
             * A map that was only resized has moved its centre, and leads. */
            follow(wasHidden ? SIDES.find(s => s !== side) : side);
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
            const ctx = tile.getContext('2d');
            ctx.putImageData(new ImageData(new Uint8ClampedArray(bytes), size.x, size.y), 0, 0);

            /* The renderer paints paper white over the whole tile, including the
             * part past the map's edge. Invisible on its own, but under a faded
             * map it would wash out a background map in tile-shaped blocks that
             * stop at the tile grid. Cleared, the paper ends where the map does. */
            const perMm = size.x / (maxX - minX);
            const left = Math.max(0, (b.minX - minX) * perMm);
            const right = Math.min(size.x, (b.maxX - minX) * perMm);
            const top = Math.max(0, (maxY - b.maxY) * perMm);
            const bottom = Math.min(size.y, (maxY - b.minY) * perMm);
            if (left > 0) ctx.clearRect(0, 0, Math.ceil(left), size.y);
            if (right < size.x) ctx.clearRect(Math.floor(right), 0, size.x, size.y);
            if (top > 0) ctx.clearRect(0, 0, size.x, Math.ceil(top));
            if (bottom < size.y) ctx.clearRect(0, Math.floor(bottom), size.x, size.y);
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

    /* --------------------------------------------------------------- public */

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
         * @param bounds {minX, minY, maxX, maxY} in paper millimetres
         * @returns {Promise<void>} resolved when the pane is drawn, or when the
         *          pane is handed to another map and these tiles are abandoned
         */
        show(side, mapKey, bounds) {
            const map = paneFor(side);

            // Drop queued work for the layer being replaced, rather than letting
            // the pump grind through tiles for a map that is no longer shown.
            const outgoing = layers[side];
            if (outgoing) {
                pump.remove(tag => tag === outgoing);
                outgoing._ocadCheck = null;
                outgoing.remove();
            }

            // Those dropped tiles are what somebody was waiting for.
            settle(side);

            /* Always re-measure before adding a layer. The ResizeObserver catches
             * later changes, but the first show often happens before the browser
             * has laid the component out, and a map that still believes it is 0x0
             * decides no tiles are in view and draws nothing at all - silently,
             * with no error and a perfectly healthy-looking layer. */
            for (const s of SIDES) remeasure(maps[s]);

            // This map's place on the ground decides how the panes line up from now on.
            grounds[side] = usableGround(bounds.ground);
            alignment = alignmentOf(grounds.left, grounds.right);

            /* Fit only on the first show, and only on one map - the other follows.
             * Re-fitting on every show fights the user's own zoom, and fitting
             * both maps bounces the view back and forth, which prunes tiles
             * mid-flight - measured at 113 stale tiles for every one drawn.
             *
             * Every later show puts this pane where its partner is looking: a
             * different version can sit somewhere else on the paper, and keeping
             * this pane's old paper view would show a different place. */
            const other = SIDES.find(s => s !== side);
            if (!fitted) {
                map.fitBounds(latLngBounds(bounds), { animate: false });
                fitted = true;
                follow(side);
            } else if (layers[other]) {
                follow(other);
            } else {
                follow(side);
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
                maxZoom: MAX_ZOOM,
                opacity: opacities[side]
            });
            layer._ocadBounds = { ...bounds };
            layers[side] = layer;

            // The background follows the map: which web maps it can be laid on,
            // and where their tiles go, are asked of the map now on this pane.
            backgrounds[side].setMap(bounds.backgroundSources ?? null,
                (sourceId, minX, maxY, mmPerPixel, pixels) =>
                    background.tiles(mapKey, sourceId, minX, maxY, mmPerPixel, pixels));

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
         * For a different project: its maps can sit anywhere on the paper, so
         * keeping the last project's zoom and centre could leave both panes
         * looking at empty paper.
         */
        refit() {
            fitted = false;
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
    globalThis.__ocad = { maps, layers, stats, pump };

    return handle;
}
