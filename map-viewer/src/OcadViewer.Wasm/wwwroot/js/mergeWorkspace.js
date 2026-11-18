/* The merge screen's composition root: tiles, panes and change outlines.
 *
 * Like mapWorkspace.js for the version comparison, this is the one module on the
 * merge screen that knows about several others at once. C# hands it the files'
 * bytes and the classified changes; it parses through the tile source, shows a
 * map per pane and draws the changes over it.
 *
 * Panes are named by the caller - one for the merge screen, three for the
 * conflict walker - and the names are passed through untouched.
 */
import { createTileSource } from './mapTiles.js';
import { createMaps } from './leafletViewport.js';
import { createChangeOverlay } from './mergeOverlay.js';

/**
 * @param elements {paneName: element}
 * @param dotnet   a DotNetObjectReference with a [JSInvokable] Pick(pane, key)
 */
export async function createMergeWorkspace(elements, dotnet) {
    const tiles = await createTileSource();
    const view = createMaps(elements, tiles.render);
    const overlays = Object.fromEntries(Object.keys(elements).map(pane => [
        pane,
        createChangeOverlay(view.leaflet(pane), key => dotnet.invokeMethodAsync('Pick', pane, key))
    ]));

    function overlay(pane) {
        const found = overlays[pane];
        if (!found) throw new Error(`Unknown pane "${pane}".`);
        return found;
    }

    return {
        /** Parses a file into the tile store under `key`. Returns its summary as JSON. */
        load(key, fileName, bytes) {
            const info = tiles.load(key, fileName, bytes);
            if (!info.ok)
                throw new Error(info.error ?? `Could not read ${fileName}.`);
            return JSON.stringify(info);
        },

        show(pane, key) {
            return view.show(pane, key, tiles.describe(key));
        },

        setFeatures(pane, json) {
            overlay(pane).setFeatures(JSON.parse(json));
        },

        select(pane, key) {
            overlay(pane).select(key);
        },

        zoomTo(pane, key) {
            return overlay(pane).zoomTo(key);
        },

        fitTo(area) {
            view.fitTo(area);
        },

        dispose() {
            for (const o of Object.values(overlays)) o.dispose();
            view.dispose();
            tiles.dispose();
        }
    };
}
