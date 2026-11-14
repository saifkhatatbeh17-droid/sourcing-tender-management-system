// Keep the selected report visible without moving the document scroll position.
const reportNavigation = document.querySelector('.cw-report-browser nav');
const selectedReport = reportNavigation?.querySelector('[aria-current="page"]');
if (selectedReport) {
    const offset = selectedReport.getBoundingClientRect().top - reportNavigation.getBoundingClientRect().top;
    reportNavigation.scrollTop += offset - (reportNavigation.clientHeight - selectedReport.offsetHeight) / 2;
}
