// Face sign-in pop-up: camera preview and matching.
(() => {
    'use strict';
    const element = document.getElementById('face-login-modal');
    if (!element || !window.bootstrap?.Modal) return;
    const form = document.getElementById('verification-form');
    let attempted = false;
    element.addEventListener('hide.bs.modal', () => {
        form?.dispatchEvent(new Event('faceverification:cancel'));
    });
    element.addEventListener('shown.bs.modal', () => {
        if (attempted || !form) return;
        attempted = true;
        if (form.dataset.verificationReady !== 'true') {
            const result = document.getElementById('verification-result');
            result.textContent = 'Verification Failed. Camera controls could not load. Reload this page or continue to your workspace.';
            result.dataset.state = 'failed';
            document.getElementById('verification-start').disabled = true;
            return;
        }
        // Camera permission remains browser-controlled. Start camera is always available for retry.
        form.requestSubmit();
    });
    bootstrap.Modal.getOrCreateInstance(element).show();
})();
