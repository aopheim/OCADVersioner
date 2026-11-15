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

        /** Raw RGBA for one square tile, or null if that map is no longer held. */
        render(key, minX, minY, width, height, pixels) {
            return interop.RenderTile(store, key, minX, minY, width, height, pixels);
        },

        /**
         * How big a change mask for this comparison should be, and what it covers.
         * @returns {ok, width, height, minX, minY, maxX, maxY} or {ok: false, error}
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
