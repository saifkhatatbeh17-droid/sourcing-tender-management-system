// Opens and collapses the workspace sidebar menu.
const sidebar = document.querySelector('.admin-sidebar');
const menuToggle = sidebar.querySelector('.admin-sidebar-toggle');
const mobile = window.matchMedia('(max-width: 650px)');
const hover = window.matchMedia('(min-width: 651px) and (hover: hover) and (pointer: fine)');
const pinKey = 'admin-sidebar-pinned';
let pinned = false;
const closeDelay = 350;
let closeTimer;

function cancelClose() {
    clearTimeout(closeTimer);
}

function scheduleClose() {
    cancelClose();
    closeTimer = setTimeout(() => {
        if (hover.matches && !pinned && !sidebar.matches(':hover') && !sidebar.contains(document.activeElement)) {
            setCollapsed(true);
        }
    }, closeDelay);
}
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
    cancelClose();
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
    cancelClose();
    if (hover.matches) setCollapsed(false);
});
sidebar.addEventListener('pointerleave', () => {
    if (hover.matches && !pinned) scheduleClose();
});
sidebar.addEventListener('focusin', event => {
    cancelClose();
    if (hover.matches && event.target !== menuToggle) setCollapsed(false);
});
sidebar.addEventListener('focusout', event => {
    if (hover.matches && !pinned && !sidebar.contains(event.relatedTarget) && !sidebar.matches(':hover')) scheduleClose();
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
