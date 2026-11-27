// Clarifications page: search the records you can ask about and show 'no results' when nothing matches.
(() => {
    const search = document.querySelector('#record-search');
    if (!search) return;
    const records = [...document.querySelectorAll('[data-record-search]')];
    const empty = document.querySelector('.ci-picker-empty');
    search.addEventListener('input', () => {
        const query = search.value.trim().toLocaleLowerCase();
        let visible = 0;
        records.forEach(record => {
            record.hidden = !record.dataset.recordSearch.toLocaleLowerCase().includes(query);
            if (!record.hidden) visible++;
        });
        empty.hidden = visible > 0;
    });
})();
