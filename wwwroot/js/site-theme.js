(() => {
    const root = document.documentElement;
    const key = 'sourcing-theme';
    const preference = window.matchMedia('(prefers-color-scheme: dark)');

    function savedTheme() {
        try {
            return localStorage.getItem(key) || localStorage.getItem('admin-sidebar-theme');
        } catch {
            return null;
        }
    }

    function apply(theme) {
        root.dataset.bsTheme = theme;
        root.dataset.theme = theme;
        const dark = theme === 'dark';
        const label = dark ? 'Light Mode' : 'Dark Mode';
        document.querySelectorAll('[data-theme-toggle]').forEach(button => {
            button.setAttribute('aria-pressed', String(dark));
            button.setAttribute('aria-label', label);
            button.title = label;
            const text = button.querySelector('[data-theme-label]');
            if (text) text.textContent = label;
        });
    }

    function initialize() {
        const saved = savedTheme();
        apply(saved === 'dark' || saved === 'light' ? saved : preference.matches ? 'dark' : 'light');
    }

    initialize();
    document.addEventListener('DOMContentLoaded', () => apply(root.dataset.theme));
    preference.addEventListener('change', () => { if (!savedTheme()) initialize(); });
    window.addEventListener('storage', event => { if (event.key === key || event.key === null) initialize(); });
    document.addEventListener('click', event => {
        if (!event.target.closest('[data-theme-toggle]')) return;
        const theme = root.dataset.theme === 'dark' ? 'light' : 'dark';
        apply(theme);
        try { localStorage.setItem(key, theme); } catch { /* Theme still applies to the current page. */ }
    });
})();
