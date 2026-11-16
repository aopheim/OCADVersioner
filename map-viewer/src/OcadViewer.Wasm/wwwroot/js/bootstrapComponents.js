/* The small amount of Bootstrap that is not driven by markup alone.
 *
 * Modals, dropdowns and dismissals work from data-bs-* attributes, but popovers
 * have to be constructed. The Angular app has the popover markup and never
 * initialises it - the attempt is still commented out in its settings component,
 * because importing Popover on its own broke a dropdown elsewhere. We load the
 * whole bundle, so the constructor is simply there to call.
 */

/**
 * Shows a modal from code.
 *
 * The Angular app does this by clicking a hidden button, with a comment saying
 * no other way was found without jQuery. There is one: Bootstrap's own API, which
 * is already loaded. The modal's data-bs-backdrop and data-bs-keyboard attributes
 * still apply, so a static modal stays static.
 */
export function showModal(id) {
    const element = document.getElementById(id);
    if (!element || !globalThis.bootstrap?.Modal) return false;

    const modal = globalThis.bootstrap.Modal.getOrCreateInstance(element);
    shownModals.set(id, modal);
    modal.show();
    return true;
}

/* Modals opened through showModal, kept so they can still be closed after Blazor
 * has removed their element. By the time a component's DisposeAsync runs, the
 * element is gone from the document and getElementById finds nothing. */
const shownModals = new Map();

/**
 * Closes and tears down a modal whose component is going away.
 *
 * Blazor's router follows an in-app link such as href="about" without a reload,
 * and removes the modal's markup with the page. Bootstrap's backdrop, though, is
 * appended to <body>, as are the modal-open class and the overflow it sets to
 * stop scrolling; left alone they stay behind and cover the next page. Hiding the
 * kept instance removes all of that, and works on a detached element because
 * these modals have no fade, so Bootstrap tidies up synchronously.
 */
export function disposeModal(id) {
    const element = document.getElementById(id);
    const modal = shownModals.get(id)
        ?? (element && globalThis.bootstrap?.Modal.getInstance(element));
    shownModals.delete(id);
    if (!modal) return false;

    modal.hide();
    modal.dispose();
    return true;
}

export function hideModal(id) {
    const element = document.getElementById(id);
    if (!element || !globalThis.bootstrap?.Modal) return false;

    // getInstance, not getOrCreateInstance: hiding a modal that was never shown
    // would otherwise construct one just to tell it to do nothing.
    globalThis.bootstrap.Modal.getInstance(element)?.hide();
    return true;
}

/**
 * Calls back into .NET whenever a modal finishes hiding.
 *
 * A modal closes four ways - the button, the X, the backdrop and Escape - and
 * only the first two are clicks a component can handle. Bootstrap raises this
 * event for all of them, which is the only place a draft can be discarded once
 * rather than four times.
 */
export function onModalHidden(id, dotNetRef) {
    const element = document.getElementById(id);
    if (!element) return false;

    element.addEventListener('hidden.bs.modal', () => {
        // The component may be gone by the time this fires on a page that
        // navigated away; losing the callback is not worth an error.
        dotNetRef.invokeMethodAsync('OnHidden').catch(() => { });
    });

    return true;
}

export function initPopovers(root) {
    const Popover = globalThis.bootstrap?.Popover;
    if (!Popover) return 0;

    const triggers = (root ?? document).querySelectorAll('[data-bs-toggle="popover"]');
    let created = 0;

    for (const trigger of triggers) {
        // getOrCreateInstance rather than new: this runs again on re-render, and a
        // second instance on the same element leaves a popover that never closes.
        Popover.getOrCreateInstance(trigger);
        created++;
    }

    return created;
}
