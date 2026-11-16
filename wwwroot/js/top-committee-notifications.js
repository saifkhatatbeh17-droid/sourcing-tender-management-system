(() => {
    'use strict';
    const openBell = () => {
        const bell = document.querySelector('.tc-notification-bell[data-open-notifications="true"] .notification-bell');
        if (bell && window.bootstrap?.Dropdown) {
            window.bootstrap.Dropdown.getOrCreateInstance(bell).show();
        }
    };
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', openBell, { once: true });
    else openBell();
})();
