/* File System Access API wrapper for the project folder.
 *
 * The on-disk layout is fixed by the existing Angular app and must not change -
 * both apps run against the same folders during the migration:
 *
 *   <project>/
 *     <anything>.ocd        the working file, known as "current"
 *     Releases/
 *       V1/V1.ocd  V1.json
 *       V2/V2.ocd  V2.json
 *
 * Handles cannot cross the JS/.NET boundary, so they are kept here and referred to
 * by name. Chromium only: showDirectoryPicker does not exist in Firefox or Safari,
 * which is the same limitation the Angular app already has.
 */

const RELEASES = 'Releases';

let projectDir = null;       // FileSystemDirectoryHandle for the project root
let currentFile = null;      // FileSystemFileHandle for the working .ocd
let chosenFileName = null;   // the name the user picked, kept across a rescan
const versions = new Map();  // "V1" -> { ocd, meta }

export function isSupported() {
    return typeof window.showDirectoryPicker === 'function';
}

/** A folder name is a release if it is V followed by a positive integer. */
function isReleaseFolder(name) {
    if (!name || name.length < 2 || !name.startsWith('V')) return false;
    const n = Number(name.slice(1));
    return Number.isFinite(n) && n > 0;
}

function versionNumber(name) {
    const n = Number(name.replace('V', ''));
    return Number.isFinite(n) && n > 0 ? n : null;
}

/**
 * Asks for a project folder and reads its structure.
 * Must be called from a user gesture or the browser refuses.
 */
export async function openProject() {
    if (!isSupported())
        throw new Error('This browser cannot open folders. Use a Chromium-based browser.');

    projectDir = await window.showDirectoryPicker({ mode: 'readwrite' });
    chosenFileName = null;
    return await rescan();
}

/** Re-reads the folder. Cheap enough to call after creating a version. */
export async function rescan() {
    if (!projectDir) throw new Error('No project folder is open.');

    versions.clear();
    currentFile = null;

    // The working file is the only .ocd directly in the project root. If there is
    // more than one the caller has to choose, so report them all.
    const candidates = [];
    for await (const [name, handle] of projectDir.entries()) {
        if (handle.kind === 'file' && name.toLowerCase().endsWith('.ocd'))
            candidates.push(name);
    }

    // A rescan must not undo the user's choice. selectCurrentFile is followed by a
    // rescan, so clearing the handle and only restoring it for a single candidate
    // threw the selection away at the moment it was made - which surfaced later as
    // "No map file for current", pointing at reading rather than at choosing.
    if (chosenFileName && candidates.includes(chosenFileName))
        currentFile = await projectDir.getFileHandle(chosenFileName);
    else if (candidates.length === 1)
        currentFile = await projectDir.getFileHandle(candidates[0]);

    const releases = await getDirectory(projectDir, RELEASES, false);
    const found = [];
    if (releases) {
        for await (const name of releases.keys()) {
            if (!isReleaseFolder(name)) continue;

            const dir = await getDirectory(releases, name, false);
            if (!dir) continue;

            // Non-destructive existence checks. The Angular version creates the
            // file with create:true, measures it and deletes it again if empty,
            // which writes to the user's folder just to ask a question.
            const ocd = await getFile(dir, `${name}.ocd`);
            const meta = await getFile(dir, `${name}.json`);
            if (!ocd) continue;

            versions.set(name, { ocd, meta });
            found.push({
                name,
                number: versionNumber(name),
                metadata: meta ? await readJson(meta) : null
            });
        }
    }

    found.sort((a, b) => a.number - b.number);

    return {
        projectName: projectDir.name,
        currentFileName: currentFile?.name ?? null,
        ocdCandidates: candidates,
        versions: found
    };
}

/** Chooses which root .ocd is the working file, when the folder has several. */
export async function selectCurrentFile(fileName) {
    if (!projectDir) throw new Error('No project folder is open.');
    currentFile = await projectDir.getFileHandle(fileName);
    chosenFileName = currentFile.name;
    return currentFile.name;
}

/**
 * Reads a version's map bytes. "current" means the working file.
 * @returns Uint8Array
 */
export async function readMap(versionName) {
    const handle = versionName === 'current' ? currentFile : versions.get(versionName)?.ocd;
    if (!handle) throw new Error(`No map file for "${versionName}".`);
    return new Uint8Array(await (await handle.getFile()).arrayBuffer());
}

/** Size in bytes, for showing progress before a parse that may take seconds. */
/**
 * Copies the working file into Releases/<name>/ and writes its metadata, which is
 * exactly what the Angular app does so either app can read the result.
 */
export async function createVersion(versionName, metadataJson) {
    if (!projectDir) throw new Error('No project folder is open.');
    if (!currentFile) throw new Error('No working .ocd file is selected.');

    const releases = await getDirectory(projectDir, RELEASES, true);
    const dir = await releases.getDirectoryHandle(versionName, { create: true });

    const ocd = await dir.getFileHandle(`${versionName}.ocd`, { create: true });
    let stream = await ocd.createWritable({ keepExistingData: false });
    await stream.write(await currentFile.getFile());
    await stream.close();

    const meta = await dir.getFileHandle(`${versionName}.json`, { create: true });
    stream = await meta.createWritable({ keepExistingData: false });
    await stream.write(metadataJson);
    await stream.close();

    return await rescan();
}

/* ------------------------------------------------------------------ helpers */

async function getDirectory(parent, name, create) {
    try {
        return await parent.getDirectoryHandle(name, { create });
    } catch {
        return null;
    }
}

async function getFile(parent, name) {
    try {
        return await parent.getFileHandle(name, { create: false });
    } catch {
        return null;
    }
}

async function readJson(handle) {
    try {
        return JSON.parse(await (await handle.getFile()).text());
    } catch {
        return null;
    }
}
