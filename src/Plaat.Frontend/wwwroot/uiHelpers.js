window.uiHelpers = {
    setBodyScrollLocked: function (locked) {
        document.body.classList.toggle('scroll-locked', locked);
    },
    // Matches the player bar's mobile breakpoint (PlayerBar.razor.css / QueuePanel.razor.css)
    // where both the fullscreen cover art view and the queue panel become full-screen overlays.
    isMobileViewport: function () {
        return window.matchMedia('(max-width: 640.98px)').matches;
    },
    _outsideClickHandlers: new WeakMap(),
    // Desktop-only by design: on mobile the queue panel is a full-screen overlay with its own
    // close button, so closing it on an "outside" click (which barely exists there) isn't wanted.
    addOutsideClickListener: function (dotNetRef, elementRef) {
        this.removeOutsideClickListener(elementRef);

        const handler = (event) => {
            if (this.isMobileViewport()) {
                return;
            }
            if (elementRef && !elementRef.contains(event.target)) {
                dotNetRef.invokeMethodAsync('CloseFromOutsideClick');
            }
        };

        document.addEventListener('mousedown', handler, true);
        this._outsideClickHandlers.set(elementRef, handler);
    },
    removeOutsideClickListener: function (elementRef) {
        const handler = this._outsideClickHandlers.get(elementRef);
        if (handler) {
            document.removeEventListener('mousedown', handler, true);
            this._outsideClickHandlers.delete(elementRef);
        }
    },
    // Swipe support for the star rating. Capturing the pointer on the star row keeps pointermove
    // and pointerup coming to that row even once the finger has left it - which is what lets a
    // swipe off the left edge clear the rating, and what stops a release just outside the row from
    // being lost. The row's box is measured in the same call, so a whole swipe costs one round trip
    // rather than one per pixel moved (it cannot move mid-gesture).
    beginPointerGesture: function (elementRef, pointerId) {
        if (!elementRef || !elementRef.getBoundingClientRect) {
            return null;
        }

        try {
            elementRef.setPointerCapture(pointerId);
        } catch {
            // No capture to be had (the pointer is already gone, or the browser refused it). The
            // swipe still tracks correctly for as long as the pointer stays over the row.
        }

        const rect = elementRef.getBoundingClientRect();

        return { left: rect.left, width: rect.width };
    },
    endPointerGesture: function (elementRef, pointerId) {
        try {
            if (elementRef && elementRef.hasPointerCapture(pointerId)) {
                elementRef.releasePointerCapture(pointerId);
            }
        } catch {
            // Already released - nothing to undo.
        }
    },
};

// Mobile browsers with a dynamic toolbar (e.g. Firefox Android) animate the URL/tab bar in and
// out of view without updating window.innerHeight to match - position:fixed elements anchored
// with `bottom: 0` stay glued to that stale, smaller layout viewport, leaving a gap between them
// and the true bottom of the screen once the toolbar has slid away. window.visualViewport tracks
// the real visible area live, so we expose the gap as a CSS variable that PlayerBar.razor.css /
// QueuePanel.razor.css fold into their `bottom` offsets to close it.
(function () {
    const root = document.documentElement;

    function updateViewportOffsetBottom() {
        const vv = window.visualViewport;
        if (!vv) {
            return;
        }
        const offsetBottom = Math.min(0, window.innerHeight - vv.height - vv.offsetTop);
        root.style.setProperty('--viewport-offset-bottom', offsetBottom + 'px');
    }

    if (window.visualViewport) {
        window.visualViewport.addEventListener('resize', updateViewportOffsetBottom);
        window.visualViewport.addEventListener('scroll', updateViewportOffsetBottom);
    }
    window.addEventListener('orientationchange', updateViewportOffsetBottom);
    updateViewportOffsetBottom();
})();
