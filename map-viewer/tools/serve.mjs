// Minimal static file server for the published WebAssembly spike.
//
// Exists because the spike has no ASP.NET host: it is a plain .NET WebAssembly
// module plus static files, which is precisely the point - that is what the
// Angular app would consume.
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
        const info = await stat(path);
        if (!info.isFile()) throw new Error('not a file');

        res.writeHead(200, {
            'Content-Type': TYPES[extname(path).toLowerCase()] ?? 'application/octet-stream',
            'Content-Length': info.size,
            'Cache-Control': 'no-cache',
        });
        createReadStream(path).pipe(res);
    } catch {
        res.writeHead(404, { 'Content-Type': 'text/plain' });
        res.end('not found');
    }
}).listen(port, () => console.log(`serving ${root} on http://localhost:${port}`));
