(() => {
    const page = document.querySelector('.evaluation-page');
    if (!page) return;
    page.querySelectorAll('[data-evaluation-theme]').forEach(button => {
        button.addEventListener('click', () => {
            page.dataset.theme = button.dataset.evaluationTheme;
            page.querySelectorAll('[data-evaluation-theme]').forEach(option => {
                option.setAttribute('aria-pressed', String(option === button));
            });
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
