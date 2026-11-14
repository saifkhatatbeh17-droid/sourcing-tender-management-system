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
    try {
        const saved = document.getElementById(sessionStorage.getItem(key));
        if (saved?.matches('[data-bs-toggle="tab"]')) bootstrap.Tab.getOrCreateInstance(saved).show();
    } catch { /* Keep the default tab when storage is unavailable. */ }
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
    const sharedForm = page.querySelector('#weights-editor');
    page.querySelector('[data-ai-report]')?.addEventListener('submit', event => {
        event.currentTarget.querySelector('button').disabled = true;
        event.currentTarget.querySelector('[role="status"]').hidden = false;
        try { sessionStorage.setItem(key, 'report-tab'); } catch { /* Storage may be disabled. */ }
    });
    const editWeights = page.querySelector('#edit-weights');
    function closeWeights() {
        sharedForm.reset();
        sharedForm.hidden = true;
        editWeights.setAttribute('aria-expanded', 'false');
        page.querySelector('#weight-total').textContent = 'Total 100%';
        sharedForm.querySelector('[type="submit"]').disabled = false;
        page.querySelectorAll('.ev-approval-action button[data-was-disabled]').forEach(button => {
            button.disabled = button.dataset.wasDisabled === 'true';
            button.textContent = button.dataset.originalText;
            delete button.dataset.wasDisabled;
        });
        editWeights.focus();
    }
    editWeights?.addEventListener('click', () => {
        if (!sharedForm.hidden) { closeWeights(); return; }
        sharedForm.hidden = false;
        editWeights.setAttribute('aria-expanded', 'true');
        sharedForm.querySelector('[name="financialWeight"]').focus();
    });
    page.querySelector('#cancel-weights')?.addEventListener('click', closeWeights);
    sharedForm?.addEventListener('input', () => {
        const financial = Number(sharedForm.querySelector('[name="financialWeight"]').value);
        const requirements = Number(sharedForm.querySelector('[name="requirementsWeight"]').value);
        const total = Math.round((financial + requirements) * 100) / 100;
        sharedForm.querySelector('#weight-total').textContent = `Total ${total}%${total === 100 ? '' : ' · must be 100%'}`;
        sharedForm.querySelector('[type="submit"]').disabled = !(financial > 0 && requirements > 0 && total === 100);
        page.querySelectorAll('.ev-approval-action button').forEach(button => {
            if (!('wasDisabled' in button.dataset)) { button.dataset.wasDisabled = String(button.disabled); button.dataset.originalText = button.textContent; }
            button.disabled = true;
            button.textContent = 'Save weights first';
        });
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
