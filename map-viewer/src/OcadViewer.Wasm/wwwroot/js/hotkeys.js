/* One keyboard shortcut, listened for on the page rather than on an element.
 *
 * Nothing here holds the keyboard for long: the maps are Leaflet canvases and a
 * toolbar button loses focus the moment its work begins. A shortcut bound to an
 * element would therefore answer only after a click on that element, which is
 * the click it was meant to save. So the listener sits on the document and
 * decides for itself whether a press was meant for it.
 *
 * What it will not answer to is written here rather than in C#, because it is
 * all DOM knowledge: what the user is typing into, and which combinations
 * belong to the browser or the operating system.
 */

/**
 * Starts listening for one key.
 *
 * @param key the key as the browser names it, e.g. 'c'
 * @param dotNetRef the component to tell; its Pressed() is called
 * @returns a handle whose dispose() stops listening
 */
export function listen(key, dotNetRef) {
    const wanted = key.toLowerCase();

    const onKeyDown = event => {
        if (event.key?.toLowerCase() !== wanted) return;

        // A chord is somebody else's shortcut, and a held key is one press.
        if (event.ctrlKey || event.altKey || event.metaKey || event.repeat) return;
        if (isTyping(event.target)) return;

        event.preventDefault();

        // The component can go while a press is in flight; losing the press is
        // better than an error nobody can act on.
        dotNetRef.invokeMethodAsync('Pressed').catch(() => { });
    };

    document.addEventListener('keydown', onKeyDown);

    return { dispose: () => document.removeEventListener('keydown', onKeyDown) };
}

/** Whether the press belongs to something the user is writing in. */
function isTyping(target) {
    if (!target) return false;
    if (target.isContentEditable) return true;

    return ['input', 'textarea', 'select'].includes(target.tagName?.toLowerCase());
}
