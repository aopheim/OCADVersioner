// Hands a file produced in .NET to the browser's download flow.
//
// The bytes arrive as a DotNetStreamReference rather than base64 so a multi-megabyte
// PNG does not have to be encoded into a string and passed through interop.
export async function downloadStream(fileName, contentType, streamReference) {
    const buffer = await streamReference.arrayBuffer();
    const url = URL.createObjectURL(new Blob([buffer], { type: contentType }));
    try {
        const link = document.createElement('a');
        link.href = url;
        link.download = fileName;
        document.body.appendChild(link);
        link.click();
        link.remove();
    } finally {
        // Give the browser a moment to start reading the blob before releasing it.
        setTimeout(() => URL.revokeObjectURL(url), 10_000);
    }
}
