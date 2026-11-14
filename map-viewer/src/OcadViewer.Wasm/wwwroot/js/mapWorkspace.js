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
import { createSyncedMaps, SIDES } from './leafletViewport.js';
import { paint, showsAnything, coverage, boundsOf } from './changeMask.js';

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

    /* The change mask for one comparison: the bytes, what they cover, and the
     * image currently painted from them. Kept because computing it means
     * rendering both whole maps - seconds of work - while changing which kinds
     * of change are shown is only a repaint of these same bytes. */
    let changes = null;

    function forgetChanges() {
        if (changes?.url) URL.revokeObjectURL(changes.url);
        changes = null;
    }

    /** Drops any mask that was computed against this version. */
    function invalidate(versionName) {
        if (changes && (changes.older === versionName || changes.newer === versionName)) {
            view.clearOverlays();
            forgetChanges();
        }
    }

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

                // The file on disk has just been read again, so any mask built
                // against the old contents is describing a map that is gone.
                invalidate(versionName);
            }

            const info = parsed.get(versionName);
            if (!info)
                throw new Error(`No parse result for "${versionName}".`);

            view.show(side, versionName, info);
            return info;
        },

        /**
         * Shows where the two versions differ, as a tinted overlay on both panes.
         *
         * The mask is computed once per comparison - both whole maps rendered and
         * differenced - and then kept, so turning the filters on and off is a
         * repaint of the same bytes with no .NET call at all.
         *
         * @param filters {added, deleted}; neither set means hide the overlay
         * @returns what was drawn, for reporting: size, pixel counts, timings
         */
        async showChanges(olderName, newerName, filters) {
            if (!showsAnything(filters)) {
                view.clearOverlays();
                if (changes?.url) { URL.revokeObjectURL(changes.url); changes.url = null; }
                return { shown: false };
            }

            let computedMs = 0;

            if (changes?.older !== olderName || changes?.newer !== newerName) {
                forgetChanges();

                const plan = tiles.planChangeMask(newerName);
                if (!plan.ok)
                    throw new Error(plan.error ?? `Cannot measure ${newerName}.`);

                const started = performance.now();
                const mask = tiles.renderChangeMask(olderName, newerName);
                computedMs = performance.now() - started;

                if (!mask)
                    throw new Error('One of the two versions is no longer loaded.');

                changes = { older: olderName, newer: newerName, plan, mask, url: null };
            }

            if (changes.url) URL.revokeObjectURL(changes.url);
            changes.filters = filters;
            changes.url = await paint(changes.mask, changes.plan.width, changes.plan.height, filters);

            const bounds = {
                minX: changes.plan.minX, minY: changes.plan.minY,
                maxX: changes.plan.maxX, maxY: changes.plan.maxY
            };
            for (const side of SIDES) view.setOverlay(side, changes.url, bounds);

            const { shown, total } = coverage(changes.mask, filters);

            return {
                shown: true,
                width: changes.plan.width,
                height: changes.plan.height,
                computedMs: Math.round(computedMs),
                shownPixels: shown,
                totalPixels: total
            };
        },

        /**
         * Moves both panes to frame everything the overlay is showing.
         *
         * A small revision is a few specks on a whole map - correct, and almost
         * impossible to find by eye. The mask already knows where they are.
         *
         * @param paddingMm breathing room around the changes
         * @returns the framed rectangle in paper millimetres, or null if there
         *          is nothing shown to frame
         */
        zoomToChanges(paddingMm = 12) {
            if (!changes?.mask || !changes.filters)
                return null;

            const { width, height, minX, minY, maxX, maxY } = changes.plan;
            const box = boundsOf(changes.mask, width, height, changes.filters);
            if (!box) return null;

            // Mask pixels to paper millimetres. The mask covers the map's own
            // rectangle exactly, and its rows run downwards while paper Y runs
            // up, which is the only subtlety here.
            const x = px => minX + (px / width) * (maxX - minX);
            const y = py => maxY - (py / height) * (maxY - minY);

            const area = {
                minX: x(box.left) - paddingMm,
                maxX: x(box.right + 1) + paddingMm,
                minY: y(box.bottom + 1) - paddingMm,
                maxY: y(box.top) + paddingMm
            };

            view.fitTo(area);
            return area;
        },

        showing: () => view.showing(),

        stats: view.stats,

        dispose() {
            forgetChanges();
            view.dispose();
            for (const name of parsed.keys())
                tiles.unload(name);
            parsed.clear();
        }
    };
}
