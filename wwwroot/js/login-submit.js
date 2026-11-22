/* Progressive enhancement: ordinary POST remains available when JavaScript is unavailable. */
(() => {
    const form = document.getElementById('password-login');
    if (!form || !window.fetch || !window.AbortController) return;
    // MVC omits a ModelOnly summary entirely until the first server-side error.
    const summary = form.querySelector('.login-error') || Object.assign(document.createElement('div'), {
        className: 'login-error', hidden: true
    });
    summary.setAttribute('role', 'alert');
    if (!summary.parentElement) form.prepend(summary);
    const password = form.querySelector('[name="Password"]');
    if (!summary || !password) return;
    let busy = false, navigating = false;
    let disabledBefore = new Map();
    const signal = state => document.dispatchEvent(new CustomEvent('login-access', { detail: state }));
    const validator = () => window.jQuery?.(form).data('validator');

    function message(text, errors = {}) {
        const fields = Object.fromEntries(Object.entries(errors)
            .filter(([key]) => key === 'Email' || key === 'Password')
            .map(([key, values]) => [key, values[0]]));
        validator()?.showErrors(fields);
        summary.replaceChildren();
        const list = document.createElement('ul'), item = document.createElement('li');
        item.textContent = text; list.appendChild(item); summary.appendChild(list);
        summary.classList.remove('validation-summary-valid');
        summary.classList.add('validation-summary-errors');
        summary.hidden = false;
    }
    function lock() {
        busy = true; form.setAttribute('aria-busy', 'true');
        disabledBefore = new Map(Array.from(document.querySelectorAll('#login-stage button, #password-login input:not([type="hidden"])'))
            .map(element => [element, element.disabled]));
        disabledBefore.forEach((_, element) => { element.disabled = true; });
    }
    function unlock() {
        disabledBefore.forEach((disabled, element) => { element.disabled = disabled; });
        disabledBefore.clear(); busy = false; navigating = false; form.removeAttribute('aria-busy');
    }
    form.addEventListener('submit', async event => {
        if (event.submitter?.id === 'face-login-button') return;
        event.preventDefault();
        if (busy || document.getElementById('face-signin-modal')?.classList.contains('show')) return;
        if (!form.reportValidity() || (window.jQuery && !window.jQuery(form).valid())) return;
        const payload = new FormData(form); // Includes the server-issued antiforgery token and ReturnUrl.
        validator()?.resetForm(); summary.replaceChildren(); summary.hidden = true;
        summary.classList.remove('validation-summary-errors'); summary.classList.add('validation-summary-valid');
        lock(); signal('pending');
        const controller = new AbortController();
        const timeout = setTimeout(() => controller.abort(), 20000);
        try {
            const response = await fetch(form.action, {
                method: 'POST', body: payload, credentials: 'same-origin', redirect: 'error', cache: 'no-store',
                headers: { Accept: 'application/json', 'X-Requested-With': 'XMLHttpRequest' }, signal: controller.signal
            });
            const result = response.headers.get('content-type')?.includes('application/json')
                ? await response.json() : null;
            if (response.ok && result?.redirectUrl) {
                const destination = new URL(result.redirectUrl, location.href);
                if (destination.origin !== location.origin) throw new Error('Unexpected sign-in response.');
                navigating = true; password.value = ''; signal('verified');
                location.assign(destination.href);
                return;
            }
            password.value = '';
            if (result?.message && (response.status === 400 || response.status === 401)) {
                message(result.message, result.errors); signal('failed');
            } else {
                message(response.status === 400
                    ? 'Your sign-in page expired. Reload the page and try again.'
                    : 'Sign-in could not finish. Please try again.'); signal('idle');
            }
        } catch {
            password.value = '';
            message('Connection interrupted. Please try again.'); signal('idle');
        } finally {
            clearTimeout(timeout);
            if (!navigating) unlock();
        }
    });
    window.addEventListener('pageshow', event => { if (event.persisted) unlock(); });
})();
