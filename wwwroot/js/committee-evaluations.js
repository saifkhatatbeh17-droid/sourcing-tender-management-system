(() => {
    const page = document.querySelector('.evaluation-page');
    if (!page) return;
    const rail = page.querySelector('.ev-tender-rail');
    const search = page.querySelector('#tender-search');
    let filter = 'all';
    function filterTenders() {
        if (!rail || !search) return;
        let count = 0;
        rail.querySelectorAll('[data-tender-search]').forEach(card => {
            card.hidden = !card.dataset.tenderSearch.toLowerCase().includes(search.value.trim().toLowerCase())
                || (filter !== 'all' && card.dataset.tenderStatus !== filter);
            if (!card.hidden) count++;
        });
        page.querySelector('#tender-search-empty').hidden = count !== 0;
        page.querySelector('#tender-result-count').textContent = `${count} tender${count === 1 ? '' : 's'}`;
    }
    search?.addEventListener('input', filterTenders);
    page.querySelectorAll('[data-tender-filter]').forEach(button => button.addEventListener('click', () => {
        filter = button.dataset.tenderFilter;
        page.querySelectorAll('[data-tender-filter]').forEach(item => {
            item.classList.toggle('is-active', item === button);
            item.setAttribute('aria-pressed', String(item === button));
        });
        filterTenders();
    }));
    const storageKey = `evaluation-tab-${page.querySelector('#tender')?.value}`;
    function openTab(id) {
        const button = document.getElementById(id);
        if (button && !button.disabled) bootstrap.Tab.getOrCreateInstance(button).show();
    }
    page.querySelectorAll('[data-bs-toggle="tab"]').forEach(button => button.addEventListener('shown.bs.tab', () => {
        try { sessionStorage.setItem(storageKey, button.id); } catch { /* Optional local navigation memory. */ }
    }));
    const requested = new URL(location.href).searchParams.get('tab');
    if (requested) openTab(`${requested}-tab`);
    else { try { openTab(sessionStorage.getItem(storageKey)); } catch { /* Storage may be unavailable. */ } }
    page.querySelectorAll('[data-open-evaluation-tab]').forEach(button => button.addEventListener('click', () => openTab(button.dataset.openEvaluationTab)));
    page.querySelector('[data-percentage-proposal]')?.addEventListener('input', event => {
        const form = event.currentTarget;
        const input = form.elements.financialWeight;
        const number = input.valueAsNumber;
        form.querySelector('[data-technical-remainder]').textContent = Number.isFinite(number) && number > 0 && number < 100
            ? `Technical: ${Number((100 - number).toFixed(2))}%` : 'Technical: —';
    });
    page.querySelectorAll('[data-score-form]').forEach(form => form.addEventListener('input', () => {
        const financial = form.elements.financialMark.valueAsNumber;
        const technical = form.elements.technicalMark.valueAsNumber;
        form.querySelector('[data-mark-total]').textContent = Number.isFinite(financial) && Number.isFinite(technical)
            ? `Total: ${Number((financial + technical).toFixed(2))} / 100` : 'Total: — / 100';
    }));
    page.querySelectorAll('form').forEach(form => form.addEventListener('submit', event => {
        if (!form.reportValidity()) { event.preventDefault(); return; }
        form.setAttribute('aria-busy', 'true');
        form.querySelectorAll('button[type="submit"], button:not([type])').forEach(button => {
            button.disabled = true;
            button.textContent = form.matches('[data-ai-comparison]') ? 'Comparing…' : 'Saving…';
        });
        const status = form.querySelector('[role="status"][hidden]');
        if (status) status.hidden = false;
    }));
})();
