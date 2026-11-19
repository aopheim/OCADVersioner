/* A web map under an OCAD map, and the menu that picks it.
 *
 * The OCAD map stays in paper millimetres (L.CRS.Simple) - the change mask, the
 * synced panes and zoom-to-changes all depend on that - so the web map is brought
 * to the paper rather than the other way round. Each background tile is a canvas
 * over a rectangle of paper; it works out which web tiles lie under that rectangle
 * and draws them in, scaled and rotated by the map's own georeference: an affine
 * transform from paper millimetres to the web map's tile grid, read from the .ocd
 * file. No-one types in a coordinate system.
 *
 * Web maps come on different tile grids - most on Web Mercator, mapant.no on UTM
 * zone 33 - so a source names its grid, and the page is given the paper's place
 * on each grid. The grid names match OcadViewer.Core's Georeference.Grids.
 *
 * Fading the OCAD map is how the background shows through: the OCAD map is drawn
 * on opaque white paper, so a background under it is otherwise hidden wherever the
 * map is. This module owns the slider but not the OCAD map: it says what opacity
 * the map should have, and whoever draws the map applies it.
 *
 * Web tiles need the internet, and the app is meant to work without it. Offline,
 * the menu is disabled and says why, and a background that was on goes off.
 *
 * This module knows Leaflet and the tile servers, nothing else. It is given a map
 * and told the georeference of whatever is drawn on it.
 */

/** Web Mercator's half-width, in metres: the world runs from -R to R both ways. */
const R = 20037508.342789244;

/** Pixels along a side of one background canvas, the Leaflet tile it fills. */
const CANVAS_TILE = 256;

/** How many web tiles one background canvas may draw before a coarser zoom is used. */
const MAX_WEB_TILES_PER_CANVAS = 36;

/** Decoded web tile pixels kept per pane, so neighbouring canvases share them:
 * 400 tiles of 256 x 256, or 25 of mapant.no's 1024 x 1024. */
const IMAGE_CACHE_PIXELS = 400 * 256 * 256;

/** The background when one is first picked: half, so both maps can be seen at once. */
const FIRST_OPACITY = 0.5;

/**
 * A tile grid: where its tile (0, 0) starts, how big a tile is, and how many
 * metres one pixel covers at each zoom. Rows count down from the top.
 *
 * Web Mercator is the grid of OpenStreetMap and nearly every web map: 256-pixel
 * tiles, the whole world in one tile at zoom 0, halving each zoom.
 */
const WEB_MERCATOR = Object.freeze({
    name: 'EPSG:3857',
    originX: -R,
    originY: R,
    tileSize: 256,
    metresPerPixel: z => (2 * R) / (256 * 2 ** z),
    columns: z => 2 ** z,
    rows: z => 2 ** z
});

/* mapant.no's own grid, from https://mapant.no/wmts/wmts.xml. Its scale
 * denominators work out to 1000 m per pixel at zoom 0, halving each zoom, so a
 * 1024-pixel tile is exactly 1 km at zoom 10, its finest. */
const MAPANT_COLUMNS = Object.freeze([3, 3, 5, 12, 25, 50, 100, 200, 400, 800, 1600]);
const MAPANT_ROWS = Object.freeze([5, 8, 15, 30, 60, 120, 240, 480, 960, 1920, 3840]);

/** UTM zone 33 on WGS84, as mapant.no tiles it. */
const MAPANT_UTM33 = Object.freeze({
    name: 'EPSG:32633',
    originX: -150000,
    originY: 10000000,
    tileSize: 1024,
    metresPerPixel: z => 1000 / 2 ** z,
    columns: z => MAPANT_COLUMNS[z],
    rows: z => MAPANT_ROWS[z]
});

/**
 * The web maps on offer, in menu order. Norge i bilder is to come.
 *
 * Kartverket's topographic map covers Norway only; outside it the tiles are
 * blank. Its cache takes row before column ({z}/{y}/{x}) and stops at zoom 18,
 * which is about 0.6 m per pixel at 63°N - finer than any orienteering map needs.
 * Free, no key, CC BY 4.0.
 *
 * mapant.no is an orienteering map drawn by a program from Kartverket's laser
 * scans, Norway only, and not yet all of it. Its tiles are 1024 pixels and about
 * half a megabyte each, down to 1 m per pixel. Its WMTS says CC BY 4.0; its web
 * page says the licence is not settled but allows non-commercial orienteering use.
 */
const SOURCES = Object.freeze({
    osm: Object.freeze({
        name: 'OpenStreetMap',
        grid: WEB_MERCATOR,
        url: (z, x, y) => `https://tile.openstreetmap.org/${z}/${x}/${y}.png`,
        maxZoom: 19,
        attribution: '&copy; <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noopener">OpenStreetMap</a> contributors'
    }),
    kartverketTopo: Object.freeze({
        name: 'Kartverket topo',
        grid: WEB_MERCATOR,
        url: (z, x, y) => `https://cache.kartverket.no/v1/wmts/1.0.0/topo/default/webmercator/${z}/${y}/${x}.png`,
        maxZoom: 18,
        attribution: '&copy; <a href="https://www.kartverket.no/" target="_blank" rel="noopener">Kartverket</a> (CC BY 4.0)'
    }),
    mapant: Object.freeze({
        name: 'MapAnt',
        grid: MAPANT_UTM33,
        url: (z, x, y) => `https://mapant.no/tiles/${z}/${y}/${x}.png`,
        maxZoom: 10,
        attribution: '&copy; <a href="https://mapant.no/" target="_blank" rel="noopener">MapAnt.no</a>, '
            + 'data &copy; Kartverket, Geovekst and the municipalities'
    })
});

/** No background. */
const NONE = 'none';

/** The words the menu shows. The page passes its own, in its own language. */
const DEFAULT_LABELS = Object.freeze({
    background: 'Background map',
    none: 'None',
    opacity: 'Map opacity',
    noGeoreference: 'This map has no georeference, so it cannot be placed on a background map',
    offline: 'No internet connection'
});

/**
 * Whether a paper-to-grid transform can lay out a web map: six finite numbers
 * whose determinant is not zero, so it can be inverted. The same rule as
 * PaperTransform.IsUsable on the .NET side, repeated so this module never trusts
 * what it is handed.
 */
export function isUsable(t) {
    if (!Array.isArray(t) || t.length !== 6 || !t.every(Number.isFinite)) return false;
    const det = t[0] * t[4] - t[1] * t[3];
    return Number.isFinite(det) && det !== 0;
}

/**
 * The 2D affine transform from web tile image pixels to background canvas pixels.
 *
 * @param t the map's paper-to-grid transform [a, b, c, d, e, f] for this grid
 * @param paper the canvas's rectangle of paper {minX, maxY} and mm per pixel k
 * @param grid the tile grid the web tile is on
 * @returns [a, b, c, d, e, f] in the order canvas setTransform takes them
 */
export function webTileToCanvas(t, paper, grid, z, tx, ty) {
    const [a, b, c, d, e, f] = t;
    const det = a * e - b * d;
    const res = grid.metresPerPixel(z);

    // Web tile pixel -> grid metres -> paper -> canvas pixel.
    const toCanvas = (i, j) => {
        const gx = grid.originX + (tx * grid.tileSize + i) * res - c;
        const gy = grid.originY - (ty * grid.tileSize + j) * res - f;
        const px = (e * gx - b * gy) / det;
        const py = (-d * gx + a * gy) / det;
        return [(px - paper.minX) / paper.k, (paper.maxY - py) / paper.k];
    };

    const [u0, v0] = toCanvas(0, 0);
    const [ui, vi] = toCanvas(1, 0);
    const [uj, vj] = toCanvas(0, 1);
    return [ui - u0, vi - v0, uj - u0, vj - v0, u0, v0];
}

/**
 * Which web tiles lie under a rectangle of paper, and at what zoom: the
 * coarsest zoom whose pixels are no larger than the canvas's, so the
 * background is as sharp as the screen without fetching more than it shows.
 *
 * @param grid the tile grid to look in
 * @returns {{z: number, tiles: {x: number, y: number}[]}}
 */
export function webTilesUnder(t, paper, size, grid, maxZoom) {
    const [a, b, c, d, e, f] = t;
    const corners = [
        [paper.minX, paper.maxY], [paper.minX + size * paper.k, paper.maxY],
        [paper.minX, paper.maxY - size * paper.k], [paper.minX + size * paper.k, paper.maxY - size * paper.k]
    ].map(([x, y]) => [a * x + b * y + c, d * x + e * y + f]);

    const xs = corners.map(p => p[0]), ys = corners.map(p => p[1]);
    const minGx = Math.min(...xs), maxGx = Math.max(...xs);
    const minGy = Math.min(...ys), maxGy = Math.max(...ys);

    const metresPerPixel = paper.k * Math.sqrt(Math.abs(a * e - b * d));
    let z = Math.ceil(Math.log2(grid.metresPerPixel(0) / metresPerPixel));
    z = Math.max(0, Math.min(maxZoom, z));

    for (; z >= 0; z--) {
        const span = grid.tileSize * grid.metresPerPixel(z);
        const columns = grid.columns(z), rows = grid.rows(z);
        const x0 = Math.max(0, Math.floor((minGx - grid.originX) / span));
        const x1 = Math.min(columns - 1, Math.floor((maxGx - grid.originX) / span));
        const y0 = Math.max(0, Math.floor((grid.originY - maxGy) / span));
        const y1 = Math.min(rows - 1, Math.floor((grid.originY - minGy) / span));

        // Entirely off the grid: nothing to draw at any zoom.
        if (x0 > x1 || y0 > y1) return { z, tiles: [] };

        if ((x1 - x0 + 1) * (y1 - y0 + 1) > MAX_WEB_TILES_PER_CANVAS && z > 0)
            continue;

        const tiles = [];
        for (let y = y0; y <= y1; y++)
            for (let x = x0; x <= x1; x++)
                tiles.push({ x, y });
        return { z, tiles };
    }
    return { z: 0, tiles: [] };
}

/**
 * Puts a background menu and layer on one Leaflet map.
 *
 * @param map the Leaflet map, in paper millimetres
 * @param options {minZoom, maxZoom, onOpacity} the paper zoom range, the same as
 *        the OCAD layer's, and a function told the opacity (0 to 1) the OCAD map
 *        should now have, so the background shows through it
 * @returns {{setGeoreference: Function, setLabels: Function, dispose: Function}}
 */
export function createBackground(map, options) {
    let labels = { ...DEFAULT_LABELS };
    /* Where the map on this pane is: paper-to-grid transforms keyed by grid name,
     * null for a map with no georeference, undefined while the pane has no map. */
    let georeference;
    let source = NONE;
    let opacity = 1;
    let layer = null;
    let attribution = null;

    // Below the OCAD map's tile pane (z-index 200), above nothing.
    const pane = map.createPane('backgroundPane');
    pane.style.zIndex = 150;
    pane.style.pointerEvents = 'none';

    /* Decoded web tiles, newest last, shared by every canvas on this pane. The
     * browser's HTTP cache would refetch nothing anyway, but decoding the same
     * PNG for each of the four canvases it straddles is wasted work. Bounded by
     * pixels rather than count, since one mapant.no tile is sixteen OSM tiles. */
    const images = new Map();
    let cachedPixels = 0;

    function image(url, tileSize) {
        const held = images.get(url);
        if (held) {
            images.delete(url);
            images.set(url, held);
            return held.loading;
        }

        const entry = { pixels: tileSize * tileSize, loading: null };
        entry.loading = new Promise(resolve => {
            const img = new Image();
            img.decoding = 'async';
            img.onload = () => resolve(img);
            img.onerror = () => { forget(url, entry); resolve(null); };
            img.src = url;
        });
        images.set(url, entry);
        cachedPixels += entry.pixels;
        while (cachedPixels > IMAGE_CACHE_PIXELS && images.size > 1) {
            const [oldest, held] = images.entries().next().value;
            forget(oldest, held);
        }
        return entry.loading;
    }

    /** Drops a cached tile, if the cache still holds this one under its URL. */
    function forget(url, entry) {
        if (images.get(url) !== entry) return;
        images.delete(url);
        cachedPixels -= entry.pixels;
    }

    const BackgroundLayer = L.GridLayer.extend({
        createTile(coords, done) {
            const size = this.getTileSize();
            const canvas = document.createElement('canvas');
            canvas.width = size.x;
            canvas.height = size.y;

            const web = SOURCES[source];
            const t = web && georeference?.[web.grid.name];
            if (!t) { setTimeout(() => done(null, canvas)); return canvas; }

            const nw = map.unproject(coords.scaleBy(size), coords.z);
            const se = map.unproject(coords.scaleBy(size).add(size), coords.z);
            const paper = { minX: nw.lng, maxY: nw.lat, k: (se.lng - nw.lng) / size.x };

            const { grid } = web;
            const { z, tiles } = webTilesUnder(t, paper, size.x, grid, web.maxZoom);
            Promise.all(tiles.map(({ x, y }) => image(web.url(z, x, y), grid.tileSize).then(img => ({ x, y, img }))))
                .then(loaded => {
                    const ctx = canvas.getContext('2d');
                    ctx.imageSmoothingQuality = 'high';
                    for (const { x, y, img } of loaded) {
                        if (!img) continue;
                        ctx.setTransform(...webTileToCanvas(t, paper, grid, z, x, y));
                        // Half a pixel of overlap, so a rotated tile's anti-aliased
                        // edge does not leave a hairline of its neighbour's absence.
                        ctx.drawImage(img, -0.5, -0.5, grid.tileSize + 1, grid.tileSize + 1);
                    }
                    done(null, canvas);
                });
            return canvas;
        }
    });

    /* ------------------------------------------------------------ the menu */

    let control = null;
    let elements = null;

    const Menu = L.Control.extend({
        // Under the zoom buttons: the page puts its own controls in the top
        // right of a pane, such as the file picker on /viewer.
        options: { position: 'topleft' },

        onAdd() {
            const box = L.DomUtil.create('div', 'leaflet-bar leaflet-control backgroundMenu');
            L.DomEvent.disableClickPropagation(box);
            L.DomEvent.disableScrollPropagation(box);

            const button = L.DomUtil.create('a', 'backgroundMenu-button', box);
            button.href = '#';
            button.setAttribute('role', 'button');
            button.innerHTML = '<i class="bi bi-stack"></i>';

            const panel = L.DomUtil.create('div', 'backgroundMenu-panel', box);
            panel.hidden = true;

            const heading = L.DomUtil.create('div', 'backgroundMenu-heading', panel);
            const choices = L.DomUtil.create('div', 'backgroundMenu-choices', panel);
            const opacityRow = L.DomUtil.create('label', 'backgroundMenu-opacity', panel);
            const opacityText = L.DomUtil.create('span', '', opacityRow);
            const slider = L.DomUtil.create('input', 'form-range', opacityRow);
            slider.type = 'range';
            slider.min = '0';
            slider.max = '100';
            slider.step = '5';

            L.DomEvent.on(button, 'click', event => {
                L.DomEvent.preventDefault(event);
                if (!available()) return;
                panel.hidden = !panel.hidden;
                render();
            });

            L.DomEvent.on(slider, 'input', () => setOpacity(Number(slider.value) / 100));

            elements = { box, button, panel, heading, choices, opacityRow, opacityText, slider };
            render();
            return box;
        }
    });

    /**
     * Why the menu cannot be used just now, or null if it can. An empty pane
     * has nothing to explain: there is simply nothing to put a background under.
     */
    function unavailableBecause() {
        if (georeference === undefined) return labels.background;
        if (!navigator.onLine) return labels.offline;
        if (!georeference) return labels.noGeoreference;
        return null;
    }

    const available = () => unavailableBecause() === null;

    /** Whether the map on this pane can be placed on a source's tile grid. */
    const onGrid = id => Boolean(georeference?.[SOURCES[id].grid.name]);

    function render() {
        if (!elements) return;
        const { box, button, panel, heading, choices, opacityRow, opacityText, slider } = elements;

        const reason = unavailableBecause();
        box.classList.toggle('disabled', reason !== null);
        button.title = reason ?? labels.background;
        button.setAttribute('aria-label', reason ?? labels.background);
        button.setAttribute('aria-disabled', String(reason !== null));
        button.setAttribute('aria-expanded', String(!panel.hidden));
        if (reason) panel.hidden = true;

        heading.textContent = labels.background;

        choices.replaceChildren(...[NONE, ...Object.keys(SOURCES)].map(id => {
            const row = document.createElement('label');
            row.className = 'form-check';
            const radio = document.createElement('input');
            radio.type = 'radio';
            radio.className = 'form-check-input';
            radio.name = `background-${L.stamp(map)}`;
            radio.checked = id === source;
            radio.disabled = id !== NONE && !onGrid(id);
            radio.addEventListener('change', () => choose(id));
            const text = document.createElement('span');
            text.className = 'form-check-label';
            text.textContent = id === NONE ? labels.none : SOURCES[id].name;
            row.append(radio, text);
            return row;
        }));

        opacityRow.hidden = source === NONE;
        opacityText.textContent = labels.opacity;
        slider.value = String(Math.round(opacity * 100));
    }

    /* -------------------------------------------------------------- state */

    function setOpacity(value) {
        opacity = value;
        options.onOpacity?.(value);
    }

    function choose(id) {
        const next = id in SOURCES && available() && onGrid(id) ? id : NONE;
        if (next === source) { render(); return; }

        const wasNone = source === NONE;
        source = next;

        layer?.remove();
        layer = null;
        attribution?.remove();
        attribution = null;

        if (source !== NONE) {
            layer = new BackgroundLayer({
                pane: 'backgroundPane',
                tileSize: CANVAS_TILE,
                minZoom: options.minZoom,
                maxZoom: options.maxZoom,
                updateWhenZooming: false,
                attribution: SOURCES[source].attribution
            }).addTo(map);
            attribution = L.control.attribution({ prefix: false }).addTo(map);
            attribution.addAttribution(SOURCES[source].attribution);
            if (wasNone) setOpacity(FIRST_OPACITY);
        } else {
            setOpacity(1);
        }
        render();
    }

    const onConnectivity = () => {
        if (!navigator.onLine) choose(NONE);
        render();
    };
    window.addEventListener('online', onConnectivity);
    window.addEventListener('offline', onConnectivity);

    control = new Menu().addTo(map);

    return {
        /**
         * Tells the background where the map now on this pane is.
         *
         * @param transforms paper-to-grid [a, b, c, d, e, f] keyed by grid name,
         *          or null when the map has no georeference - which switches the
         *          background off. A transform with a non-finite number or no
         *          inverse is dropped: it would draw unrelated web tiles in the
         *          canvas corners. A source on a grid the map lacks cannot be picked.
         */
        setGeoreference(transforms) {
            const usable = Object.entries(transforms ?? {}).filter(([, t]) => isUsable(t));
            const next = usable.length ? Object.fromEntries(usable.map(([grid, t]) => [grid, [...t]])) : null;
            if (next && georeference && JSON.stringify(next) === JSON.stringify(georeference)) return;

            georeference = next;
            if (!georeference || (source !== NONE && !onGrid(source))) choose(NONE);
            else layer?.redraw();
            render();
        },

        /** The menu's words, in the page's language. */
        setLabels(words) {
            labels = { ...DEFAULT_LABELS, ...words };
            render();
        },

        dispose() {
            window.removeEventListener('online', onConnectivity);
            window.removeEventListener('offline', onConnectivity);
            layer?.remove();
            attribution?.remove();
            control?.remove();
            images.clear();
        }
    };
}
