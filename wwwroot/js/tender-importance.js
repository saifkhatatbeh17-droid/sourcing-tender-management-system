const importanceDialog = document.getElementById('tender-importance');
document.querySelector('[data-importance-open]')?.addEventListener('click', () => importanceDialog.showModal());
importanceDialog?.querySelectorAll('[data-importance-close]').forEach(button =>
    button.addEventListener('click', () => importanceDialog.close()));
// Close when the backdrop (the dialog element itself) is clicked.
importanceDialog?.addEventListener('click', event => { if (event.target === importanceDialog) importanceDialog.close(); });

const money = (value, currency) => value == null ? null
    : `${Number(value).toLocaleString(undefined, { minimumFractionDigits: 0, maximumFractionDigits: 0 })} ${currency ?? ''}`.trim();
const percent = value => value == null ? null : `${Number(value).toLocaleString(undefined, { maximumFractionDigits: 1 })}%`;

function renderFacts(list, facts) {
    const rows = [];
    const add = (label, value, note, warning) => { if (value != null) rows.push({ label, value, note, warning }); };
    if (facts) {
        const currency = facts.offerCurrency ?? facts.currency;
        add('Nominated offer', money(facts.offerTotal, currency),
            facts.estimatedBudget != null ? `Estimate ${money(facts.estimatedBudget, facts.currency)}` : null);
        add('Share of remaining budget', percent(facts.percentOfRemaining),
            facts.remaining != null ? `${money(facts.remaining, facts.currency)} left in FY${facts.fiscalYear}` : null,
            facts.percentOfRemaining >= 10);
        add('Vs requester estimate',
            facts.varianceFromEstimatePercent == null ? null
                : `${facts.varianceFromEstimatePercent > 0 ? '+' : ''}${percent(facts.varianceFromEstimatePercent)}`,
            null, facts.varianceFromEstimatePercent > 15);
        add('Department budget used', percent(facts.usedPercent),
            facts.fiscalYearElapsedPercent != null ? `${percent(facts.fiscalYearElapsedPercent)} of the year elapsed` : null,
            facts.usedPercent > facts.fiscalYearElapsedPercent);
    }
    list.replaceChildren(...rows.map(row => {
        const wrap = document.createElement('div');
        if (row.warning) wrap.className = 'is-warning';
        const dt = document.createElement('dt'); dt.textContent = row.label;
        const dd = document.createElement('dd'); dd.textContent = row.value;
        if (row.note) { const small = document.createElement('small'); small.textContent = row.note; dd.append(small); }
        wrap.append(dt, dd);
        return wrap;
    }));
    list.hidden = rows.length === 0;
}

importanceDialog?.querySelector('[data-importance-form]')?.addEventListener('submit', async event => {
    event.preventDefault();
    const form = event.currentTarget, panel = form.closest('.tc-importance');
    const button = form.querySelector('button[type=submit]');
    const status = panel.querySelector('[data-importance-status]');
    const loading = panel.querySelector('[data-importance-loading]');
    const error = panel.querySelector('[data-importance-error]');
    const intro = panel.querySelector('[data-importance-intro]');
    const result = panel.querySelector('[data-importance-result]');
    button.disabled = true;
    form.setAttribute('aria-busy', 'true');
    error.hidden = true;
    result.hidden = true;
    loading.hidden = false;
    status.textContent = 'Analyzing the report…';
    try {
        const response = await fetch(form.action, { method: 'POST', body: new FormData(form) });
        if (!response.headers.get('content-type')?.includes('application/json')) {
            throw new Error('AI analysis is unavailable on this page.');
        }
        const data = await response.json();
        if (!response.ok) throw new Error(data.error || 'Could not analyze this report. Refresh and try again.');
        const badge = panel.querySelector('[data-importance-level]');
        badge.textContent = data.level;
        badge.dataset.level = data.level;
        renderFacts(panel.querySelector('[data-importance-facts]'), data.facts);
        panel.querySelector('[data-importance-reason]').textContent = data.reason;
        const missing = panel.querySelector('[data-importance-missing]');
        missing.querySelector('ul').replaceChildren(...data.missingInformation.map(text => {
            const item = document.createElement('li'); item.textContent = text; return item;
        }));
        missing.hidden = data.missingInformation.length === 0;
        if (intro) intro.hidden = true;
        result.hidden = false;
        status.textContent = `Suggested importance: ${data.level}.`;
        button.textContent = 'Analyze again';
    } catch (failure) {
        error.textContent = failure.message;
        error.hidden = false;
        status.textContent = failure.message;
    } finally {
        loading.hidden = true;
        button.disabled = false;
        form.removeAttribute('aria-busy');
    }
});
