/* A web map under an OCAD map, and the menu that picks it.
 *
 * The OCAD map stays in paper millimetres (L.CRS.Simple) - the change mask, the
 * synced panes and zoom-to-changes all depend on that - so the web map is brought
 * to the paper rather than the other way round. Each background tile is a canvas
 * over a rectangle of paper; it works out which web tiles lie under that rectangle
 * and draws them in, scaled and rotated by the map's own georeference: an affine
 * transform from paper millimetres to Web Mercator metres, read from the .ocd file.
 * No-one types in a coordinate system.
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

/** Pixels along a web tile's side. */
const WEB_TILE = 256;

/** How many web tiles one background canvas may draw before a coarser zoom is used. */
const MAX_WEB_TILES_PER_CANVAS = 36;

/** Web tiles kept decoded per pane, so neighbouring canvases share them. */
const IMAGE_CACHE_SIZE = 400;

/** The background when one is first picked: half, so both maps can be seen at once. */
const FIRST_OPACITY = 0.5;

/**
 * The web maps on offer. Only OpenStreetMap so far; Kartverket's topographic map,
 * Norge i bilder and mapant.no are to come.
 */
const SOURCES = Object.freeze({
    osm: Object.freeze({
        name: 'OpenStreetMap',
        url: (z, x, y) => `https://tile.openstreetmap.org/${z}/${x}/${y}.png`,
        maxZoom: 19,
        attribution: '&copy; <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noopener">OpenStreetMap</a> contributors'
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
 * Whether a paper-to-Mercator transform can lay out a web map: six finite numbers
 * whose determinant is not zero, so it can be inverted. The same rule as
 * PaperToMercator.IsUsable on the .NET side, repeated so this module never trusts
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
 * @param t the map's paper-to-Mercator transform [a, b, c, d, e, f]
 * @param paper the canvas's rectangle of paper {minX, maxY} and mm per pixel k
 * @returns [a, b, c, d, e, f] in the order canvas setTransform takes them
 */
export function webTileToCanvas(t, paper, z, tx, ty) {
    const [a, b, c, d, e, f] = t;
    const det = a * e - b * d;
    const res = (2 * R) / (WEB_TILE * 2 ** z);

    // Web tile pixel -> Mercator -> paper -> canvas pixel.
    const toCanvas = (i, j) => {
        const mx = -R + (tx * WEB_TILE + i) * res - c;
        const my = R - (ty * WEB_TILE + j) * res - f;
        const px = (e * mx - b * my) / det;
        const py = (-d * mx + a * my) / det;
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
 * @returns {{z: number, tiles: {x: number, y: number}[]}}
 */
export function webTilesUnder(t, paper, size, maxZoom) {
    const [a, b, c, d, e, f] = t;
    const corners = [
        [paper.minX, paper.maxY], [paper.minX + size * paper.k, paper.maxY],
        [paper.minX, paper.maxY - size * paper.k], [paper.minX + size * paper.k, paper.maxY - size * paper.k]
    ].map(([x, y]) => [a * x + b * y + c, d * x + e * y + f]);

    const xs = corners.map(p => p[0]), ys = corners.map(p => p[1]);
    const minMx = Math.min(...xs), maxMx = Math.max(...xs);
    const minMy = Math.min(...ys), maxMy = Math.max(...ys);

    const metresPerPixel = paper.k * Math.sqrt(Math.abs(a * e - b * d));
    let z = Math.ceil(Math.log2((2 * R) / (WEB_TILE * metresPerPixel)));
    z = Math.max(0, Math.min(maxZoom, z));

    for (; z >= 0; z--) {
        const n = 2 ** z, span = (2 * R) / n;
        const clamp = v => Math.max(0, Math.min(n - 1, v));
        const x0 = clamp(Math.floor((minMx + R) / span)), x1 = clamp(Math.floor((maxMx + R) / span));
        const y0 = clamp(Math.floor((R - maxMy) / span)), y1 = clamp(Math.floor((R - minMy) / span));

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
    /* Where the map on this pane is: paper-to-Mercator, null for a map with no
     * georeference, undefined while the pane has no map at all. */
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
     * PNG for each of the four canvases it straddles is wasted work. */
    const images = new Map();

    function image(url) {
        const held = images.get(url);
        if (held) {
            images.delete(url);
            images.set(url, held);
            return held;
        }

        const loading = new Promise(resolve => {
            const img = new Image();
            img.decoding = 'async';
            img.onload = () => resolve(img);
            img.onerror = () => { images.delete(url); resolve(null); };
            img.src = url;
        });
        images.set(url, loading);
        while (images.size > IMAGE_CACHE_SIZE)
            images.delete(images.keys().next().value);
        return loading;
    }

    const BackgroundLayer = L.GridLayer.extend({
        createTile(coords, done) {
            const size = this.getTileSize();
            const canvas = document.createElement('canvas');
            canvas.width = size.x;
            canvas.height = size.y;

            const t = georeference;
            const web = SOURCES[source];
            if (!t || !web) { setTimeout(() => done(null, canvas)); return canvas; }

            const nw = map.unproject(coords.scaleBy(size), coords.z);
            const se = map.unproject(coords.scaleBy(size).add(size), coords.z);
            const paper = { minX: nw.lng, maxY: nw.lat, k: (se.lng - nw.lng) / size.x };

            const { z, tiles } = webTilesUnder(t, paper, size.x, web.maxZoom);
            Promise.all(tiles.map(({ x, y }) => image(web.url(z, x, y)).then(img => ({ x, y, img }))))
                .then(loaded => {
                    const ctx = canvas.getContext('2d');
                    ctx.imageSmoothingQuality = 'high';
                    for (const { x, y, img } of loaded) {
                        if (!img) continue;
                        ctx.setTransform(...webTileToCanvas(t, paper, z, x, y));
                        // Half a pixel of overlap, so a rotated tile's anti-aliased
                        // edge does not leave a hairline of its neighbour's absence.
                        ctx.drawImage(img, -0.5, -0.5, WEB_TILE + 1, WEB_TILE + 1);
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
        const next = id in SOURCES && available() ? id : NONE;
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
                tileSize: WEB_TILE,
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
         * @param t paper-to-Mercator [a, b, c, d, e, f], or null when the map has
         *          no georeference - which switches the background off. A transform
         *          with a non-finite number or no inverse counts as none: it would
         *          draw unrelated web tiles in the canvas corners.
         */
        setGeoreference(t) {
            if (t && !isUsable(t)) t = null;
            const same = t && georeference && t.every((v, i) => v === georeference[i]);
            if (same) return;

            georeference = t ? [...t] : null;
            if (!georeference) choose(NONE);
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
