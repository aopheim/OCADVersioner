/* Turning a change mask into something a map can show.
 *
 * The mask is one byte per pixel, straight from the renderer: 1, 2 or 3 where
 * the two versions differ, 0 where both are blank, and 4 where there is ink
 * that did not change. This module only colours and counts it. It knows
 * nothing about .NET, Leaflet or project versions.
 *
 * Note what it does NOT know: the renderer classifies each changed pixel as
 * appeared, vanished or altered, and nothing here reads that. The overlay is a
 * single question - did this change - so the vocabulary stays on the side that
 * can actually answer it, and the contract across the boundary is just
 * "1 to 3 means changed, 4 means drawn".
 */

const UNCHANGED_INK = 4;

const isChange = value => value !== 0 && value !== UNCHANGED_INK;

/* Crimson, and the hue is not a matter of taste. An orienteering map already
 * spends most of the spectrum: brown contours, blue water, yellow and orange
 * open land, green vegetation, black rock and paths, and purple for overprint
 * and boundaries, which a magenta tint is nearly indistinguishable from.
 * Crimson is the gap. */
const TINT = { r: 232, g: 0, b: 60, a: 165 };

/**
 * Paints the mask into an image, transparent except where something changed.
 *
 * @param mask Uint8Array, one byte per pixel, row by row
 * @param width pixels
 * @param height pixels
 * @returns {Promise<string>} an object URL; revoke it when done with it
 */
export async function paint(mask, width, height) {
    const pixels = new Uint8ClampedArray(width * height * 4);

    for (let i = 0, at = 0; i < mask.length; i++, at += 4) {
        if (!isChange(mask[i])) continue;

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

    // toBlob hands back null rather than throwing when the browser cannot
    // encode the canvas, usually because it is too large.
    if (!blob)
        throw new Error(`The change overlay (${width} x ${height} px) is too large for this browser to draw.`);

    return URL.createObjectURL(blob);
}

/**
 * How much of the map changed.
 *
 * The total is the drawn map - every pixel with ink in either version - not
 * the whole rectangle, whose blank margins would understate the share.
 *
 * @returns {{shown: number, total: number}} in pixels
 */
export function coverage(mask) {
    let shown = 0, total = 0;

    for (let i = 0; i < mask.length; i++) {
        const value = mask[i];
        if (!value) continue;

        total++;
        if (value !== UNCHANGED_INK) shown++;
    }

    return { shown, total };
}

/**
 * The smallest rectangle holding every change.
 *
 * In mask pixels, with y running downwards from the top of the mask - the
 * caller turns that into whatever coordinates it works in. Null when nothing
 * changed.
 *
 * @returns {{left: number, top: number, right: number, bottom: number}|null}
 */
export function boundsOf(mask, width, height) {
    let left = width, top = height, right = -1, bottom = -1;

    for (let y = 0, i = 0; y < height; y++) {
        for (let x = 0; x < width; x++, i++) {
            if (!isChange(mask[i])) continue;

            if (x < left) left = x;
            if (x > right) right = x;
            if (y < top) top = y;
            if (y > bottom) bottom = y;
        }
    }

    return right < 0 ? null : { left, top, right, bottom };
}
