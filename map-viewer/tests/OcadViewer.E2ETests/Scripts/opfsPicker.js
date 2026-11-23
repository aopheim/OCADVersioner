/* Replaces the native folder picker with a folder in the Origin Private File
 * System, for the end-to-end tests.
 *
 * Playwright cannot drive showDirectoryPicker's native dialog. OPFS returns a
 * real FileSystemDirectoryHandle, so projectFolder.js runs unchanged against
 * it - listing, reading and writing exactly as it would on disk.
 *
 * Which project the "user picks" is read from localStorage at the moment of
 * picking, so a test sets it before clicking and it survives a reload. With
 * none set the picker behaves as if the user closed it.
 *
 * window.__e2eOpfs is the one place that knows where test projects live; the
 * C# fixture builder (OpfsProject.cs) calls it rather than walking OPFS itself.
 *
 * Runs before any page script, on every navigation in the context.
 */
(() => {
    const ProjectKey = 'e2e.project';
    const ProjectsFolder = 'e2e-projects';

    /** A folder inside a test project; '' for the project itself. */
    async function folder(project, path, create) {
        const root = await navigator.storage.getDirectory();
        let dir = await (await root.getDirectoryHandle(ProjectsFolder, { create: true }))
            .getDirectoryHandle(project, { create });
        for (const part of (path ?? '').split('/').filter(Boolean))
            dir = await dir.getDirectoryHandle(part, { create });
        return dir;
    }

    /** Writes a file into a test project, creating folders on the way. */
    async function write(project, path, base64) {
        const parts = path.split('/');
        const name = parts.pop();
        const dir = await folder(project, parts.join('/'), true);
        const writable = await (await dir.getFileHandle(name, { create: true })).createWritable();
        await writable.write(Uint8Array.from(atob(base64), c => c.charCodeAt(0)));
        await writable.close();
    }

    /** Makes a test project the one the picker returns next. */
    function pickNext(project) {
        localStorage.setItem(ProjectKey, project);
    }

    window.__e2eOpfs = { folder, write, pickNext };

    window.showDirectoryPicker = async () => {
        const name = localStorage.getItem(ProjectKey);
        if (!name)
            throw new DOMException('The user aborted a request.', 'AbortError');

        return folder(name, '', false);
    };
})();
