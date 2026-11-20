// Sign-in mockup (docs/development/ui-mockups.md). There is no server session yet: the demo account's session is
// kept in localStorage, and every page but the sign-in page sends a visitor without one there. This is a mockup,
// not security: the server checks nothing and anyone can write the key. Loaded in <head>, before the page is drawn.
(function () {
    'use strict';

    const key = 'ocr.session';
    const hour = 60 * 60 * 1000;
    const loginPath = new URL('login', document.baseURI).pathname;

    function read() {
        try {
            const session = JSON.parse(localStorage.getItem(key));
            return session && typeof session.user === 'string' && session.expires > Date.now() ? session : null;
        } catch {
            return null;
        }
    }

    // Sends a visitor without a session to the sign-in page, with the page they asked for to come back to.
    function guard() {
        if (location.pathname === loginPath || read()) {
            return;
        }
        const back = location.pathname + location.search;
        location.replace(loginPath + '?returnUrl=' + encodeURIComponent(back));
    }

    window.ocrSession = {
        // Called by the sign-in page once the one-time code is right: 30 days with "Remember me", else 8 hours.
        start(user, remember) {
            localStorage.setItem(key, JSON.stringify({ user, expires: Date.now() + (remember ? 30 * 24 : 8) * hour }));
        },
        end() {
            try {
                localStorage.removeItem(key);
            } catch {
                // Nothing stored, nothing to end.
            }
        },
    };

    guard();

    // "Sign out" clears the session before its link opens the sign-in page.
    document.addEventListener('click', event => {
        if (event.target instanceof Element && event.target.closest('[data-sign-out]')) {
            window.ocrSession.end();
        }
    }, true);

    // Enhanced navigation swaps pages without reloading, and another tab may sign out: check again then.
    document.addEventListener('DOMContentLoaded', () => window.Blazor?.addEventListener('enhancedload', guard));
    window.addEventListener('storage', event => {
        if (event.key === key || event.key === null) {
            guard();
        }
    });
})();
