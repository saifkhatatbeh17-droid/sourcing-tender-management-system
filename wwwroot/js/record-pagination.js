(() => {
    'use strict';
    const pageSize = 7;
    // Record lists only; form choices, line items and exported reports are not paged.
    const selector = [
        '.admin-record-table tbody > tr', '.supplier-offer-card-grid > article',
        '.supplier-invitation-row', '.supplier-history-record', '.supplier-notifications > article',
        '.clarification-list > .clarification-record', '.rp-table tbody > tr',
        '.mr-queue-group ul > li', '.mr-awards > .mr-award', '.mr-conversations .mr-conversation',
        '.manager-refresh table tbody > tr', '.mr-rejection-record', '.ci-conversations > .ci-conversation',
        '.ci-record-list > .ci-record-option', '.ev-tender-rail > a', '#report-options > a',
        '.tb-list > a', '.tc-tender-choices > a', '.tc-conversation',
        '.os-offers > article', '.so-page--invitations table tbody > tr', '.ev-offer-grid > article'
    ].join(',');
    const groups = new Map();
    const makePager = parent => {
        const nav = document.createElement('nav');
        nav.className = 'record-pagination'; nav.setAttribute('aria-label', 'Record pages');
        const label = document.createElement('span'); label.setAttribute('role', 'status');
        const actions = document.createElement('div');
        const previous = document.createElement('button'), next = document.createElement('button');
        [previous, next].forEach(button => { button.type = 'button'; button.className = 'btn btn-outline-primary'; });
        previous.textContent = 'Previous'; next.textContent = 'Next';
        actions.append(previous, next); nav.append(label, actions);
        const anchor = parent.tagName === 'TBODY' ? parent.closest('.table-responsive') || parent.closest('table') : parent;
        anchor.after(nav);
        const state = { parent, nav, label, previous, next, page:1, initialized:false };
        previous.addEventListener('click', () => { state.page--; render(state); });
        next.addEventListener('click', () => { state.page++; render(state); });
        return state;
    };
    function render(state) {
        const rows = Array.from(state.parent.children).filter(row => row.matches(selector));
        const matches = rows.filter(row => !row.hidden && row.style.display !== 'none');
        if (!state.initialized) {
            const selected = matches.findIndex(row => row.getAttribute('aria-current') === 'page' || row.querySelector('[aria-current="page"]'));
            if (selected >= 0) state.page = Math.floor(selected / pageSize) + 1;
            state.initialized = true;
        }
        const pages = Math.max(1, Math.ceil(matches.length / pageSize));
        state.page = Math.max(1, Math.min(state.page, pages));
        const visible = new Set(matches.slice((state.page - 1) * pageSize, state.page * pageSize));
        rows.forEach(row => row.toggleAttribute('data-page-hidden', !visible.has(row)));
        state.nav.hidden = matches.length <= pageSize;
        state.label.textContent = `Page ${state.page} of ${pages} · ${matches.length} records`;
        state.previous.disabled = state.page === 1; state.next.disabled = state.page === pages;
    }
    function discover() {
        document.querySelectorAll(selector).forEach(row => {
            const parent = row.parentElement;
            if (!groups.has(parent)) groups.set(parent, makePager(parent));
        });
        groups.forEach((state, parent) => {
            if (!parent.isConnected) { state.nav.remove(); groups.delete(parent); }
            else render(state);
        });
    }
    discover();
    // Existing local searches control `hidden`; pagination uses its own attribute.
    // This keeps searching over every record, including later pages.
    new MutationObserver(records => {
        let changed = false;
        records.forEach(record => {
            if (record.type === 'attributes' && record.target.matches(selector)) {
                const state = groups.get(record.target.parentElement);
                if (state) state.page = 1;
                changed = true;
            } else if (record.type === 'childList' && [...record.addedNodes].some(node =>
                node.nodeType === 1 && (node.matches(selector) || node.querySelector(selector)))) changed = true;
        });
        if (changed) discover();
    }).observe(document.querySelector('main') || document.body, { subtree:true, childList:true, attributes:true, attributeFilter:['hidden','style'] });
})();
