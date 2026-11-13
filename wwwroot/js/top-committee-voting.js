(() => {
    const form = document.getElementById('committee-vote-form');
    if (!form || !window.bootstrap) return;
    const modalElement = document.getElementById('vote-confirmation');
    const modal = bootstrap.Modal.getOrCreateInstance(modalElement);
    const notes = document.getElementById('vote-notes');
    const confirmed = document.getElementById('vote-confirmed');
    const confirmButton = document.getElementById('confirm-vote');
    const reviewButton = document.getElementById('review-vote');
    let submitting = false;

    function decision() { return form.querySelector('input[name="Vote.Decision"]:checked')?.value; }
    function validate() {
        notes.required = decision() === 'Rejected';
        notes.setCustomValidity(notes.required && !notes.value.trim() ? 'A rejection justification is required.' : '');
        return form.reportValidity();
    }
    function review() {
        if (!validate()) return;
        confirmed.value = 'false';
        document.getElementById('vote-confirmation-title').textContent = decision() === 'Rejected' ? 'Confirm rejection' : 'Confirm approval';
        document.getElementById('vote-confirmation-description').textContent = decision() === 'Rejected'
            ? 'Reject this tender? One rejection permanently ends committee voting and returns the tender to Sourcing Manager review.'
            : 'Approve the nominated bid? The fourth valid approval automatically awards the tender.';
        modal.show();
    }
    form.addEventListener('change', () => { confirmed.value = 'false'; notes.setCustomValidity(''); notes.required = decision() === 'Rejected'; });
    notes.addEventListener('input', () => notes.setCustomValidity(''));
    reviewButton.addEventListener('click', review);
    confirmButton.addEventListener('click', () => { confirmed.value = 'true'; });
    form.addEventListener('submit', event => {
        if (submitting) { event.preventDefault(); return; }
        if (confirmed.value !== 'true' || !modalElement.classList.contains('show')) {
            event.preventDefault(); review(); return;
        }
        if (!validate()) { event.preventDefault(); confirmed.value = 'false'; modal.hide(); return; }
        submitting = true;
        confirmButton.disabled = true;
        reviewButton.disabled = true;
    });
    modalElement.addEventListener('hidden.bs.modal', () => {
        if (!submitting) confirmed.value = 'false';
        reviewButton.focus();
    });
    window.addEventListener('pageshow', () => {
        submitting = false; confirmed.value = 'false';
        confirmButton.disabled = false; reviewButton.disabled = false;
    });
})();
