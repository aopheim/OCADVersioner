/* Where the three halves of "show a version in a pane" are wired together.
 *
 * The composition root for the viewer, and the only module the Blazor component
 * talks to. It is here, and only here, that reading a file, parsing it in .NET and
 * putting it on a Leaflet map meet each other - each of which is a module that
 * knows nothing about the other two.
 *
 * It is also why map bytes no longer cross the JS/.NET boundary three times. The
 * page used to ask .NET for the bytes, get them marshalled out of JavaScript, hand
 * them back down to JavaScript, and have JavaScript pass them into .NET again -
 * three crossings of a multi-megabyte array for data the page never looked at. Now
 * the bytes are read and handed straight to the renderer, and C# only ever says
 * which version it wants to see.
 */

import { readMap } from './projectFolder.js';
import { createTileSource } from './mapTiles.js';
import { createSyncedMaps } from './leafletViewport.js';

/**
 * Builds the viewer over two elements.
 * @returns {Promise<{show: Function, showing: Function, stats: object, dispose: Function}>}
 */
export async function createWorkspace(leftEl, rightEl) {
    const tiles = await createTileSource();
    const view = createSyncedMaps(leftEl, rightEl, tiles.render);

    /* What each parse reported, by version name. Bounds and georeferencing do not
     * change, so this survives the map itself being evicted from .NET's store and
     * re-read later. */
    const parsed = new Map();

    return {
        /**
         * Shows a version on one side, parsing it only if it is not already held.
         *
         * @param side 'left' or 'right'
         * @param versionName 'current', 'V3', ...
         * @param reload true for the working file, which changes on disk while the
         *               app is open; false for a release, which never does.
         * @returns the parse summary
         */
        async show(side, versionName, reload) {
            if (reload || !tiles.isLoaded(versionName)) {
                const bytes = await readMap(versionName);
                const info = tiles.load(versionName, `${versionName}.ocd`, bytes);

                if (!info.ok)
                    throw new Error(info.error ?? `Could not read ${versionName}.`);

                parsed.set(versionName, info);
            }

            const info = parsed.get(versionName);
            if (!info)
                throw new Error(`No parse result for "${versionName}".`);

            view.show(side, versionName, info);
            return info;
        },

        showing: () => view.showing(),

        stats: view.stats,

        dispose() {
            view.dispose();
            for (const name of parsed.keys())
                tiles.unload(name);
            parsed.clear();
        }
    };
}
