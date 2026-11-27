// The 'set up face sign-in' pop-up shown after login.
(() => {
    'use strict';
    const element = document.getElementById('face-setup-modal');
    const form = document.getElementById('face-form');
    if (!element || !form || !window.bootstrap?.Modal) return;
    let leaving = false;
    async function decline() {
        if (leaving) return;
        leaving = true; form.dispatchEvent(new Event('faceenrollment:cancel'));
        try {
            const body = new FormData(); body.append('__RequestVerificationToken', form.querySelector('input[name="__RequestVerificationToken"]').value);
            const response = await fetch(form.dataset.declineUrl, { method: 'POST', credentials: 'same-origin', redirect: 'error', headers: { Accept: 'application/json' }, body });
            if (!response.ok) throw new Error();
            window.location.assign(form.dataset.successRedirect);
        } catch {
            leaving = false; document.getElementById('face-message').textContent = "Couldn't continue. Please try again.";
        }
    }
    element.addEventListener('hide.bs.modal', event => { event.preventDefault(); void decline(); });
    document.getElementById('face-setup-skip').addEventListener('click', () => void decline());
    element.dataset.setupReady = 'true';
    bootstrap.Modal.getOrCreateInstance(element).show();
})();
