/* Hands the user a file to save, as a download.
 *
 * A download rather than the File System Access save picker: it works in every
 * browser, and it can never overwrite one of the files being merged, because
 * the browser only ever writes a new file into the downloads folder.
 */

/**
 * @param fileName the suggested name
 * @param bytes    Uint8Array
 */
export function download(fileName, bytes) {
    const url = URL.createObjectURL(new Blob([bytes], { type: 'application/octet-stream' }));
    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    link.remove();
    // The click has handed the blob to the browser's download; give it a moment.
    setTimeout(() => URL.revokeObjectURL(url), 10_000);
}
