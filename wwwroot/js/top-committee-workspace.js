/* Search assigned tenders without hiding the primary control or changing server routes. */
(() => {
    document.querySelectorAll('[data-top-search]').forEach(input => {
        const scope = input.closest('.workspace-section');
        const rows = [...scope.querySelectorAll(input.dataset.topSearch)];
        const feedback = scope.querySelector('[data-top-search-status]');
        input.addEventListener('input', () => {
            const query = input.value.trim().toLocaleLowerCase();
            rows.forEach(row => { row.hidden = !row.textContent.toLocaleLowerCase().includes(query); });
            const count = rows.filter(row => !row.hidden).length;
            feedback.hidden = !query;
            feedback.textContent = count ? `${count} matching tenders` : 'No tenders match. Clear the search to see all assignments.';
        });
    });
    function revealConversation() {
        if (!location.hash) return;
        const target = document.getElementById(decodeURIComponent(location.hash.slice(1)));
        const details = target?.closest('details.tc-conversation');
        if (details) { details.open = true; target.scrollIntoView({block: 'start'}); }
    }
    revealConversation();
    window.addEventListener('hashchange', revealConversation);
})();
/* Progressive presentation only: existing sections, IDs, forms and server bindings remain intact. */
(() => {
    const root = document.querySelector('.tc-tender');
    if (!root || !window.bootstrap?.Tab) return;
    const oldNavigation = root.querySelector('[aria-label="Tender sections"]');
    if (!oldNavigation) return;
    const votingSection = root.querySelector('#voting');
    if (votingSection) root.insertBefore(oldNavigation, votingSection);
    const groups = [
        ['overview', 'Recommendation', ['recommendation', 'evaluation-report']],
        ['comparison', 'Compare offers', ['bid-comparison', 'selected-bid']],
        ['request', 'Purchase request', ['request']],
        ['records', 'Committee record', ['committee']],
        ['vote', 'Your decision', ['voting']]
    ];
    const nav = document.createElement('div');
    nav.className = 'tc-tab-nav nav'; nav.setAttribute('role', 'tablist'); nav.setAttribute('aria-label', 'Tender sections');
    const content = document.createElement('div'); content.className = 'tab-content';
    oldNavigation.replaceWith(nav); nav.after(content);
    groups.forEach(([key, label, ids], index) => {
        const button = document.createElement('button'); button.type = 'button'; button.className = 'nav-link' + (index === 0 ? ' active' : '');
        button.id = `tc-tab-${key}`; button.textContent = label; button.setAttribute('role', 'tab');
        button.setAttribute('data-bs-toggle', 'tab'); button.setAttribute('data-bs-target', `#tc-pane-${key}`);
        button.setAttribute('aria-controls', `tc-pane-${key}`); button.setAttribute('aria-selected', String(index === 0));
        if (index !== 0) button.tabIndex = -1;
        const pane = document.createElement('div'); pane.className = `tab-pane tc-pane-${key}` + (index === 0 ? ' active' : '');
        pane.id = `tc-pane-${key}`; pane.setAttribute('role', 'tabpanel'); pane.setAttribute('aria-labelledby', button.id); pane.tabIndex = 0;
        ids.forEach(id => { const section = root.querySelector(`#${id}`); if (section) pane.append(section); });
        nav.append(button); content.append(pane);
    });
    // Keep the secondary bid details available in a scrollable, accessible Bootstrap modal.
    const bid = root.querySelector('#selected-bid');
    let bidModal;
    if (bid) {
        const modal = document.createElement('div'); modal.id = 'tc-bid-modal'; modal.className = 'modal fade tc-bid-modal'; modal.tabIndex = -1;
        modal.setAttribute('aria-labelledby', 'selected-bid-heading'); modal.setAttribute('aria-hidden', 'true');
        modal.innerHTML = '<div class="modal-dialog modal-xl modal-dialog-scrollable"><div class="modal-content"><div class="modal-header"><span>Bid and supplier details</span><button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Close bid details"></button></div><div class="modal-body"></div><div class="modal-footer"><button type="button" class="btn btn-outline-secondary" data-bs-dismiss="modal">Close</button></div></div></div>';
        modal.querySelector('.modal-body').append(bid); root.append(modal); bidModal = bootstrap.Modal.getOrCreateInstance(modal);
        const open = document.createElement('button'); open.type = 'button'; open.className = 'btn btn-outline-primary mb-3'; open.textContent = 'Open selected bid details';
        open.addEventListener('click', () => bidModal.show(open)); root.querySelector('#tc-pane-comparison').append(open);
        modal.addEventListener('hidden.bs.modal', () => {
            if (location.hash === '#selected-bid') history.replaceState(null, '', '#bid-comparison');
            open.focus();
        });
    }
    function revealHash() {
        const id = location.hash.slice(1);
        const group = groups.find(g => g[2].some(section => section === id || document.getElementById(section)?.querySelector(`[id="${CSS.escape(id)}"]`)));
        if (group) bootstrap.Tab.getOrCreateInstance(document.getElementById(`tc-tab-${group[0]}`)).show();
        if (id === 'selected-bid' && bidModal) bidModal.show();
    }
    window.addEventListener('hashchange', revealHash);
    nav.addEventListener('shown.bs.tab', event => {
        const group = groups.find(g => `tc-tab-${g[0]}` === event.target.id);
        if (group && !(group[0] === 'comparison' && location.hash === '#selected-bid')) history.replaceState(null, '', '#' + group[2][0]);
    });
    if (root.querySelector('.validation-summary-errors')) bootstrap.Tab.getOrCreateInstance(document.getElementById('tc-tab-vote')).show();
    else revealHash();
})();
