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
import { paint } from './changeMask.js';

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

    /* What one comparison came to: the plan it was computed under, what the
     * grouping found, and the image hanging on the map. Kept because getting
     * here means rendering both whole maps and differencing them - seconds of
     * work - while turning the overlay off and on, or walking the region list,
     * is free from this. The mask itself is not kept; see showChanges. */
    let changes = null;

    function forgetChanges() {
        if (changes?.url) URL.revokeObjectURL(changes.url);
        changes = null;
    }

    /**
     * Which pane is showing a version, found by name rather than by position.
     *
     * The newer side is the left one today, and saying so here would be the
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

    /**
     * Puts a rectangle of paper on screen, with room around it.
     *
     * Zooming to everything and zooming to one place differ in which rectangle
     * they ask for and nothing else, so the asking is written once.
     */
    function frame(area, paddingMm) {
        if (!area) return null;

        const padded = {
            minX: area.minX - paddingMm, minY: area.minY - paddingMm,
            maxX: area.maxX + paddingMm, maxY: area.maxY + paddingMm
        };

        view.fitTo(padded);
        return padded;
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
         * Computed once per comparison: both whole maps rendered, differenced,
         * grouped into places, and the picture painted. Turning the overlay off
         * and on again costs nothing, and neither does the region list.
         *
         * @returns what was drawn and found, for reporting and for the list of
         *          places: size, pixel counts, timings, regions
         */
        async showChanges(olderName, newerName, show) {
            if (!show) {
                view.clearOverlays();
                return { shown: false };
            }

            let computedMs = 0;
            let groupedMs = 0;

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

                const grouping = performance.now();
                const found = tiles.groupChanges(
                    mask, plan.width, plan.height,
                    plan.minX, plan.minY, plan.maxX, plan.maxY);
                groupedMs = performance.now() - grouping;

                // The mask itself is not kept. Everything anything above here
                // asks of it - how much changed, where, and what it looks like -
                // has now been answered, and it is a megabyte per comparison.
                changes = {
                    older: olderName, newer: newerName, plan, found,
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

            return {
                shown: true,
                width: changes.plan.width,
                height: changes.plan.height,
                computedMs: Math.round(computedMs),
                groupedMs: Math.round(groupedMs),
                shownPixels: changes.found.changedPixels,
                smallerRegions: changes.found.smallerRegions,
                totalPixels: changes.found.totalPixels,
                regions: changes.found.regions
            };
        },

        /**
         * Moves both panes to frame every change.
         *
         * A small revision is a few specks on a whole map - correct, and almost
         * impossible to find by eye. The grouping already knows where they are.
         *
         * @param paddingMm breathing room around the changes
         * @returns the framed rectangle in paper millimetres, or null if there
         *          is nothing to frame
         */
        zoomToChanges(paddingMm = 12) {
            return frame(changes?.found?.bounds, paddingMm);
        },

        /**
         * Moves both panes to frame one of the places that changed.
         *
         * @param rank as the region reported it, 1 for the largest
         * @param paddingMm breathing room; less than for the whole set, because
         *                  this is deliberately a close look
         * @returns the framed rectangle in paper millimetres, or null if there
         *          is no region with that rank
         */
        zoomToRegion(rank, paddingMm = 6) {
            const region = changes?.found?.regions
                ?.find(candidate => candidate.rank === rank);

            return frame(region?.area, paddingMm);
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
