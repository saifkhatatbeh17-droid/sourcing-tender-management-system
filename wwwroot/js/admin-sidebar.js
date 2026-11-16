const reviewDialog = document.getElementById('supplier-review-dialog');
if (reviewDialog) {
    let request;
    let opener;
    let reviewApplication;
    let refreshListOnClose = false;
    const content = reviewDialog.querySelector('[data-supplier-content]');
    const modal = bootstrap.Modal.getOrCreateInstance(reviewDialog);

    reviewDialog.addEventListener('hidden.bs.modal', () => {
        request?.abort();
        if (refreshListOnClose) {
            window.location.reload();
            return;
        }
        opener?.focus();
    });

    function renderSupplierDetails(markup) {
        const page = new DOMParser().parseFromString(markup, 'text/html');
        const flow = page.querySelector('[data-review-flow]');
        if (!flow) throw new Error('Supplier details unavailable');
        if (!reviewApplication) flow.querySelector('[data-review-step="Decision"]')?.remove();
        content.replaceChildren(flow);
        initializeReview(flow);
    }

    document.addEventListener('click', async event => {
        const link = event.target.closest('[data-supplier-dialog]');
        if (!link || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        event.preventDefault();
        opener = link;
        request?.abort();
        content.removeAttribute('aria-busy');
        const pending = new AbortController();
        request = pending;
        reviewDialog.querySelector('.modal-title').textContent = link.dataset.company;
        const loading = document.createElement('p');
        loading.className = 'text-secondary mb-0';
        loading.setAttribute('role', 'status');
        loading.textContent = 'Loading supplier details…';
        content.replaceChildren(loading);
        modal.show();
        const url = new URL(link.href);
        reviewApplication = url.searchParams.get('reviewApplication') === 'true';

        try {
            const response = await fetch(url, { signal: pending.signal });
            if (!response.ok || response.redirected) throw new Error('Supplier details unavailable');
            const markup = await response.text();
            if (!pending.signal.aborted) renderSupplierDetails(markup);
        } catch {
            if (pending.signal.aborted) return;
            const fallback = document.createElement('a');
            fallback.href = link.href;
            fallback.textContent = 'Open supplier details';
            const error = document.createElement('p');
            error.className = 'text-secondary';
            error.setAttribute('role', 'alert');
            error.textContent = 'Supplier details could not load here.';
            content.replaceChildren(error, fallback);
        }
    });

    content.addEventListener('submit', async event => {
        const form = event.target;
        if (event.defaultPrevented || !form.matches('[data-supplier-popup-form]')) return;
        event.preventDefault();
        if (content.getAttribute('aria-busy') === 'true') return;

        const pending = new AbortController();
        request = pending;
        const button = event.submitter;
        const data = new FormData(form);
        if (button) button.disabled = true;
        content.setAttribute('aria-busy', 'true');
        form.querySelector('[data-supplier-save-error]')?.remove();
        try {
            const response = await fetch(form.action, { method: 'POST', body: data, signal: pending.signal });
            if (!response.ok) throw new Error('Supplier update failed');
            const markup = await response.text();
            if (pending.signal.aborted) return;
            renderSupplierDetails(markup);
            if (content.querySelector('[data-supplier-review-saved]')) refreshListOnClose = true;
            const focusTarget = content.querySelector('.validation-summary-errors, [data-supplier-review-saved], .alert-warning');
            if (focusTarget) {
                focusTarget.tabIndex = -1;
                focusTarget.focus();
            }
        } catch {
            if (pending.signal.aborted) return;
            const error = document.createElement('p');
            error.dataset.supplierSaveError = '';
            error.className = 'text-danger';
            error.setAttribute('role', 'alert');
            error.textContent = 'The result could not be confirmed. Reopen supplier details to check the saved values.';
            form.prepend(error);
        } finally {
            if (request === pending) content.removeAttribute('aria-busy');
            if (button) button.disabled = false;
        }
    });
}

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
    if (!flow || flow.dataset.reviewInitialized) return;
    flow.dataset.reviewInitialized = 'true';
    const steps = [...flow.querySelectorAll('[data-review-step]')];
    if (!steps.length) return;
    const stepper = document.createElement('ol');
    stepper.className = 'admin-review-stepper';
    stepper.setAttribute('aria-label', 'Supplier review progress');
    const markers = steps.map((step, index) => {
        const item = document.createElement('li');
        const number = document.createElement('span'); number.textContent = String(index + 1);
        const label = document.createElement('strong'); label.textContent = step.dataset.reviewStep;
        item.append(number, label); stepper.append(item); return item;
    });
    const controls = document.createElement('div');
    controls.className = 'admin-review-controls';
    const progress = document.createElement('p');
    progress.className = 'admin-review-progress';
    progress.setAttribute('aria-live', 'polite');
    function button(text) {
        const element = document.createElement('button');
        element.type = 'button'; element.className = 'btn btn-outline-primary'; element.textContent = text;
        return element;
    }
    const back = button('Previous');
    const next = button('Review documents'); next.className = 'btn btn-primary';
    const done = button('Close details');
    done.setAttribute('data-bs-dismiss', 'modal');
    controls.append(back, next, done);
    const decisionActions = flow.querySelector('[data-decision-actions]');
    const heading = flow.querySelector('[data-review-overview]');
    if (heading) heading.after(stepper, progress); else flow.prepend(stepper, progress);
    flow.append(controls);
    const invalidIndex = steps.findIndex(step => step.querySelector('.field-validation-error,.validation-summary-errors'));
    let index = invalidIndex >= 0 ? invalidIndex : 0;
    function show(moveFocus = false) {
        steps.forEach((step, position) => { step.hidden = position !== index; });
        markers.forEach((marker, position) => {
            marker.classList.toggle('is-complete', position < index);
            if (position === index) marker.setAttribute('aria-current', 'step'); else marker.removeAttribute('aria-current');
        });
        progress.textContent = `Step ${index + 1} of ${steps.length} · ${steps[index].dataset.reviewStep}`;
        const isLastStep = index === steps.length - 1;
        if (isLastStep && decisionActions) { decisionActions.prepend(back); controls.hidden = true; }
        else { controls.prepend(back); controls.hidden = false; }
        back.hidden = index === 0;
        next.hidden = isLastStep;
        next.textContent = index === 0 ? 'Review documents' : 'Continue to decision';
        done.hidden = !isLastStep || Boolean(decisionActions) || !flow.closest('.modal');
        if (moveFocus) {
            const title = steps[index].querySelector('h2');
            title.tabIndex = -1; title.focus();
            flow.closest('.modal-body')?.scrollTo(0, 0);
        }
    }
    back.addEventListener('click', () => { index--; show(true); });
    next.addEventListener('click', () => { index++; show(true); });
    const decision = flow.querySelector('[name="Review.Status"]');
    const submit = flow.querySelector('[data-review-decision-submit]');
    const labels = { Approved: 'Approve supplier', Rejected: 'Reject application', ChangesRequested: 'Request changes', Suspended: 'Suspend supplier', Pending: 'Keep pending' };
    function updateDecision() { if (submit) submit.textContent = labels[decision?.value] || 'Save decision'; }
    decision?.addEventListener('change', updateDecision);
    updateDecision(); show();
}

document.querySelectorAll('[data-review-flow]').forEach(initializeReview);

function applyLocalPage(results, filter) {
    const pager = results.querySelector('[data-local-pager]');
    if (!pager) return;
    const rows = [...results.querySelectorAll('[data-admin-row]')];
    const previous = pager.querySelector('[data-page-previous]');
    const next = pager.querySelector('[data-page-next]');
    const summary = pager.querySelector('[data-page-summary]');
    const search = filter?.querySelector('[name="search"]');
    const status = filter?.querySelector('select[name]');
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
document.querySelectorAll('[data-paginated-report]').forEach(report => applyLocalPage(report));
