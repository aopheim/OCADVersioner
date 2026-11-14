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
 * not. */
const TINT = { r: 214, g: 51, b: 168, a: 170 };

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

/** How many pixels each kind accounts for. For reporting, not for drawing. */
export function tally(mask) {
    const counts = { unchanged: 0, appeared: 0, vanished: 0, altered: 0 };
    const names = ['unchanged', 'appeared', 'vanished', 'altered'];

    for (let i = 0; i < mask.length; i++)
        counts[names[mask[i]]]++;

    return counts;
}
