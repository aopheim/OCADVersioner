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
 * Runs before any page script, on every navigation in the context.
 */
(() => {
    const ProjectKey = 'e2e.project';

    window.showDirectoryPicker = async () => {
        const name = localStorage.getItem(ProjectKey);
        if (!name)
            throw new DOMException('The user aborted a request.', 'AbortError');

        const root = await navigator.storage.getDirectory();
        const projects = await root.getDirectoryHandle('e2e-projects', { create: true });
        return projects.getDirectoryHandle(name, { create: false });
    };
})();
