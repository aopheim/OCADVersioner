/* The merge screen's composition root: tiles, panes, change outlines, regions.
 *
 * Like mapWorkspace.js for the version comparison, this is the one module on the
 * merge screen that knows about several others at once. C# hands it the files'
 * bytes and the classified changes; it parses through the tile source, shows a
 * map per pane, draws the changes over it and lets the user draw regions.
 *
 * Panes are named by the caller - one for the merge screen, three for the
 * conflict walker - and the names are passed through untouched.
 */
import { createTileSource } from './mapTiles.js';
import { createMaps } from './leafletViewport.js';
import { createChangeOverlay } from './mergeOverlay.js';
import { createRegionTools } from './regionTools.js';

/**
 * @param elements {paneName: element}
 * @param dotnet   a DotNetObjectReference with [JSInvokable] Pick(pane, key),
 *                 RegionsChanged(pane, json) and RegionPicked(pane, id)
 */
export async function createMergeWorkspace(elements, dotnet) {
    const tiles = await createTileSource();
    const view = createMaps(elements, tiles.render);
    const panes = Object.keys(elements);
    const overlays = Object.fromEntries(panes.map(pane => [
        pane,
        createChangeOverlay(view.leaflet(pane), key => dotnet.invokeMethodAsync('Pick', pane, key))
    ]));
    // Region tools only where somebody asks for them: the walker never does.
    const regionTools = {};

    function overlay(pane) {
        const found = overlays[pane];
        if (!found) throw new Error(`Unknown pane "${pane}".`);
        return found;
    }

    function regions(pane) {
        overlay(pane);
        return regionTools[pane] ??= createRegionTools(
            view.leaflet(pane),
            list => dotnet.invokeMethodAsync('RegionsChanged', pane, JSON.stringify(list)),
            id => dotnet.invokeMethodAsync('RegionPicked', pane, id),
            // The click that places a corner must not also pick the change under it.
            drawing => overlay(pane).mute(drawing));
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

        drawRegion(pane, shape) {
            regions(pane).draw(shape);
        },

        cancelRegion(pane) {
            regions(pane).cancel();
        },

        describeRegions(pane, json) {
            regions(pane).describe(JSON.parse(json));
        },

        removeRegion(pane, id) {
            regions(pane).remove(id);
        },

        dispose() {
            for (const tools of Object.values(regionTools)) tools.dispose();
            for (const o of Object.values(overlays)) o.dispose();
            view.dispose();
            tiles.dispose();
        }
    };
}
