/* Unfinished merges, kept in this browser's IndexedDB.
 *
 * A merge's decisions and regions, keyed by the three files' hashes, so picking
 * the same files again can pick up where the user left off. Only decisions are
 * kept, never the map files themselves. Records are JSON strings in and out: what
 * is in them is C#'s business (MergeProgress), this module only stores them.
 *
 * It is a convenience. If the browser refuses storage - a private window,
 * cleared site data - every call fails, and the caller carries on without it.
 */

const DATABASE = 'ocadversioner-merge';
const STORE = 'merges';

function open() {
    return new Promise((resolve, reject) => {
        const request = indexedDB.open(DATABASE, 1);
        request.onupgradeneeded = () => request.result.createObjectStore(STORE, { keyPath: 'key' });
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error);
    });
}

async function transaction(mode, work) {
    const db = await open();
    try {
        return await new Promise((resolve, reject) => {
            const tx = db.transaction(STORE, mode);
            const request = work(tx.objectStore(STORE));
            tx.oncomplete = () => resolve(request?.result);
            tx.onerror = () => reject(tx.error);
            tx.onabort = () => reject(tx.error);
        });
    } finally {
        db.close();
    }
}

/** Stores a record (a JSON object with a `key`), replacing any with the same key. */
export function save(json) {
    return transaction('readwrite', store => store.put(JSON.parse(json)));
}

/** The record under `key` as JSON, or null. */
export async function load(key) {
    const record = await transaction('readonly', store => store.get(key));
    return record ? JSON.stringify(record) : null;
}

/** Every record, as a JSON array. */
export async function list() {
    return JSON.stringify(await transaction('readonly', store => store.getAll()) ?? []);
}

export function remove(key) {
    return transaction('readwrite', store => store.delete(key));
}
