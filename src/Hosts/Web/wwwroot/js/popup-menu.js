// Dropdowns (language, account, download) are native <details class="popup-menu"> elements, so they
// work without the Blazor circuit. This closes them on a click outside or on Escape, as people expect
// from a menu; opening one closes the others, since that click is outside them.
document.addEventListener('click', (event) => {
    document.querySelectorAll('details.popup-menu[open]').forEach((menu) => {
        if (!menu.contains(event.target)) {
            menu.open = false;
        }
    });
});

document.addEventListener('keydown', (event) => {
    if (event.key !== 'Escape') {
        return;
    }
    document.querySelectorAll('details.popup-menu[open]').forEach((menu) => {
        menu.open = false;
        menu.querySelector('summary')?.focus();
    });
});

// A menu in a table would be cut off by the table's scroll box, so a floating menu's panel is placed
// in the window instead: under its button, or above it when there is no room below, and kept inside
// the window horizontally (aligned with the button's end, which is the left side in Arabic).
document.addEventListener('toggle', (event) => {
    const menu = event.target;
    if (!(menu instanceof HTMLDetailsElement) || !menu.matches('details.popup-menu.is-floating') || !menu.open) {
        return;
    }
    const panel = menu.querySelector('.popup-panel');
    const button = menu.querySelector('summary')?.getBoundingClientRect();
    if (!panel || !button) {
        return;
    }
    const gap = 4;
    const height = panel.offsetHeight;
    const width = panel.offsetWidth;
    const fitsBelow = button.bottom + gap + height <= window.innerHeight;
    panel.style.top = `${fitsBelow ? button.bottom + gap : Math.max(gap, button.top - gap - height)}px`;
    const rtl = getComputedStyle(menu).direction === 'rtl';
    const left = rtl ? button.left : button.right - width;
    panel.style.left = `${Math.max(gap, Math.min(left, window.innerWidth - width - gap))}px`;
}, true);

// The panel is fixed to the window, so it would stay put while the page scrolls under it.
const closeFloating = () => document.querySelectorAll('details.popup-menu.is-floating[open]').forEach((menu) => {
    menu.open = false;
});
window.addEventListener('scroll', closeFloating, true);
window.addEventListener('resize', closeFloating);
