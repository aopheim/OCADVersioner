/* The merge's changes, drawn as outlines over one Leaflet map.
 *
 * Plain Leaflet and nothing else: no .NET, no tiles, no idea what a branch is.
 * Each feature arrives already classified - whose change it is, what kind, how
 * it was decided - and this module only turns that into strokes and fills. The
 * meaning lives in C# (MergeOverlay.cs); the look lives here.
 *
 * Coordinates are paper millimetres, which are Leaflet coordinates under
 * L.CRS.Simple: lat = Y, lng = X.
 */

/* Branch colours. The map underneath is greyed while merging, so these never
 * fight map colours; crimson is the app's existing change-overlay colour. */
const COLOURS = { a: '#0E8A93', b: '#E26A0A', both: '#475467', conflict: '#D7003A' };

const toLatLngs = part => part.map(([x, y]) => [y, x]);

function styleFor(feature, selected) {
    const color = COLOURS[feature.side] ?? COLOURS.conflict;
    const rejected = feature.state === 'rejected';
    const style = {
        color: rejected ? '#98A2B3' : color,
        weight: selected ? 6 : feature.side === 'conflict' ? 4 : 3,
        opacity: 1,
        fill: feature.kind === 'area' || feature.kind === 'box',
        fillColor: color,
        fillOpacity: feature.kind === 'box' ? 0 : feature.side === 'conflict' ? 0.25 : 0.12,
        dashArray: null
    };

    if (rejected) style.dashArray = '2 6';
    else if (feature.kind === 'box') style.dashArray = '10 6';
    else if (feature.change === 'changed') style.dashArray = '9 5';
    else if (feature.change === 'deleted') style.dashArray = '3 5';

    return style;
}

function layerFor(feature, selected) {
    const style = styleFor(feature, selected);
    const parts = feature.parts.map(toLatLngs);

    switch (feature.kind) {
        case 'point':
            return L.circleMarker(parts[0][0], { ...style, radius: feature.change === 'deleted' ? 9 : 7, fill: true, fillOpacity: 0.6 });
        case 'line':
            return L.polyline(parts, style);
        case 'area':
            return L.polygon(parts, style);
        case 'box':
            return L.rectangle(parts[0], style);
        default:
            throw new Error(`Unknown feature kind "${feature.kind}".`);
    }
}

/**
 * Draws features over a map and reports which one the user clicked.
 *
 * @param map    a Leaflet map
 * @param onPick (key) => void, when a feature is clicked
 */
export function createChangeOverlay(map, onPick) {
    const group = L.featureGroup().addTo(map);
    const drawn = new Map();
    let features = [];
    let selectedKey = null;

    /* An item can have several outlines - an open conflict draws base's shape and
     * both branches' versions - so a key collects layers rather than owning one. */
    function draw(feature) {
        const selected = feature.key === selectedKey;
        const layer = layerFor(feature, selected).on('click', () => onPick(feature.key));
        const layers = drawn.get(feature.key) ?? [];
        layers.push(layer);
        drawn.set(feature.key, layers);
        group.addLayer(layer);
        if (selected) layer.bringToFront();
    }

    function redraw() {
        group.clearLayers();
        drawn.clear();
        // Boxes underneath, so the changes inside them stay clickable.
        for (const f of features.filter(f => f.kind === 'box')) draw(f);
        for (const f of features.filter(f => f.kind !== 'box')) draw(f);
    }

    return {
        setFeatures(next) {
            features = next;
            redraw();
        },

        select(key) {
            selectedKey = key;
            redraw();
        },

        /** Zooms to a feature's extent, padded so its surroundings show. */
        zoomTo(key, paddingPx = 60) {
            const layers = drawn.get(key);
            if (!layers?.length) return false;
            const bounds = L.latLngBounds([]);
            for (const layer of layers)
                bounds.extend(layer.getBounds ? layer.getBounds() : L.latLngBounds([layer.getLatLng()]));
            map.fitBounds(bounds, { animate: false, padding: [paddingPx, paddingPx] });
            return true;
        },

        dispose() {
            group.remove();
            drawn.clear();
        }
    };
}
