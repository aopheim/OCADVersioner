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
import { paint, coverage, boundsOf } from './changeMask.js';

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

    /**
     * Which pane is showing a version, found by name rather than by position.
     *
     * The newer side is the right one today, and saying so here would be the
     * third place that knew it. Asking the viewport what it is showing keeps
     * this working if the panes are ever swapped or a third is added.
     */
    function sideShowing(versionName) {
        const showing = view.showing();
        return Object.keys(showing).find(side => showing[side] === versionName) ?? null;
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
         * Shows where the two versions differ, as a tinted overlay.
         *
         * On the newer side only. The change is a property of the newer map -
         * this is what was done to it - and tinting the older one as well
         * covered the very pixels you turn to it to see.
         *
         * The mask is computed once per comparison, both whole maps rendered and
         * differenced, and then kept with the image painted from it. Turning the
         * overlay off and on again costs nothing.
         *
         * @returns what was drawn, for reporting: size, pixel counts, timings
         */
        async showChanges(olderName, newerName, show) {
            if (!show) {
                view.clearOverlays();
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

                changes = {
                    older: olderName, newer: newerName, plan, mask,
                    url: await paint(mask, plan.width, plan.height)
                };
            }

            const side = sideShowing(newerName);
            if (!side)
                throw new Error(`${newerName} is not on screen.`);

            view.clearOverlays();
            view.setOverlay(side, changes.url, {
                minX: changes.plan.minX, minY: changes.plan.minY,
                maxX: changes.plan.maxX, maxY: changes.plan.maxY
            });

            const { shown, total } = coverage(changes.mask);

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
         * Moves both panes to frame every change.
         *
         * A small revision is a few specks on a whole map - correct, and almost
         * impossible to find by eye. The mask already knows where they are.
         *
         * @param paddingMm breathing room around the changes
         * @returns the framed rectangle in paper millimetres, or null if there
         *          is nothing to frame
         */
        zoomToChanges(paddingMm = 12) {
            if (!changes?.mask)
                return null;

            const { width, height, minX, minY, maxX, maxY } = changes.plan;
            const box = boundsOf(changes.mask, width, height);
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

        /**
         * Moves both panes to frame one rectangle of the map - an object from the
         * diff table. A point is a rectangle with no size, so the padding is what
         * gives it a sensible zoom.
         *
         * @param area paper millimetres, Y up
         * @param paddingMm breathing room around it
         */
        zoomTo(area, paddingMm = 8) {
            const framed = {
                minX: area.minX - paddingMm, maxX: area.maxX + paddingMm,
                minY: area.minY - paddingMm, maxY: area.maxY + paddingMm
            };
            view.fitTo(framed);
            return framed;
        },

        /** Shows or hides the maps under the change overlay, on both panes. */
        setMapVisible(visible) {
            view.setMapVisible(visible);
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
