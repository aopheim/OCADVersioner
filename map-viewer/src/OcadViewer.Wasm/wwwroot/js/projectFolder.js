/* File System Access API wrapper for the project folder.
 *
 * Disk access and nothing else. Everything here works on paths relative to the
 * folder the user picked, '/'-separated. What those paths mean - where releases
 * live, what the working file is, what a version's files are called - is decided
 * in C#, by OcadViewer.Versioning, because that layout is a contract with the
 * Angular app and is stated in one place.
 *
 * Handles cannot cross the JS/.NET boundary, so the picked folder is kept here.
 * Chromium only: showDirectoryPicker does not exist in Firefox or Safari, which
 * is the same limitation the Angular app has.
 */

let projectDir = null;   // FileSystemDirectoryHandle for the folder the user picked

export function isSupported() {
    return typeof window.showDirectoryPicker === 'function';
}

/**
 * Asks for a project folder. Must be called from a user gesture or the browser
 * refuses.
 * @returns {Promise<string>} the folder's name
 */
export async function openProject() {
    if (!isSupported())
        throw new Error('This browser cannot open folders. Use a Chromium-based browser.');

    projectDir = await window.showDirectoryPicker({ mode: 'readwrite' });
    return projectDir.name;
}

/**
 * What a folder holds.
 * @param path folder to list, '' for the project root
 * @param withFolderFiles also list the files directly inside each subfolder
 * @returns {Promise<{files: string[], folders: {name: string, files: string[]}[]}|null>}
 *          null when the folder does not exist
 */
export async function listFolder(path, withFolderFiles) {
    const dir = await folderAt(path, false);
    if (!dir) return null;

    const files = [];
    const folders = [];

    for await (const [name, handle] of dir.entries()) {
        if (handle.kind === 'file') {
            files.push(name);
        } else {
            const inside = [];
            if (withFolderFiles) {
                for await (const [child, childHandle] of handle.entries())
                    if (childHandle.kind === 'file') inside.push(child);
            }
            folders.push({ name, files: inside });
        }
    }

    return { files, folders };
}

/** A file's contents. */
export async function readBytes(path) {
    const file = await fileAt(path, false);
    if (!file) throw new Error(`Could not find ${path}.`);
    return new Uint8Array(await (await file.getFile()).arrayBuffer());
}

/**
 * How many bytes a file is, without reading any of them.
 *
 * The size is in the directory entry, so this costs nothing, and it is the only
 * thing known about a load before it starts that says how long it will take.
 * What that is worth is decided in C#; this reports what the folder says.
 */
export async function sizeOf(path) {
    const file = await fileAt(path, false);
    if (!file) throw new Error(`Could not find ${path}.`);
    return (await file.getFile()).size;
}

/**
 * Several small text files at once.
 * @param paths may contain nulls, which come back as null
 * @returns {Promise<(string|null)[]>} null for anything missing or unreadable
 */
export async function readTexts(paths) {
    return Promise.all(paths.map(async path => {
        if (!path) return null;
        try {
            return await (await (await fileAt(path, false)).getFile()).text();
        } catch {
            return null;
        }
    }));
}

/**
 * Creates a folder, and any missing folders above it.
 *
 * Refuses if the folder itself already exists. Whatever is in it belongs to
 * somebody else - the Angular app shares these folders - and the files written
 * into it next are truncated as they are opened, so there is no undo.
 */
export async function createFolder(path) {
    const { parent, name } = split(path);
    const above = await folderAt(parent, true);

    if (await child(above, name, 'directory'))
        throw new Error(`${path} already exists. Re-read the folder and try again.`);

    await above.getDirectoryHandle(name, { create: true });
}

/** Copies a file, replacing the target if it exists. */
export async function copyFile(from, to) {
    const source = await fileAt(from, false);
    if (!source) throw new Error(`Could not find ${from}.`);
    await write(to, await source.getFile());
}

/** Writes a text file, replacing it if it exists. */
export async function writeText(path, text) {
    await write(path, text);
}

/* ------------------------------------------------------------------ helpers */

function root() {
    if (!projectDir) throw new Error('No project folder is open.');
    return projectDir;
}

function split(path) {
    const parts = path.split('/').filter(Boolean);
    return { parent: parts.slice(0, -1).join('/'), name: parts.at(-1) };
}

async function folderAt(path, create) {
    let dir = root();
    for (const part of path.split('/').filter(Boolean)) {
        dir = create
            ? await dir.getDirectoryHandle(part, { create: true })
            : await child(dir, part, 'directory');
        if (!dir) return null;
    }
    return dir;
}

async function fileAt(path, create) {
    const { parent, name } = split(path);
    const dir = await folderAt(parent, create);
    if (!dir) return null;
    return create ? dir.getFileHandle(name, { create: true }) : child(dir, name, 'file');
}

/** A child handle, or null - asking without creating anything. */
async function child(dir, name, kind) {
    try {
        return kind === 'file'
            ? await dir.getFileHandle(name, { create: false })
            : await dir.getDirectoryHandle(name, { create: false });
    } catch {
        return null;
    }
}

async function write(path, data) {
    const handle = await fileAt(path, true);
    const stream = await handle.createWritable({ keepExistingData: false });
    try {
        await stream.write(data);
        await stream.close();
    } catch (err) {
        await stream.abort().catch(() => { });
        throw err;
    }
}
