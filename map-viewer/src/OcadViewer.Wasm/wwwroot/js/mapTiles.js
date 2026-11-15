/* Maps and tiles from the .NET renderer, addressed by name.
 *
 * This is the ONLY module that reaches into .NET. Everything above it - the
 * Leaflet viewport, the pump - is handed a plain object with these four methods
 * and has no idea what is behind them. That boundary is enforced by
 * OcadViewer.ArchitectureTests; before it existed the Leaflet module imported the
 * runtime directly and could not be loaded, tested or reused without Blazor.
 */

import { getTileInterop } from './dotnetExports.js';

/**
 * @typedef {object} TileSource
 * @property {(key: string) => boolean} isLoaded
 * @property {(key: string, fileName: string, bytes: Uint8Array) => object} load
 * @property {(key: string) => void} unload
 * @property {(key: string, minX: number, minY: number, width: number, height: number,
 *             pixels: number) => Uint8Array|null} render
 */

/**
 * Resolves the .NET side and wraps it.
 * @returns {Promise<TileSource>}
 */
export async function createTileSource() {
    const interop = await getTileInterop();

    return {
        /** Whether this map is already parsed, so the caller can skip re-reading it. */
        isLoaded(key) {
            return interop.IsLoaded(key);
        },

        /**
         * Parses bytes and keeps them under a name.
         * @returns the parse summary: {ok, key, version, minX, minY, maxX, maxY,
         *          parseMs, georeference} or {ok: false, key, error}.
         */
        load(key, fileName, bytes) {
            return JSON.parse(interop.LoadMap(key, fileName, bytes));
        },

        unload(key) {
            interop.UnloadMap(key);
        },

        /** Raw RGBA for one square tile, or null if that map is no longer held. */
        render(key, minX, minY, width, height, pixels) {
            return interop.RenderTile(key, minX, minY, width, height, pixels);
        },

        /**
         * How big a change mask for this comparison would be, and what it covers.
         * @returns {ok, width, height, minX, minY, maxX, maxY} or {ok: false, error}
         */
        planChangeMask(olderKey, newerKey) {
            return JSON.parse(interop.PlanChangeMask(olderKey, newerKey));
        },

        /**
         * Where two versions differ: one byte per pixel.
         *
         * This is the expensive one - it renders both versions of the whole map -
         * so it is called once per comparison and the result kept.
         * @returns Uint8Array, or null if either version has gone from the store
         */
        renderChangeMask(olderKey, newerKey) {
            return interop.RenderChangeMask(olderKey, newerKey);
        }
    };
}
