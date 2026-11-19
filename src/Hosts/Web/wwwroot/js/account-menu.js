// The account menu is a native <details> element (it works without the Blazor circuit).
// This only closes it on a click outside it or on Escape, as people expect from a menu.
document.addEventListener('click', (event) => {
    document.querySelectorAll('details.account-menu[open]').forEach((menu) => {
        if (!menu.contains(event.target)) {
            menu.open = false;
        }
    });
});

document.addEventListener('keydown', (event) => {
    if (event.key !== 'Escape') {
        return;
    }
    document.querySelectorAll('details.account-menu[open]').forEach((menu) => {
        menu.open = false;
        menu.querySelector('summary')?.focus();
    });
});
