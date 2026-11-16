/* A one-job-per-frame scheduler.
 *
 * WebAssembly is single threaded, so a tile render blocks everything - including
 * Leaflet's own animations. Running one job per frame lets the browser paint what
 * is already done and keeps input alive in between.
 *
 * This is a factory, not a module. Each caller gets its own queue, latch and
 * watchdog; shared ones would let two viewports feed one queue and fight over
 * one latch.
 */

/**
 * Creates an independent pump.
 * @returns {{enqueue: Function, remove: Function, clear: Function,
 *            dispose: Function, readonly size: number}}
 */
export function createTilePump() {
    const queue = [];

    let pumping = false;
    let tickPending = false;
    let lastPumpAt = 0;
    let watchdog = null;
    let disposed = false;

    /* The pump deliberately does NOT rely on requestAnimationFrame alone. When the
     * page is hidden - and, as the performance work found, when the window is
     * merely occluded while document.visibilityState still says "visible" - rAF
     * stops firing. A naive implementation latches "pumping" and then deadlocks
     * permanently, even after the page comes back. A timer fallback plus a
     * watchdog is the fix.
     *
     * The timer is 250 ms, which is also the cost of this safety net: with frames
     * flowing, gaps between tiles are 1-17 ms; with the window occluded, every
     * tile waits out the full timer. */
    const FALLBACK_MS = 250;
    const STALL_MS = 2000;
    const WATCHDOG_MS = 1000;

    function schedule() {
        if (disposed) return;
        if (!pumping) {
            pumping = true;
            nextTick();
        }
        startWatchdog();
    }

    function nextTick() {
        if (tickPending || disposed) return;
        tickPending = true;
        lastPumpAt = performance.now();

        // Arm both: rAF keeps us in step with painting when frames are flowing,
        // the timer keeps the queue moving when they are not. Whichever fires
        // first clears the flag and does the work; the other finds nothing to do.
        const run = () => {
            if (!tickPending || disposed) return;
            tickPending = false;
            pump();
        };
        requestAnimationFrame(run);
        setTimeout(run, FALLBACK_MS);
    }

    function pump() {
        const job = queue.shift();
        if (!job) {
            pumping = false;
            stopWatchdog();
            return;
        }

        try {
            job.run();
        } catch (err) {
            console.error('[ocad] tile job failed', err);
        }
        nextTick();
    }

    /* If the page is hidden mid-queue the pump can stall. Restart it when it comes
     * back, and never let the latch stay set with work outstanding. */
    function startWatchdog() {
        watchdog ??= setInterval(() => {
            if (!queue.length) { stopWatchdog(); return; }
            if (performance.now() - lastPumpAt > STALL_MS) {
                pumping = false;
                schedule();
            }
        }, WATCHDOG_MS);
    }

    function stopWatchdog() {
        if (watchdog !== null) { clearInterval(watchdog); watchdog = null; }
    }

    const onVisibilityChange = () => {
        if (!document.hidden && queue.length) { pumping = false; schedule(); }
    };
    document.addEventListener('visibilitychange', onVisibilityChange);

    return {
        /**
         * Queues work. The tag is opaque here and only used by remove().
         * @param {Function} run
         * @param {*} [tag]
         */
        enqueue(run, tag) {
            queue.push({ run, tag });
            schedule();
        },

        /** Drops queued jobs whose tag matches, newest first. Returns how many. */
        remove(matches) {
            let dropped = 0;
            for (let i = queue.length - 1; i >= 0; i--) {
                if (matches(queue[i].tag)) { queue.splice(i, 1); dropped++; }
            }
            return dropped;
        },

        clear() {
            queue.length = 0;
        },

        dispose() {
            disposed = true;
            queue.length = 0;
            stopWatchdog();
            document.removeEventListener('visibilitychange', onVisibilityChange);
        },

        get size() { return queue.length; }
    };
}
