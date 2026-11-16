// Keep the selected report visible without moving the document scroll position.
const reportNavigation = document.querySelector('.cw-report-browser nav');
const selectedReport = reportNavigation?.querySelector('[aria-current="page"]');
if (selectedReport) {
    const offset = selectedReport.getBoundingClientRect().top - reportNavigation.getBoundingClientRect().top;
    reportNavigation.scrollTop += offset - (reportNavigation.clientHeight - selectedReport.offsetHeight) / 2;
}

// Search reports without leaving the selected report.
document.querySelectorAll('[data-committee-search]').forEach(input => {
    const list = document.getElementById(input.dataset.committeeSearch);
    const empty = document.querySelector(`[data-search-empty="${input.dataset.committeeSearch}"]`);
    if (!list) return;
    input.addEventListener('input', () => {
        const query = input.value.trim().toLocaleLowerCase();
        let visible = 0;
        list.querySelectorAll('[data-search-row]').forEach(row => {
            row.hidden = !row.textContent.toLocaleLowerCase().includes(query);
            if (!row.hidden) visible++;
        });
        if (empty) empty.hidden = visible !== 0;
    });
});
