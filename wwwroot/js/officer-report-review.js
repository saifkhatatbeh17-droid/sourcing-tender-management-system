const decisionModal = document.getElementById('officer-decision-modal');
decisionModal?.addEventListener('show.bs.modal', event => {
    const form = decisionModal.querySelector('form');
    form.reset();
    const rejecting = event.relatedTarget?.dataset.reviewDecision === 'Reject';
    form.elements.decision.value = rejecting ? 'Reject' : 'RequestEdits';
    decisionModal.querySelector('#officer-decision-title').textContent = rejecting ? 'Reject report' : 'Request edits';
    decisionModal.querySelector('#officer-decision-help').textContent = rejecting ? 'This will reject the report and cancel the tender. Enter the reason to confirm.' : 'Explain what the committee should change. The report will need four fresh approvals.';
    const submit = decisionModal.querySelector('[data-confirm-review]');
    submit.textContent = rejecting ? 'Reject and cancel tender' : 'Send edit request';
    submit.className = rejecting ? 'btn btn-danger' : 'btn btn-primary';
});
decisionModal?.addEventListener('shown.bs.modal', () => decisionModal.querySelector('textarea').focus());
