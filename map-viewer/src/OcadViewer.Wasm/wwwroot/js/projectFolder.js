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
 * This module reads and writes; it does not interpret. Which folder names count as
 * releases, what the next one is called and how the metadata parses are decided in
 * C#, by OcadViewer.Versioning, because those rules are a contract with the Angular
 * app and were previously stated here *and* there - in two languages, with nothing
 * keeping them in step. What this reports is simply what it saw.
 *
 * Handles cannot cross the JS/.NET boundary, so they are kept here and referred to
 * by name. Chromium only: showDirectoryPicker does not exist in Firefox or Safari,
 * which is the same limitation the Angular app already has.
 */

const RELEASES = 'Releases';

/** The name C# and this module both use for the working file. */
const WORKING_FILE = 'current';

let projectDir = null;       // FileSystemDirectoryHandle for the project root
let currentFile = null;      // FileSystemFileHandle for the working .ocd
let chosenFileName = null;   // the name the user picked, kept across a rescan
const folders = new Map();   // folder name -> { ocd, meta } under Releases/

export function isSupported() {
    return typeof window.showDirectoryPicker === 'function';
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

    folders.clear();
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
            const dir = await getDirectory(releases, name, false);
            if (!dir) continue;

            // Non-destructive existence checks. The Angular version creates the
            // file with create:true, measures it and deletes it again if empty,
            // which writes to the user's folder just to ask a question.
            const ocd = await getFile(dir, `${name}.ocd`);
            const meta = await getFile(dir, `${name}.json`);

            folders.set(name, { ocd, meta });
            found.push({
                name,
                hasMapFile: ocd !== null,
                metadataJson: meta ? await readText(meta) : null
            });
        }
    }

    return {
        projectName: projectDir.name,
        currentFileName: currentFile?.name ?? null,
        ocdCandidates: candidates,
        releaseFolders: found
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
    return new Uint8Array(await (await fileFor(versionName)).arrayBuffer());
}

/**
 * How many bytes a version's map file is, without reading any of them.
 *
 * The size is in the directory entry, so this costs nothing, and it is the only
 * thing known about a load before it starts that says how long it will take.
 * What that is worth is decided in C#; this reports what the folder says.
 */
export async function sizeOf(versionName) {
    return (await fileFor(versionName)).size;
}

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

/** The File behind a version's map, named rather than numbered. */
async function fileFor(versionName) {
    const handle = versionName === WORKING_FILE
        ? currentFile
        : folders.get(versionName)?.ocd;

    if (!handle) throw new Error(`No map file for "${versionName}".`);
    return await handle.getFile();
}

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

async function readText(handle) {
    try {
        return await (await handle.getFile()).text();
    } catch {
        return null;
    }
}
