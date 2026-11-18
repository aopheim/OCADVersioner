/* Where the three halves of "show a version in a pane" are wired together.
 *
 * The composition root for the viewer, and the only module the Blazor component
 * talks to. It is here, and only here, that reading a file, parsing it in .NET and
 * putting it on a Leaflet map meet each other - each of which is a module that
 * knows nothing about the other two.
 *
 * Map bytes are read here and handed straight to the renderer. C# only says which
 * map it wants and where its bytes are: a path in the project folder (parse), or
 * a file input the user chose a file in (showFile).
 *
 * What is parsed is held in one place: the tile source's store. This module keeps
 * no record of its own of what was loaded. The only thing it caches is the change
 * mask, and that is tagged with the load ids of the two parses it was made from,
 * so it goes stale by itself when either is read again.
 */

import { readBytes } from './projectFolder.js';
import { createTileSource } from './mapTiles.js';
import { createSyncedMaps } from './leafletViewport.js';
import { paint, coverage, boundsOf } from './changeMask.js';

/**
 * Builds the viewer over two elements.
 * @returns {Promise<{parse: Function, draw: Function, showFile: Function, showChanges: Function,
 *                    zoomToChanges: Function, setMapVisible: Function, reset: Function,
 *                    showing: Function, stats: object, dispose: Function}>}
 */
export async function createWorkspace(leftEl, rightEl) {
    const tiles = await createTileSource();
    const view = createSyncedMaps(leftEl, rightEl, tiles.render);

    /* The change mask for one pair of parses: the bytes, the plan they were drawn
     * to, the image painted from them and how much they cover. Kept because
     * computing it means rendering both whole maps, while turning the overlay
     * off and on again should cost nothing. */
    let changes = null;

    function forgetChanges() {
        if (changes?.url) URL.revokeObjectURL(changes.url);
        changes = null;
    }

    /**
     * Names the two parses a mask would be made from, or null if either is not
     * held. Two masks with the same key were drawn from the same parses.
     */
    function maskKey(olderName, newerName) {
        const older = tiles.describe(olderName);
        const newer = tiles.describe(newerName);
        return older.ok && newer.ok
            ? `${olderName}#${older.loadId}|${newerName}#${newer.loadId}`
            : null;
    }

    /** Drops the mask, and takes it off the screen, if its parses have changed. */
    function dropStaleChanges() {
        if (changes && changes.key !== maskKey(changes.older, changes.newer)) {
            view.clearOverlays();
            forgetChanges();
        }
    }

    /**
     * Which pane is showing a version, found by name rather than by position.
     * Asking the viewport keeps this working if the panes are ever swapped.
     */
    function sideShowing(versionName) {
        const showing = view.showing();
        return Object.keys(showing).find(side => showing[side] === versionName) ?? null;
    }

    /**
     * Makes sure a map is parsed, wherever its bytes come from.
     *
     * @param read async () => ({ fileName, bytes }); only called when the map
     *             has to be (re)parsed
     * @returns the parse summary
     */
    async function parseFrom(mapName, read, reload) {
        if (!reload && tiles.isLoaded(mapName))
            return tiles.describe(mapName);

        const { fileName, bytes } = await read();
        const info = tiles.load(mapName, fileName, bytes);

        if (!info.ok)
            throw new Error(info.error ?? `Could not read ${fileName}.`);

        dropStaleChanges();
        return info;
    }

    /** Puts a parsed map in a pane; resolves once the pane has drawn it. */
    function draw(side, mapName) {
        return view.show(side, mapName, tiles.describe(mapName));
    }

    return {
        /**
         * Makes sure a version from the project folder is parsed, reading it
         * only if it has to. Parsing and drawing are separate calls so the page
         * can say which is happening, and can stop between them when the mapper
         * has already picked something else.
         *
         * @param versionName the name the map is held under
         * @param path where its file is, relative to the project folder
         * @param reload true when the file can have changed since it was read
         * @returns the parse summary
         */
        parse(versionName, path, reload) {
            return parseFrom(versionName, async () => ({
                fileName: path.split('/').pop(),
                bytes: await readBytes(path)
            }), reload);
        },

        /**
         * Puts a parsed map in a pane.
         *
         * @param side a pane name, passed through to the viewport
         * @returns resolved when the pane has drawn it, or when another map
         *          takes the pane first
         */
        draw,

        /**
         * Shows the file chosen in an <input type="file"> on one side.
         *
         * The element is passed rather than the file because a File cannot
         * cross into .NET - and should not: map bytes go straight from the
         * browser to the renderer. Always re-read, since the same input can
         * hold a different file each time it is asked.
         *
         * @param mapName the name the map is held under
         * @param input the file input element
         * @returns the parse summary
         */
        async showFile(side, mapName, input) {
            const file = input?.files?.[0];
            if (!file)
                throw new Error('No file was chosen.');

            /* Emptied once the file is in hand, because a browser only fires
             * 'change' when the choice differs from what the input holds - and
             * choosing the same file again, after saving it in OCAD, is exactly
             * how a user reloads it. */
            input.value = '';

            const info = await parseFrom(mapName, async () => ({
                fileName: file.name,
                bytes: new Uint8Array(await file.arrayBuffer())
            }), true);

            draw(side, mapName);
            return info;
        },

        /**
         * Shows where the two versions differ, as a tinted overlay.
         *
         * On the newer side only. The change is a property of the newer map -
         * this is what was done to it - and tinting the older one as well would
         * cover the very pixels you turn to it to see.
         *
         * @returns what was drawn, for reporting: size, pixel counts, timings
         */
        async showChanges(olderName, newerName, show) {
            if (!show) {
                view.clearOverlays();
                return { shown: false };
            }

            const key = maskKey(olderName, newerName);
            if (!key)
                throw new Error(`${olderName} and ${newerName} are not both loaded.`);

            let computedMs = 0;

            if (changes?.key !== key) {
                forgetChanges();

                const plan = tiles.planChangeMask(olderName, newerName);
                if (!plan.ok)
                    throw new Error(plan.error ?? `Cannot measure ${olderName} against ${newerName}.`);

                const started = performance.now();
                const mask = tiles.renderChangeMask(olderName, newerName, plan);
                computedMs = performance.now() - started;

                if (!mask)
                    throw new Error('One of the two versions is no longer loaded.');

                changes = {
                    key, older: olderName, newer: newerName, plan, mask,
                    covered: coverage(mask, plan),
                    url: await paint(mask, plan)
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
                shownPixels: changes.covered.shown,
                totalPixels: changes.covered.total
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
            const box = boundsOf(changes.mask, changes.plan);
            if (!box) return null;

            // Mask pixels to paper millimetres. The mask covers the planned
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

        /** Shows or hides the maps under the change overlay, on both panes. */
        setMapVisible(visible) {
            view.setMapVisible(visible);
        },

        /**
         * Forgets every parsed map and change mask, for when a different
         * project folder is opened. Version names are only unique within one
         * project - every project has a "V1".
         */
        reset() {
            view.clearOverlays();
            forgetChanges();
            tiles.clear();
            view.refit();
        },

        showing: () => view.showing(),

        stats: view.stats,

        dispose() {
            forgetChanges();
            view.dispose();
            tiles.dispose();
        }
    };
}
