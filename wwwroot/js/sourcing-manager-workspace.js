/* Role lists keep filtering local; server form actions and bindings remain authoritative. */
(() => {
    document.querySelectorAll('[data-manager-search]').forEach(input => {
        const scope = input.closest('.workspace-section');
        const records = [...scope.querySelectorAll(input.dataset.managerSearch)];
        const status = scope.querySelector('[data-manager-search-status]');
        input.addEventListener('input', () => {
            const query = input.value.trim().toLocaleLowerCase();
            let visible = 0;
            records.forEach(record => {
                record.hidden = !record.textContent.toLocaleLowerCase().includes(query);
                if (!record.hidden) visible++;
            });
            if (status) {
                status.hidden = !query;
                status.textContent = visible ? `${visible} matching ${visible === 1 ? 'record' : 'records'}` : 'No matches. Try another title or clear the search.';
            }
            scope.querySelectorAll('.mr-queue-group').forEach(group => {
                group.hidden = query && ![...group.querySelectorAll('li')].some(row => !row.hidden);
            });
        });
    });

    // Picking a tender loads its committee contacts straight away; no separate button.
    document.querySelectorAll('[data-autosubmit]').forEach(control => {
        control.addEventListener('change', () => control.form?.requestSubmit());
    });
})();
