// Dump objects of an OCAD 12+/2018 .ocd: index, symbol, type, status, dates, ids, geometry hash.
// Usage: node ocddump.mjs [-c] file.ocd ...
import { readFileSync } from 'node:fs';
import { createHash } from 'node:crypto';

const delphi = d => d ? new Date(Date.UTC(1899, 11, 30) + Math.round(d * 86400000)).toISOString().replace('T', ' ').slice(0, 23) : '-';
const showCoords = process.argv.includes('-c');

for (const path of process.argv.slice(2).filter(a => a !== '-c')) {
  const b = readFileSync(path);
  const ver = b.readInt16LE(4), sub = b.readInt16LE(6), firstIdx = b.readInt32LE(12);
  console.log(`${path}: version ${ver}.${sub}, ${b.length} bytes`);
  let nxt = firstIdx, idx = 1;
  const types = { 1: 'point', 2: 'line', 3: 'area', 4: 'text', 5: 'text', 6: 'line text', 7: 'rect' };
  const statuses = { 1: 'normal', 2: 'hidden', 3: 'deleted' };
  while (nxt > 0 && nxt < b.length) {
    const blk = nxt;
    nxt = b.readInt32LE(blk);
    for (let i = 0; i < 256; i++, idx++) {
      const o = blk + 4 + i * 40;
      const pos = b.readInt32LE(o + 16), sym = b.readInt32LE(o + 24);
      const otp = b.readUInt8(o + 28), status = b.readUInt8(o + 30);
      if (!sym || !status) continue;
      const srvId = b.readInt32LE(pos + 16);
      const created = b.readDoubleLE(pos + 24), modified = b.readDoubleLE(pos + 36);
      const nItem = b.readInt32LE(pos + 44);
      const raw = b.subarray(pos + 56, pos + 56 + 8 * nItem);
      const pts = [];
      for (let k = 0; k < nItem; k++) pts.push([(raw.readInt32LE(8 * k) >> 8) / 100, (raw.readInt32LE(8 * k + 4) >> 8) / 100]);
      const geo = createHash('md5').update(raw).digest('hex').slice(0, 8);
      console.log(`#${String(idx).padStart(3)} sym ${(sym / 1000).toFixed(3).padStart(7)} ${(types[otp] ?? otp).padEnd(5)} ${(statuses[status] ?? status).padEnd(7)} `
        + `srvId ${srvId} n=${String(nItem).padStart(3)} geo ${geo} created ${delphi(created)} modified ${delphi(modified)} first ${JSON.stringify(pts[0])}`);
      if (showCoords) console.log('      ', JSON.stringify(pts));
    }
  }
  console.log();
}
