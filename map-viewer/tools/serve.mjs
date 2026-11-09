// Minimal static file server for a published Blazor WebAssembly build.
//
// `dotnet run` serves the app during development; this serves what `dotnet
// publish` produces, which is a folder of static files with no .NET behind it -
// the same thing Azure Static Web Apps serves. Problems that only appear in the
// real bundle (trimming, fingerprinted assets, MIME types) show up here instead
// of after a deploy.
//
//   node tools/serve.mjs <root> [port]

import { createServer } from 'node:http';
import { createReadStream } from 'node:fs';
import { stat } from 'node:fs/promises';
import { join, extname, normalize } from 'node:path';

const root = process.argv[2];
const port = Number(process.argv[3] ?? 5002);

if (!root) {
    console.error('usage: node tools/serve.mjs <root> [port]');
    process.exit(2);
}

// .wasm must be served as application/wasm or instantiateStreaming refuses it.
const TYPES = {
    '.html': 'text/html; charset=utf-8',
    '.js': 'text/javascript; charset=utf-8',
    '.mjs': 'text/javascript; charset=utf-8',
    '.css': 'text/css; charset=utf-8',
    '.json': 'application/json; charset=utf-8',
    '.wasm': 'application/wasm',
    '.dat': 'application/octet-stream',
    '.blat': 'application/octet-stream',
    '.ocd': 'application/octet-stream',
    '.ttf': 'font/ttf',
    '.png': 'image/png',
    '.svg': 'image/svg+xml',
};

createServer(async (req, res) => {
    const url = decodeURIComponent(req.url.split('?')[0]);
    const relative = normalize(url === '/' ? '/index.html' : url).replace(/^(\.\.[/\\])+/, '');
    const path = join(root, relative);

    try {
        await send(path);
    } catch {
        // Client-side routes like /about have no file behind them. Fall back to
        // index.html, as Azure Static Web Apps does via navigationFallback - but
        // never for /_framework, where a missing asset must stay a 404 or Blazor
        // fails with a confusing integrity error instead of a clear one.
        if (!relative.startsWith('_framework') && !extname(relative)) {
            try {
                await send(join(root, 'index.html'));
                return;
            } catch { /* fall through to 404 */ }
        }
        res.writeHead(404, { 'Content-Type': 'text/plain' });
        res.end('not found');
    }

    async function send(filePath) {
        const info = await stat(filePath);
        if (!info.isFile()) throw new Error('not a file');

        res.writeHead(200, {
            'Content-Type': TYPES[extname(filePath).toLowerCase()] ?? 'application/octet-stream',
            'Content-Length': info.size,
            'Cache-Control': 'no-cache',
        });
        createReadStream(filePath).pipe(res);
    }
}).listen(port, () => console.log(`serving ${root} on http://localhost:${port}`));
