(() => {
    const identity = document.querySelector('.login-identity');
    const form = document.getElementById('password-login');
    const message = document.getElementById('access-message');
    if (!identity || !form) return;
    const messages = { idle: 'Ready to sign in', pending: 'Checking your sign-in request…', verified: 'Verified. Opening your workspace…', failed: 'Please review your details and try again' };
    function state(value) {
        identity.dataset.access = value;
        if (message) message.textContent = messages[value] || messages.idle;
    }
    document.addEventListener('login-access', event => state(event.detail));
    form.querySelectorAll('input:not([type="hidden"])').forEach(input => {
        input.addEventListener('focus', () => { identity.dataset.focus = input.id.toLowerCase(); });
        input.addEventListener('blur', () => { delete identity.dataset.focus; });
    });
    window.addEventListener('pageshow', () => {
        state(document.querySelector('.validation-summary-errors,.field-validation-error') ? 'failed' : 'idle');
    });
    form.addEventListener('submit', event => {
        if (event.defaultPrevented || event.submitter?.id === 'face-login-button') return;
        if (window.jQuery && !window.jQuery(form).valid()) return;
        state('pending');
    });
})();
