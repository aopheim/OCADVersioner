/* Dropping a file on a pane, as if it had been chosen in that pane's file input.
 *
 * A dropped file arrives in a DragEvent that Blazor cannot see into, and a File
 * cannot cross into .NET anyway. So a drop is turned into the one thing the page
 * already handles: the input now holds the file, and fires 'change'. Whatever
 * reads the input next - mapWorkspace.showFile - cannot tell which way the file
 * came in, so there is one path for picking and dropping, not two.
 *
 * DOM only. It does not read the file, and knows nothing about maps.
 */

/**
 * Makes the element an input sits in accept a dropped file for that input.
 *
 * The drop zone is the input's parent, so the input can be placed anywhere in
 * the area it serves - a pane - without the caller having to reach that area's
 * element as well.
 *
 * @param input an <input type="file">
 * @returns a handle whose dispose() stops listening
 */
export function acceptDrops(input) {
    const zone = input.parentElement;
    const HOVER = 'fileDropTarget';

    const carriesFiles = event => [...(event.dataTransfer?.types ?? [])].includes('Files');

    const onOver = event => {
        if (!carriesFiles(event)) return;
        // Without this the browser opens the file instead of dropping it here.
        event.preventDefault();
        event.dataTransfer.dropEffect = 'copy';
        zone.classList.add(HOVER);
    };

    const onLeave = event => {
        // dragleave also fires when moving onto a child; only leaving counts.
        if (!zone.contains(event.relatedTarget)) zone.classList.remove(HOVER);
    };

    const onDrop = event => {
        zone.classList.remove(HOVER);
        if (!carriesFiles(event)) return;
        event.preventDefault();

        const files = event.dataTransfer.files;
        if (!files?.length) return;

        input.files = files;
        input.dispatchEvent(new Event('change', { bubbles: true }));
    };

    zone.addEventListener('dragover', onOver);
    zone.addEventListener('dragleave', onLeave);
    zone.addEventListener('drop', onDrop);

    return {
        dispose() {
            zone.removeEventListener('dragover', onOver);
            zone.removeEventListener('dragleave', onLeave);
            zone.removeEventListener('drop', onDrop);
            zone.classList.remove(HOVER);
        }
    };
}
