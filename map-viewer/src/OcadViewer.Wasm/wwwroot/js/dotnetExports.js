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
