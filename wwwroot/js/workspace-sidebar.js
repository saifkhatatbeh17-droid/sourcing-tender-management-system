const sidebar = document.querySelector('.admin-sidebar');
const menuToggle = sidebar.querySelector('.admin-sidebar-toggle');
const mobile = window.matchMedia('(max-width: 650px)');
const hover = window.matchMedia('(min-width: 651px) and (hover: hover) and (pointer: fine)');
const pinKey = 'admin-sidebar-pinned';
let pinned = false;
try {
    pinned = sessionStorage.getItem(pinKey) === 'true';
} catch {
    // Pinning still works for the current page.
}

function setCollapsed(collapsed) {
    sidebar.classList.toggle('is-collapsed', collapsed);
    menuToggle.setAttribute('aria-expanded', String(!collapsed));
}

function syncSidebar() {
    setCollapsed(mobile.matches || (hover.matches && !pinned && !sidebar.matches(':hover')));
    const label = hover.matches ? 'Pin sidebar open' : 'Toggle sidebar menu';
    menuToggle.setAttribute('aria-label', label);
    menuToggle.title = hover.matches && pinned ? 'Unpin sidebar' : label;
    if (hover.matches) {
        menuToggle.setAttribute('aria-pressed', String(pinned));
    } else {
        menuToggle.removeAttribute('aria-pressed');
    }
}

syncSidebar();
mobile.addEventListener('change', syncSidebar);
hover.addEventListener('change', syncSidebar);
sidebar.addEventListener('pointerenter', () => {
    if (hover.matches) setCollapsed(false);
});
sidebar.addEventListener('pointerleave', () => {
    if (hover.matches && !pinned && !sidebar.querySelector('.admin-sidebar-nav :focus')) setCollapsed(true);
});
sidebar.addEventListener('focusin', event => {
    if (hover.matches && event.target !== menuToggle) setCollapsed(false);
});
sidebar.addEventListener('focusout', event => {
    if (hover.matches && !pinned && !sidebar.contains(event.relatedTarget) && !sidebar.matches(':hover')) setCollapsed(true);
});
menuToggle.addEventListener('click', () => {
    if (!hover.matches) {
        setCollapsed(!sidebar.classList.contains('is-collapsed'));
        return;
    }
    pinned = !pinned;
    try {
        sessionStorage.setItem(pinKey, String(pinned));
    } catch {
        // Pinning still works for the current page.
    }
    syncSidebar();
});
