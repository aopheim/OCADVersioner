/* Maps and tiles from the .NET renderer, addressed by name.
 *
 * This is the ONLY module that reaches into .NET. Everything above it - the
 * Leaflet viewport, the pump - is handed a plain object and has no idea what is
 * behind it, which is what lets them be loaded and tested without Blazor. The
 * boundary is enforced by OcadViewer.ArchitectureTests.
 *
 * Each tile source owns a store of parsed maps on the .NET side, created with it
 * and disposed with it, so two sources never see each other's maps.
 */

import { getTileInterop } from './dotnetExports.js';

/**
 * @typedef {object} MapSummary
 * @property {boolean} ok
 * @property {string} key
 * @property {string} [error]
 * @property {number} [loadId] which parse of this key; changes when it is read again
 * @property {number} [minX] @property {number} [minY] @property {number} [maxX] @property {number} [maxY]
 * @property {number} [parseMs]
 * @property {string} [georeference]
 * @property {string[]} [backgroundSources] ids of the background maps it can be laid on;
 *           absent when it has no usable georeference
 */

/**
 * Resolves the .NET side and wraps a fresh store of maps in it.
 */
export async function createTileSource() {
    const interop = await getTileInterop();
    const store = interop.CreateStore();

    return {
        /** Whether this map is already parsed, so the caller can skip re-reading it. */
        isLoaded(key) {
            return interop.IsLoaded(store, key);
        },

        /**
         * Parses bytes and keeps them under a name.
         * @returns {MapSummary}
         */
        load(key, fileName, bytes) {
            return JSON.parse(interop.LoadMap(store, key, fileName, bytes));
        },

        /**
         * What is held under a name, as load() reported it.
         * @returns {MapSummary} with ok false when nothing is held
         */
        describe(key) {
            return JSON.parse(interop.Describe(store, key));
        },

        /**
         * Straight RGBA for one square canvas: the canvas's top-left corner and
         * units per pixel, in Web Mercator metres on the ground - or paper
         * millimetres, when onGround is false. Empty where the map is not; null
         * if that map is no longer held.
         */
        render(key, onGround, minX, maxY, unitsPerPixel, pixels) {
            return interop.RenderOnGround(store, key, onGround, minX, maxY, unitsPerPixel, pixels);
        },

        /** Whether these maps can all be shown on the ground, rather than on paper. */
        canShowOnGround(keys) {
            return interop.CanShowOnGround(store, keys);
        },

        /**
         * What these maps cover, in the coordinates shown.
         * @returns {{minX: number, minY: number, maxX: number, maxY: number}|null}
         */
        extent(keys, onGround) {
            const e = interop.Extent(store, keys, onGround);
            return e ? { minX: e[0], minY: e[1], maxX: e[2], maxY: e[3] } : null;
        },

        /**
         * Where a change mask lands on one canvas: mask pixels to canvas pixels as
         * setTransform takes them, placed through the newer map. Null when it is
         * not held.
         * @param plan what planChangeMask answered
         */
        maskToCanvas(newerKey, onGround, plan, minX, maxY, unitsPerPixel, pixels) {
            const t = interop.MaskToCanvas(store, newerKey, onGround,
                plan.minX, plan.minY, plan.maxX, plan.maxY, plan.width, plan.height,
                minX, maxY, unitsPerPixel, pixels);
            return t ? Array.from(t) : null;
        },

        /**
         * How big a change mask for this comparison should be, and what it covers.
         * @returns {ok, width, height, minX, minY, maxX, maxY, changeBits, inMap}
         *          or {ok: false, error}; changeBits and inMap say how to read the mask
         */
        planChangeMask(olderKey, newerKey) {
            return JSON.parse(interop.PlanChangeMask(store, olderKey, newerKey));
        },

        /**
         * Where two versions differ over a planned rectangle: one byte per pixel.
         *
         * This is the expensive one - it renders both versions of the whole map -
         * so call it once per comparison and keep the result.
         * @param plan what planChangeMask answered
         * @returns Uint8Array, or null if either version has gone from the store
         */
        renderChangeMask(olderKey, newerKey, plan) {
            return interop.RenderChangeMask(store, olderKey, newerKey,
                plan.minX, plan.minY, plan.maxX, plan.maxY, plan.width, plan.height);
        },

        /**
         * The background maps on offer, in menu order.
         * @returns {{id: string, name: string, attribution: string}[]}
         */
        backgroundSources() {
            return JSON.parse(interop.BackgroundSources());
        },

        /**
         * The web tiles to draw on one square canvas of the ground, given in Web
         * Mercator metres. The same under every map, so no map is named.
         * @returns {{url: string, size: number, transform: number[]}[]} empty when
         *          there is nothing to draw
         */
        backgroundOnGround(sourceId, minX, maxY, metresPerPixel, pixels) {
            return JSON.parse(interop.BackgroundOnGround(sourceId, minX, maxY, metresPerPixel, pixels));
        },

        /** Forgets every map; the source stays usable. */
        clear() {
            interop.ClearStore(store);
        },

        /** Releases the store. Nothing here works afterwards. */
        dispose() {
            interop.DisposeStore(store);
        }
    };
}
