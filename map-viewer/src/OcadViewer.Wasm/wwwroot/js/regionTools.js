/* Regions drawn over one Leaflet map, with Leaflet-Geoman.
 *
 * Leaflet and Geoman only: no .NET, no idea what a merge is. A region is a
 * polygon with an id; this module draws, edits and removes them and reports the
 * whole set whenever it changes. Rectangles and polygons are both reported as a
 * ring of [x, y] in paper millimetres (lng = X, lat = Y under L.CRS.Simple).
 *
 * Geoman is switched to opt-in, so it only ever handles the layers created here
 * and never the tiles or change outlines on the same map.
 */

const COLOURS = { a: '#0E8A93', b: '#E26A0A' };

const ringOf = layer => layer.getLatLngs()[0].map(p => [p.lng, p.lat]);

/**
 * @param map       a Leaflet map
 * @param onChange  (regions) => void, regions = [{ id, ring }], after every draw, edit or removal
 * @param onSelect  (id) => void, when a region is clicked
 * @param onDrawing (active) => void, when drawing starts and ends - so whoever owns
 *                  other clickable layers can ignore the clicks that place corners
 */
export function createRegionTools(map, onChange, onSelect, onDrawing = () => {}) {
    L.PM.setOptIn(true);
    const regions = new Map();
    let counter = 0;
    let editing = null;

    function report() {
        onChange([...regions].map(([id, layer]) => ({ id, ring: ringOf(layer) })));
    }

    function style(side) {
        const color = COLOURS[side] ?? '#344054';
        // Outline only: a filled region would swallow clicks on the changes inside it.
        return { color, weight: 3, dashArray: side ? null : '8 6', fill: false };
    }

    function adopt(id, layer, label, side) {
        layer.setStyle(style(side));
        layer.options.pmIgnore = false;
        L.PM.reInitLayer(layer);
        layer.bindTooltip(label ?? id, { permanent: true, direction: 'center', className: 'region-label' });
        layer.on('click', () => { edit(id); onSelect(id); });
        layer.on('pm:edit', report);
        regions.set(id, layer);
    }

    function edit(id) {
        if (editing && editing !== id) regions.get(editing)?.pm.disable();
        editing = id;
        regions.get(id)?.pm.enable({ allowSelfIntersection: false });
    }

    function onCreate(e) {
        map.pm.disableDraw();
        onDrawing(false);
        // Regions restored from an earlier visit already hold some ids.
        let id;
        do id = `r${++counter}`; while (regions.has(id));
        adopt(id, e.layer, id.toUpperCase(), null);
        report();
        onSelect(id);
    }

    map.on('pm:create', onCreate);

    return {
        /** Lets the user draw one region: 'Rectangle' or 'Polygon'. */
        draw(shape) {
            onDrawing(true);
            map.pm.enableDraw(shape, { pmIgnore: false, snappable: false, templineStyle: style(null), pathOptions: style(null) });
        },

        cancel() {
            map.pm.disableDraw();
            onDrawing(false);
            if (editing) regions.get(editing)?.pm.disable();
            editing = null;
        },

        /**
         * Labels and colours, decided elsewhere, and regions added from outside
         * (suggestions, a restored merge): [{ id, ring?, label, side }].
         */
        describe(list) {
            for (const r of list) {
                let layer = regions.get(r.id);
                if (!layer && r.ring) {
                    layer = L.polygon(r.ring.map(([x, y]) => [y, x])).addTo(map);
                    adopt(r.id, layer, r.label, r.side);
                    continue;
                }
                if (!layer) continue;
                layer.setStyle(style(r.side));
                layer.setTooltipContent(r.label);
            }
        },

        remove(id) {
            const layer = regions.get(id);
            if (!layer) return;
            if (editing === id) editing = null;
            layer.remove();
            regions.delete(id);
            report();
        },

        dispose() {
            map.off('pm:create', onCreate);
            map.pm.disableDraw();
            for (const layer of regions.values()) layer.remove();
            regions.clear();
        }
    };
}
