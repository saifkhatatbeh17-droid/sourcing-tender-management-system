(() => {
    function attachSearch(input, rows, count, empty, noun) {
        if (!input) return;
        input.addEventListener('input', () => {
            const query = input.value.trim().toLocaleLowerCase();
            let visible = 0;
            rows.forEach(row => { const target = row.closest('.tb-choice-row') ?? row; target.hidden = !row.dataset.search.toLocaleLowerCase().includes(query); if (!target.hidden) visible++; });
            if (count) count.textContent = `${visible} ${noun}${visible === 1 ? '' : 's'}`;
            if (empty) empty.hidden = visible !== 0;
        });
    }
    attachSearch(document.querySelector('#request-search'), document.querySelectorAll('.rr-request-row[data-search]'), document.querySelector('.rr-search-result'), document.querySelector('.rr-no-results'), 'request');
    attachSearch(document.querySelector('#tender-search'), document.querySelectorAll('.tb-choice'), document.querySelector('.tb-result'), document.querySelector('.tb-no-results'), 'tender');
    const chooser = document.querySelector('.tender-browser');
    chooser?.addEventListener('keydown', event => { if (event.key === 'Escape' && chooser.open) { event.preventDefault(); chooser.open = false; chooser.querySelector('summary').focus(); } });
    chooser?.addEventListener('toggle', () => { if (chooser.open) document.querySelector('#tender-search')?.focus(); });
})();
