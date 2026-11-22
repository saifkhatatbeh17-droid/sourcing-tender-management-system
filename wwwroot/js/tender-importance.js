const importanceDialog = document.getElementById('tender-importance');
document.querySelector('[data-importance-open]')?.addEventListener('click', () => importanceDialog.showModal());
importanceDialog?.querySelector('[data-importance-close]')?.addEventListener('click', () => importanceDialog.close());

importanceDialog?.querySelector('[data-importance-form]')?.addEventListener('submit', async event => {
    event.preventDefault();
    const form = event.currentTarget, panel = form.closest('.tc-importance');
    const button = form.querySelector('button'), status = panel.querySelector('[data-importance-status]');
    const result = panel.querySelector('[data-importance-result]');
    button.disabled = true;
    form.setAttribute('aria-busy', 'true');
    result.hidden = true;
    status.textContent = 'Analyzing the report…';
    try {
        const response = await fetch(form.action, { method: 'POST', body: new FormData(form) });
        if (!response.headers.get('content-type')?.includes('application/json')) {
            throw new Error('AI analysis is unavailable on this page.');
        }
        const data = await response.json();
        if (!response.ok) throw new Error(data.error || 'Could not analyze this report. Refresh and try again.');
        panel.querySelector('[data-importance-level]').textContent = data.level;
        panel.querySelector('[data-importance-reason]').textContent = data.reason;
        const missing = panel.querySelector('[data-importance-missing]');
        missing.querySelector('ul').replaceChildren(...data.missingInformation.map(text => {
            const item = document.createElement('li'); item.textContent = text; return item;
        }));
        missing.hidden = data.missingInformation.length === 0;
        result.hidden = false;
        status.textContent = 'AI assessment based on the current report. Verify the evidence before deciding.';
        button.textContent = 'Analyze again';
    } catch (error) { status.textContent = error.message; }
    finally { button.disabled = false; form.removeAttribute('aria-busy'); }
});
