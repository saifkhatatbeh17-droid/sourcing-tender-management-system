const reviewDialog = document.getElementById('supplier-review-dialog');
if (reviewDialog) {
    let request;
    let opener;
    const content = reviewDialog.querySelector('[data-supplier-content]');
    const modal = bootstrap.Modal.getOrCreateInstance(reviewDialog);

    reviewDialog.addEventListener('hidden.bs.modal', () => {
        request?.abort();
        opener?.focus();
    });

    document.addEventListener('click', async event => {
        const link = event.target.closest('[data-supplier-dialog]');
        if (!link || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        event.preventDefault();
        opener = link;
        request?.abort();
        const pending = new AbortController();
        request = pending;
        reviewDialog.querySelector('.modal-title').textContent = link.dataset.company;
        content.textContent = 'Loading supplier details…';
        modal.show();
        const url = new URL(link.href);

        try {
            const response = await fetch(url, { signal: pending.signal });
            if (!response.ok || response.redirected) throw new Error('Supplier details unavailable');
            const markup = await response.text();
            if (!pending.signal.aborted) {
                const page = new DOMParser().parseFromString(markup, 'text/html');
                const flow = page.querySelector('[data-review-flow]');
                if (!flow) throw new Error('Supplier details unavailable');
                if (url.searchParams.get('reviewApplication') !== 'true') {
                    flow.querySelector('[data-review-step="Decision"]')?.remove();
                }
                content.replaceChildren(flow);
                initializeReview(content.querySelector('[data-review-flow]'));
            }
        } catch {
            if (pending.signal.aborted) return;
            const fallback = document.createElement('a');
            fallback.href = link.href;
            fallback.textContent = 'Open supplier details';
            content.replaceChildren(fallback);
        }
    });
}

document.querySelectorAll('[data-admin-feedback]').forEach(message => {
    window.setTimeout(() => message.remove(), 4500);
});

document.querySelectorAll('[data-auto-filter]').forEach(form => {
    const results = document.getElementById(form.dataset.results || 'user-results');
    const dialogs = form.dataset.results ? document.getElementById(form.dataset.dialogs) : document.getElementById('user-edit-dialogs');
    let request;
    let fadeOut;
    let fadeIn;

    form.addEventListener('change', event => {
        if (event.target.matches('select')) form.requestSubmit();
    });
    form.addEventListener('input', event => {
        if (event.target.matches('input[type="search"]') && !event.isComposing) form.requestSubmit();
    });
    form.addEventListener('submit', async event => {
        if (!results) return;
        event.preventDefault();
        request?.abort();
        const pending = new AbortController();
        request = pending;
        const animate = !window.matchMedia('(prefers-reduced-motion: reduce)').matches;
        if (animate && !fadeOut) {
            const opacity = getComputedStyle(results).opacity;
            fadeIn?.cancel();
            fadeOut = results.animate([{ opacity }, { opacity: 0 }], {
                duration: 125, easing: 'ease-out', fill: 'forwards'
            });
        }
        const url = new URL(form.action);
        url.search = new URLSearchParams(new FormData(form)).toString();
        try {
            const response = await fetch(url, { signal: pending.signal });
            if (!response.ok || response.redirected) throw new Error('Search unavailable');
            const page = new DOMParser().parseFromString(await response.text(), 'text/html');
            if (pending.signal.aborted) return;
            const updatedResults = page.getElementById(results.id);
            const updatedDialogs = dialogs && page.getElementById(dialogs.id);
            if (!updatedResults || (dialogs && !updatedDialogs)) throw new Error('Search results missing');
            const openDialog = dialogs?.querySelector('.modal.show');
            if (openDialog) {
                await new Promise(resolve => openDialog.addEventListener('hidden.bs.modal', resolve, { once: true }));
                if (pending.signal.aborted) return;
            }
            if (fadeOut) await fadeOut.finished;
            if (pending.signal.aborted) return;
            results.replaceChildren(...updatedResults.childNodes);
            if (dialogs) dialogs.replaceChildren(...updatedDialogs.childNodes);
            const exportLink = document.getElementById('supplier-export');
            const updatedExport = page.getElementById('supplier-export');
            if (exportLink && updatedExport) exportLink.setAttribute('href', updatedExport.getAttribute('href'));
            fadeOut?.cancel();
            fadeOut = null;
            if (animate) {
                fadeIn = results.animate([{ opacity: 0 }, { opacity: 1 }], {
                    duration: 165, easing: 'ease-out'
                });
            }
            applyLocalPage(results);
            window.history.replaceState(null, '', url);
        } catch {
            if (!pending.signal.aborted) window.location.assign(url);
        }
    });
});

function initializeReview(flow) {
    if (!flow) return;
    const steps = [...flow.querySelectorAll('[data-review-step]')];
    const controls = document.createElement('div');
    controls.className = 'admin-review-controls';
    const progress = document.createElement('p');
    progress.className = 'admin-review-progress';
    progress.setAttribute('aria-live', 'polite');

    function button(text) {
        const element = document.createElement('button');
        element.type = 'button';
        element.className = 'btn btn-outline-primary';
        element.textContent = text;
        return element;
    }

    const back = button('Back');
    const next = button('Next');
    const done = button('Done');
    done.setAttribute('data-bs-dismiss', 'modal');
    controls.append(back, next, done);
    const decisionActions = flow.querySelector('[data-decision-actions]');
    flow.prepend(progress);
    flow.append(controls);
    let index = 0;

    function show(moveFocus = false) {
        steps.forEach((step, position) => { step.hidden = position !== index; });
        progress.textContent = `Step ${index + 1} of ${steps.length} · ${steps[index].dataset.reviewStep}`;
        const isLastStep = index === steps.length - 1;
        if (isLastStep && decisionActions) {
            decisionActions.prepend(back);
            controls.hidden = true;
        } else {
            controls.prepend(back);
            controls.hidden = false;
        }
        back.hidden = index === 0;
        next.hidden = isLastStep;
        done.hidden = !isLastStep || Boolean(decisionActions);
        if (moveFocus) {
            const heading = steps[index].querySelector('h2');
            heading.tabIndex = -1;
            heading.focus();
            flow.closest('.modal-body')?.scrollTo(0, 0);
        }
    }

    back.addEventListener('click', () => { index--; show(true); });
    next.addEventListener('click', () => { index++; show(true); });
    show();
}

function applyLocalPage(results, filter) {
    const pager = results.querySelector('[data-local-pager]');
    if (!pager) return;
    const rows = [...results.querySelectorAll('[data-admin-row]')];
    const previous = pager.querySelector('[data-page-previous]');
    const next = pager.querySelector('[data-page-next]');
    const summary = pager.querySelector('[data-page-summary]');
    const search = filter?.querySelector('[name="search"]');
    const status = filter?.querySelector('select');
    const pageSize = 15;
    let page = 1;

    function render() {
        const term = search?.value.trim().toLowerCase() ?? '';
        const matching = rows.filter(row =>
            (!term || row.dataset.search?.toLowerCase().includes(term)) &&
            (!status?.value || row.dataset.status === status.value));
        const pages = Math.max(1, Math.ceil(matching.length / pageSize));
        page = Math.min(page, pages);
        const visible = new Set(matching.slice((page - 1) * pageSize, page * pageSize));
        rows.forEach(row => { row.hidden = !visible.has(row); });
        summary.textContent = matching.length ? `Page ${page} of ${pages} · ${matching.length} records` : 'No matching records';
        pager.hidden = pages === 1 && matching.length > 0;
        previous.disabled = page === 1;
        next.disabled = page === pages;
    }

    previous.addEventListener('click', () => { page--; render(); });
    next.addEventListener('click', () => { page++; render(); });
    if (filter) {
        filter.addEventListener('submit', event => event.preventDefault());
        filter.addEventListener('input', () => { page = 1; render(); });
        filter.addEventListener('change', () => { page = 1; render(); });
    }
    render();
}

applyLocalPage(document.getElementById('user-results') ?? document.createElement('div'));
document.querySelectorAll('[data-local-filter]').forEach(filter => {
    applyLocalPage(document.getElementById(filter.dataset.results), filter);
});
const applications = document.getElementById('supplier-results');
if (applications && !document.querySelector('[data-local-filter]')) applyLocalPage(applications);
