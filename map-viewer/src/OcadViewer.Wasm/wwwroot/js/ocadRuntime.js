// Reaches the [JSExport] methods on the .NET side.
//
// Blazor exposes its runtime through getDotnetRuntime(0) once it has started.
// getAssemblyExports returns the generated wrappers, which marshal byte[] as a real
// typed array - the reason for using [JSExport] here rather than
// DotNetObjectReference, which would base64 every tile inside JSON.

let exportsPromise = null;

/** Resolves the TileInterop exports, booting lazily and only once. */
export function getTileInterop() {
    exportsPromise ??= (async () => {
        const runtime = await waitForRuntime();
        const exports = await runtime.getAssemblyExports('OcadViewer.Wasm');
        const interop = exports?.OcadViewer?.Wasm?.TileInterop;
        if (!interop)
            throw new Error('TileInterop exports not found; is WasmEnableJSExports set?');
        return interop;
    })();
    return exportsPromise;
}

/** The runtime appears a moment after Blazor starts, so poll briefly for it. */
async function waitForRuntime(timeoutMs = 30000) {
    const deadline = performance.now() + timeoutMs;
    while (performance.now() < deadline) {
        const runtime = globalThis.getDotnetRuntime?.(0);
        if (runtime?.getAssemblyExports)
            return runtime;
        await new Promise(resolve => setTimeout(resolve, 50));
    }
    throw new Error('.NET runtime did not become available');
}

/**
 * Checks that the boundary behaves the way the tile layer needs: a byte[] has to
 * arrive as a typed array, not a base64 string, and the round trip has to be cheap
 * relative to the render itself.
 */
export async function probeBoundary(pixels = 256) {
    const interop = await getTileInterop();

    const started = performance.now();
    const bytes = interop.RenderTile(0, 0, 0, 60, 60, pixels);
    const callMs = performance.now() - started;

    if (bytes === null)
        return { ok: false, reason: 'no map loaded in slot 0' };

    const renderMs = interop.GetLastRenderMs();
    const expected = pixels * pixels * 4;

    return {
        ok: ArrayBuffer.isView(bytes) && bytes.length === expected,
        type: Object.prototype.toString.call(bytes),
        isTypedArray: ArrayBuffer.isView(bytes),
        length: bytes.length,
        expected,
        renderMs: +renderMs.toFixed(1),
        callMs: +callMs.toFixed(1),
        marshallingMs: +(callMs - renderMs).toFixed(2)
    };
}
