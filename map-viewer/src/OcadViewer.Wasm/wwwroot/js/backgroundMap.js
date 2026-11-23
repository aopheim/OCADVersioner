/* A web map under an OCAD map, and the menu that picks it.
 *
 * Deliberately thin. Which web maps there are, which of their tiles lie under a
 * square of ground and how each must be scaled and rotated to land in place are
 * all worked out in .NET (OcadViewer.Core's BackgroundMaps). This module is
 * handed the list of sources and a function that answers "what goes on this
 * canvas?"; it fetches the images and draws them where it is told.
 *
 * The map shows the ground in Web Mercator, so a canvas is given to .NET as its
 * top-left corner in Web Mercator metres and the metres each pixel spans.
 *
 * Which web map is wanted is remembered apart from which one is shown: when the
 * pane has no ground to lay it on - its maps are shown on paper - nothing is
 * shown, and the wanted one comes back as soon as there is ground again.
 *
 * Fading the OCAD map is how the background shows through: the OCAD map is drawn
 * on opaque white paper, so a background under it is otherwise hidden wherever the
 * map is. This module owns the slider but not the OCAD map: it says what opacity
 * the map should have, and whoever draws the map applies it.
 *
 * Web tiles need the internet, and the app is meant to work without it. Offline,
 * the menu is disabled and says why, and a background that was on goes off.
 */

/** Pixels along a side of one background canvas, the Leaflet tile it fills. */
const CANVAS_TILE = 256;

/** Decoded web tile pixels kept per pane, so neighbouring canvases share them:
 * 400 tiles of 256 x 256, or 25 of mapant.no's 1024 x 1024. */
const IMAGE_CACHE_PIXELS = 400 * 256 * 256;

/** The background when one is first picked: half, so both maps can be seen at once. */
const FIRST_OPACITY = 0.5;

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
 * Puts a background menu and layer on one Leaflet map.
 *
 * @param map the Leaflet map, in Web Mercator
 * @param options {minZoom, maxZoom, sources, initialSource, onOpacity, onChoose}:
 *        the zoom range, the same as the OCAD layer's; the web maps on offer as
 *        [{id, name, attribution}], in menu order; the one wanted until the
 *        mapper picks another; a function told the opacity (0 to 1) the OCAD map
 *        should now have; and one told the id the mapper picked in this menu
 * @returns {{setMap: Function, select: Function, setLabels: Function, dispose: Function}}
 */
export function createBackground(map, options) {
    const sources = new Map(options.sources.map(s => [s.id, s]));
    let labels = { ...DEFAULT_LABELS };
    /* The ids of the sources there is ground for: null when the pane shows
     * paper, undefined while the pane has no map at all. */
    let available;
    /* (sourceId, minX, maxY, metresPerPixel, pixels) => [{url, size, transform}]. */
    let tilesFor = null;
    /* The web map the mapper wants, shown whenever there is ground for it. */
    let wanted = sources.has(options.initialSource) ? options.initialSource : NONE;
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

    function image(url, size) {
        const held = images.get(url);
        if (held) {
            images.delete(url);
            images.set(url, held);
            return held.loading;
        }

        const entry = { pixels: size * size, loading: null };
        entry.loading = new Promise(resolve => {
            const img = new Image();
            img.decoding = 'async';
            /* OpenStreetMap refuses tile requests with no Referer, and Azure Static
             * Web Apps serves the page with Referrer-Policy: same-origin, which
             * sends none across origins. Ask for the browser default on these
             * requests alone: the origin is sent, never the path. */
            img.referrerPolicy = 'strict-origin-when-cross-origin';
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

    /**
     * What .NET says goes on one canvas, or nothing if asking it failed. Thrown
     * out of createTile, an error would stop Leaflet partway through laying out
     * the canvases, leaving the rest of the pane's background undrawn and its
     * load event never fired; a blank canvas is the lesser harm.
     */
    function tilesUnder(minX, maxY, mmPerPixel, pixels) {
        try {
            return tilesFor(source, minX, maxY, mmPerPixel, pixels);
        } catch (error) {
            console.error('Could not work out the background tiles for a canvas.', error);
            return [];
        }
    }

    const BackgroundLayer = L.GridLayer.extend({
        createTile(coords, done) {
            const size = this.getTileSize();
            const canvas = document.createElement('canvas');
            canvas.width = size.x;
            canvas.height = size.y;

            const crs = map.options.crs;
            const nw = crs.project(map.unproject(coords.scaleBy(size), coords.z));
            const se = crs.project(map.unproject(coords.scaleBy(size).add(size), coords.z));
            const tiles = source === NONE || !tilesFor
                ? []
                : tilesUnder(nw.x, nw.y, (se.x - nw.x) / size.x, size.x);

            Promise.all(tiles.map(tile => image(tile.url, tile.size).then(img => ({ tile, img }))))
                .then(loaded => {
                    const ctx = canvas.getContext('2d');
                    ctx.imageSmoothingQuality = 'high';
                    for (const { tile, img } of loaded) {
                        if (!img) continue;
                        ctx.setTransform(...tile.transform);
                        // Half a pixel of overlap, so a rotated tile's anti-aliased
                        // edge does not leave a hairline of its neighbour's absence.
                        ctx.drawImage(img, -0.5, -0.5, tile.size + 1, tile.size + 1);
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
                if (unavailableBecause()) return;
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
        if (available === undefined) return labels.background;
        if (!navigator.onLine) return labels.offline;
        if (!available) return labels.noGeoreference;
        return null;
    }

    /** Whether the map on this pane can be laid on a source. */
    const offered = id => Boolean(available?.includes(id));

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

        choices.replaceChildren(...[NONE, ...sources.keys()].map(id => {
            const row = document.createElement('label');
            row.className = 'form-check';
            const radio = document.createElement('input');
            radio.type = 'radio';
            radio.className = 'form-check-input';
            radio.name = `background-${L.stamp(map)}`;
            radio.checked = id === source;
            radio.disabled = id !== NONE && !offered(id);
            radio.addEventListener('change', () => {
                wanted = id;
                choose(id);
                options.onChoose?.(id);
            });
            const text = document.createElement('span');
            text.className = 'form-check-label';
            text.textContent = id === NONE ? labels.none : sources.get(id).name;
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
        const next = !unavailableBecause() && offered(id) ? id : NONE;
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
                updateWhenZooming: false
            }).addTo(map);
            attribution = L.control.attribution({ prefix: false }).addTo(map);
            attribution.addAttribution(sources.get(source).attribution);
            if (wasNone) setOpacity(FIRST_OPACITY);
        } else {
            setOpacity(1);
        }
        render();
    }

    const onConnectivity = () => {
        choose(navigator.onLine && offered(wanted) ? wanted : NONE);
        render();
    };
    window.addEventListener('online', onConnectivity);
    window.addEventListener('offline', onConnectivity);

    control = new Menu().addTo(map);

    return {
        /**
         * Tells the background whether the pane shows ground it can lie on.
         *
         * @param ids the sources on offer, or null when the pane shows paper -
         *        which takes the background off until there is ground again
         * @param tiles (sourceId, minX, maxY, metresPerPixel, pixels) =>
         *        [{url, size, transform}]: what to draw on a canvas
         */
        setMap(ids, tiles) {
            available = ids ? [...ids] : null;
            tilesFor = tiles;
            const next = offered(wanted) ? wanted : NONE;
            if (next !== source) choose(next);
            else layer?.redraw();
            render();
        },

        /**
         * Shows the web map picked in another pane's menu: the panes lie on the
         * same ground, so they share one. Not passed on again.
         */
        select(id) {
            wanted = id;
            choose(id);
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
