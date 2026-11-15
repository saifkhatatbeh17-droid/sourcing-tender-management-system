(() => {
    const page = document.querySelector('.evaluation-page');
    if (!page) return;
    page.querySelectorAll('[data-evaluation-theme]').forEach(button => {
        button.addEventListener('click', () => {
            page.dataset.theme = button.dataset.evaluationTheme;
            document.documentElement.dataset.theme = button.dataset.evaluationTheme;
            page.querySelectorAll('[data-evaluation-theme]').forEach(option => {
                option.setAttribute('aria-pressed', String(option === button));
            });
        });
    });
    const rail = document.querySelector('.ev-tender-rail');
    const activeTender = rail?.querySelector('[aria-current="page"]');
    if (activeTender) rail.scrollTop += activeTender.getBoundingClientRect().top - rail.getBoundingClientRect().top - 8;
    const tenderSearch = document.querySelector('#tender-search');
    let tenderFilter = 'all';
    function filterTenders() {
        const query = tenderSearch.value.trim().toLowerCase();
        let count = 0;
        rail.querySelectorAll('[data-tender-search]').forEach(card => {
            card.hidden = !card.dataset.tenderSearch.toLowerCase().includes(query)
                || (tenderFilter !== 'all' && card.dataset.tenderStatus !== tenderFilter);
            if (!card.hidden) count++;
        });
        document.querySelector('#tender-search-empty').hidden = count !== 0;
        document.querySelector('#tender-result-count').textContent = `${count} tender${count === 1 ? '' : 's'}`;
    }
    tenderSearch?.addEventListener('input', filterTenders);
    document.querySelectorAll('[data-tender-filter]').forEach(button => {
        button.addEventListener('click', () => {
            tenderFilter = button.dataset.tenderFilter;
            document.querySelectorAll('[data-tender-filter]').forEach(option => {
                option.classList.toggle('is-active', option === button);
                option.setAttribute('aria-pressed', String(option === button));
            });
            filterTenders();
        });
    });
    const key = `evaluation-tab-${page.querySelector('#tender')?.value}`;
    page.querySelectorAll('[data-bs-toggle="tab"]').forEach(button => {
        button.addEventListener('shown.bs.tab', () => {
            try { sessionStorage.setItem(key, button.id); } catch { /* Storage may be disabled. */ }
        });
    });
    page.querySelectorAll('[data-open-evaluation-tab]').forEach(button => {
        button.addEventListener('click', () => {
            const target = document.getElementById(button.dataset.openEvaluationTab);
            bootstrap.Tab.getOrCreateInstance(target).show();
            target.scrollIntoView({ block: 'nearest' });
        });
    });
    const navigation = new URL(window.location.href);
    const requestedTab = navigation.searchParams.get('tab');
    if (['offers', 'forms', 'report'].includes(requestedTab)) {
        const target = document.getElementById(`${requestedTab}-tab`);
        if (target) {
            bootstrap.Tab.getOrCreateInstance(target).show();
            target.focus({ preventScroll: true });
            navigation.searchParams.delete('tab');
            history.replaceState(null, '', navigation);
        }
    } else {
        try {
            const saved = document.getElementById(sessionStorage.getItem(key));
            if (saved?.matches('[data-bs-toggle="tab"]')) bootstrap.Tab.getOrCreateInstance(saved).show();
        } catch { /* Keep the default tab when storage is unavailable. */ }
    }
    if (navigation.searchParams.get('focus') === 'ai') {
        const ai = page.querySelector('#ai-offers');
        const scrollToAi = () => { ai?.focus({ preventScroll: true }); ai?.scrollIntoView({ block: 'start', behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'instant' : 'smooth' }); };
        requestAnimationFrame(scrollToAi);
        navigation.searchParams.delete('focus');
        history.replaceState(null, '', navigation);
    }
    page.querySelector('[data-ai-comparison]')?.addEventListener('submit', event => {
        const form = event.currentTarget;
        const technical = form.elements.requirementsWeight;
        technical.setCustomValidity(Number(form.elements.financialWeight.value) + Number(technical.value) === 100 ? '' : 'Percentages must total 100%.');
        if (!form.reportValidity()) { event.preventDefault(); return; }
        form.querySelector('button').disabled = true;
        form.querySelector('[role="status"]').hidden = false;
        form.setAttribute('aria-busy', 'true');
        try { sessionStorage.setItem(key, 'offers-tab'); } catch { /* Storage may be disabled. */ }
    });
    page.querySelector('[data-ai-comparison]')?.addEventListener('input', event => event.currentTarget.elements.requirementsWeight.setCustomValidity(''));
    page.querySelector('[data-ai-report]')?.addEventListener('submit', event => {
        event.currentTarget.querySelector('button').disabled = true;
        event.currentTarget.querySelector('[role="status"]').hidden = false;
        try { sessionStorage.setItem(key, 'report-tab'); } catch { /* Storage may be disabled. */ }
    });
    page.querySelector('[data-percentage-proposal]')?.addEventListener('input', event => {
        const form = event.currentTarget;
        const number = form.elements.financialWeight.valueAsNumber;
        form.querySelector('[data-financial-percentage]').textContent = `${number}%`;
        form.elements.financialWeight.setAttribute('aria-valuetext', `${number}% financial, ${Number((100 - number).toFixed(0))}% technical`);
        form.querySelector('[data-technical-percentage]').textContent = `${Number((100 - number).toFixed(0))}%`;
        const slider = form.elements.financialWeight;
        slider.style.setProperty('--slider-fill', `${(number - Number(slider.min)) / (Number(slider.max) - Number(slider.min)) * 100}%`);
    });
    const rows = page.querySelector('#criteria-rows');
    if (!rows) return;
    function renumber() {
        rows.querySelectorAll('.criterion-row').forEach((row, i) => {
            row.querySelectorAll('[name]').forEach(input => input.name = input.name.replace(/criteria\[\d+\]/, `criteria[${i}]`));
        });
    }
    page.querySelector('#add-criterion').addEventListener('click', () => {
        const row = rows.firstElementChild.cloneNode(true);
        row.querySelectorAll('input').forEach(input => input.value = '');
        rows.appendChild(row);
        renumber();
        row.querySelector('input').focus();
    });
    rows.addEventListener('click', event => {
        if (event.target.classList.contains('remove-criterion') && rows.children.length > 1) {
            event.target.closest('.criterion-row').remove();
            renumber();
        }
    });
})();
