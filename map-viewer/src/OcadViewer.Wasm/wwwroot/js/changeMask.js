/* Turning a change mask into something a map can show.
 *
 * The mask is one byte per pixel, straight from the renderer: what happened at
 * that spot between the two versions. This module only colours it. It knows
 * nothing about .NET, Leaflet or project versions, which is what lets the
 * filters be instant - changing which kinds are shown repaints from the same
 * bytes and never asks for the mask again.
 */

/** Matches OcadViewer.Core's PixelChange. */
export const CHANGE = {
    unchanged: 0,
    appeared: 1,   // blank before, ink after
    vanished: 2,   // ink before, blank after
    altered: 3     // ink both times, and not the same ink
};

/* One colour for every kind of change, by choice: the buttons say which kinds
 * are showing, so the picture does not have to. A second colour here would
 * imply the mask always knows which way a change went, and over ink it does
 * not.
 *
 * Crimson, and the hue is not a matter of taste. An orienteering map already
 * spends most of the spectrum: brown contours, blue water, yellow and orange
 * open land, green vegetation, black rock and paths, and - the one that caught
 * the first attempt out - purple for overprint and boundaries, which a magenta
 * tint is nearly indistinguishable from. Crimson is the gap. */
const TINT = { r: 232, g: 0, b: 60, a: 165 };

/**
 * Which mask values each filter shows.
 *
 * Altered pixels count as added/edited. They are the ones where both versions
 * have ink - a path redrawn across a yellow field - so the mask can see the
 * change but not its direction. Putting them here leaves deletions meaning
 * strictly "this went back to blank paper", which is a narrow, trustworthy
 * thing to look for.
 */
const SHOWN_BY = {
    added: [CHANGE.appeared, CHANGE.altered],
    deleted: [CHANGE.vanished]
};

/** The kinds of change a set of filters asks for, as a lookup by mask value. */
function wanted(filters) {
    const show = [false, false, false, false];

    for (const [filter, values] of Object.entries(SHOWN_BY)) {
        if (!filters?.[filter]) continue;
        for (const value of values) show[value] = true;
    }

    return show;
}

/** Whether these filters would show anything at all. */
export function showsAnything(filters) {
    return wanted(filters).some(Boolean);
}

/**
 * Paints the mask into an image, transparent except where change is shown.
 *
 * @param mask Uint8Array, one byte per pixel, row by row
 * @param width pixels
 * @param height pixels
 * @param filters {added, deleted}
 * @returns {Promise<string>} an object URL; revoke it when done with it
 */
export async function paint(mask, width, height, filters) {
    const show = wanted(filters);
    const pixels = new Uint8ClampedArray(width * height * 4);

    for (let i = 0, at = 0; i < mask.length; i++, at += 4) {
        if (!show[mask[i]]) continue;

        pixels[at] = TINT.r;
        pixels[at + 1] = TINT.g;
        pixels[at + 2] = TINT.b;
        pixels[at + 3] = TINT.a;
    }

    const canvas = document.createElement('canvas');
    canvas.width = width;
    canvas.height = height;
    canvas.getContext('2d').putImageData(new ImageData(pixels, width, height), 0, 0);

    // A blob URL rather than a data URL: at four megapixels the base64 of this
    // image is tens of megabytes of string, built and parsed for nothing.
    const blob = await new Promise(resolve => canvas.toBlob(resolve, 'image/png'));
    return URL.createObjectURL(blob);
}

/**
 * How much of the map these filters light up.
 *
 * Answered here rather than from a per-kind tally handed upwards, because
 * working out that "added" means appeared *plus* altered is this module's job.
 * Stating that rule anywhere else - and the page did state it, to write its
 * percentage - is a second copy to keep in step.
 *
 * @returns {{shown: number, total: number}} in pixels
 */
export function coverage(mask, filters) {
    const show = wanted(filters);
    let shown = 0;

    for (let i = 0; i < mask.length; i++)
        if (show[mask[i]]) shown++;

    return { shown, total: mask.length };
}

/**
 * The smallest rectangle holding everything these filters light up.
 *
 * In mask pixels, with y running downwards from the top of the mask - the
 * caller turns that into whatever coordinates it works in. Null when nothing is
 * shown.
 *
 * @returns {{left: number, top: number, right: number, bottom: number}|null}
 */
export function boundsOf(mask, width, height, filters) {
    const show = wanted(filters);

    let left = width, top = height, right = -1, bottom = -1;

    for (let y = 0, i = 0; y < height; y++) {
        for (let x = 0; x < width; x++, i++) {
            if (!show[mask[i]]) continue;

            if (x < left) left = x;
            if (x > right) right = x;
            if (y < top) top = y;
            if (y > bottom) bottom = y;
        }
    }

    return right < 0 ? null : { left, top, right, bottom };
}
