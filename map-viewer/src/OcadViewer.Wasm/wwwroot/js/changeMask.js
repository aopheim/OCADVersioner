/* Colouring a change mask.
 *
 * The mask is one byte per pixel, straight from the renderer: zero where the
 * two versions agree, non-zero where they do not. This module only paints it.
 * It knows nothing about .NET, Leaflet or project versions.
 *
 * Note what it does NOT know. The renderer classifies each changed pixel as
 * appeared, vanished or altered, and nothing here reads that: the overlay asks
 * a single question - did this change - so the vocabulary stays on the side
 * that can answer it. It does not count the changed pixels or work out where
 * they are, either. Both of those are measurements of the same bytes, and the
 * side that already walks them pixel by pixel reports them in the same pass.
 * What is left is the one thing a browser can do and .NET cannot: make a
 * picture the page can hang on a map.
 */

/* Crimson, and the hue is not a matter of taste. An orienteering map already
 * spends most of the spectrum: brown contours, blue water, yellow and orange
 * open land, green vegetation, black rock and paths, and - the one that caught
 * the first attempt out - purple for overprint and boundaries, which a magenta
 * tint is nearly indistinguishable from. Crimson is the gap. */
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
        if (!mask[i]) continue;

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
