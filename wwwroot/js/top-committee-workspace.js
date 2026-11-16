/* Search assigned tenders without hiding the primary control or changing server routes. */
(() => {
    document.querySelectorAll('[data-top-search]').forEach(input => {
        const scope = input.closest('.workspace-section');
        const rows = [...scope.querySelectorAll(input.dataset.topSearch)];
        const feedback = scope.querySelector('[data-top-search-status]');
        input.addEventListener('input', () => {
            const query = input.value.trim().toLocaleLowerCase();
            rows.forEach(row => { row.hidden = !row.textContent.toLocaleLowerCase().includes(query); });
            const count = rows.filter(row => !row.hidden).length;
            feedback.hidden = !query;
            feedback.textContent = count ? `${count} matching tenders` : 'No tenders match. Clear the search to see all assignments.';
        });
    });
    function revealConversation() {
        if (!location.hash) return;
        const target = document.getElementById(decodeURIComponent(location.hash.slice(1)));
        const details = target?.closest('details.tc-conversation');
        if (details) { details.open = true; target.scrollIntoView({block: 'start'}); }
    }
    revealConversation();
    window.addEventListener('hashchange', revealConversation);
})();
/* Keep each review step separate while preserving deep links and server forms. */
(() => {
    const review = document.querySelector('[data-tender-review]');
    if (!review) return;

    const panels = [...review.querySelectorAll('[data-review-panel]')];
    const navigation = review.querySelector('.tc-review-steps');
    const steps = [...navigation.querySelectorAll('a')];
    const dialogTriggers = [...review.querySelectorAll('[data-review-dialog-open]')];

    function openReviewDialog(trigger) {
        const dialog = document.getElementById(trigger.getAttribute('aria-controls'));
        if (dialog.open) return;

        trigger.focus({ preventScroll: true });
        dialog.querySelector('.tc-review-card-body').scrollTop = 0;
        dialog.showModal();
    }

    dialogTriggers.forEach(trigger => {
        const dialog = document.getElementById(trigger.getAttribute('aria-controls'));
        trigger.addEventListener('click', () => openReviewDialog(trigger));
        dialog.querySelectorAll('[data-review-dialog-close]').forEach(button => {
            button.addEventListener('click', () => dialog.close());
        });
    });

    function revealStep(hash, moveFocus = false) {
        const target = document.getElementById(hash.slice(1));
        const targetPanel = target?.closest('[data-review-panel]');
        const panel = panels.includes(targetPanel) ? targetPanel : panels[0];
        const trigger = target?.querySelector(':scope > [data-review-dialog-open]');
        const openDialog = review.querySelector('.tc-review-card-dialog[open]');
        if (openDialog && openDialog.id !== trigger?.getAttribute('aria-controls')) openDialog.close();

        panels.forEach(item => { item.hidden = item !== panel; });
        steps.forEach(step => {
            if (step.hash === '#' + panel.dataset.reviewPanel) step.setAttribute('aria-current', 'step');
            else step.removeAttribute('aria-current');
        });

        if (targetPanel === panel && target instanceof HTMLDetailsElement) target.open = true;

        if (moveFocus) {
            panel.focus({ preventScroll: true });
            (targetPanel === panel && target.id !== panel.dataset.reviewPanel ? target : navigation)
                .scrollIntoView({ block: 'start' });
        }

        if (trigger && targetPanel === panel) openReviewDialog(trigger);
    }

    review.addEventListener('click', event => {
        const link = event.target.closest('a[href^="#"]');
        if (!link || event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
        const target = document.getElementById(link.hash.slice(1));
        if (!panels.includes(target?.closest('[data-review-panel]'))) return;

        event.preventDefault();
        if (link.hash !== location.hash) history.pushState(null, '', link.hash);
        revealStep(link.hash, true);
    });

    window.addEventListener('popstate', () => revealStep(location.hash));
    window.addEventListener('hashchange', () => revealStep(location.hash));
    if (review.querySelector('.validation-summary-errors, [data-vote-result]')) revealStep('#voting');
    else {
        const initialHash = location.hash || (new URLSearchParams(location.search).has('bidId') ? '#selected-bid' : '');
        revealStep(initialHash, Boolean(initialHash));
    }
})();
