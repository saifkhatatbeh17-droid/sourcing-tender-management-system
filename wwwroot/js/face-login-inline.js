(() => {
    'use strict';
    const button = document.getElementById('face-login-button');
    const host = document.getElementById('face-login-host');
    if (!button || !host) return;
    const form = button.form;
    const error = document.getElementById('face-login-error');
    const continueLink = document.getElementById('face-login-continue');
    const scripts = new Map();
    let busy = false, signedIn = false, modalUrl = null;
    const localUrl = value => {
        const url = new URL(value, window.location.href);
        if (url.origin !== window.location.origin) throw new Error('Unable to load Face ID. Please reload the page.');
        return url.href;
    };
    const loadScript = value => {
        const url = localUrl(value);
        if (scripts.has(url)) return scripts.get(url);
        const promise = new Promise((resolve, reject) => {
            const script = document.createElement('script');
            script.src = url;
            script.onload = resolve;
            script.onerror = () => { scripts.delete(url); script.remove(); reject(new Error('Camera controls could not load. Try again or continue to your workspace.')); };
            document.body.appendChild(script);
        });
        scripts.set(url, promise);
        return promise;
    };
    async function openModal() {
        const setup = document.getElementById('face-setup-modal');
        if (setup && window.bootstrap?.Modal && setup.dataset.setupReady === 'true') { bootstrap.Modal.getOrCreateInstance(setup).show(); return; }
        const existing = document.getElementById('face-login-modal');
        if (existing && window.bootstrap?.Modal && (!document.getElementById('verification-form') || document.getElementById('verification-form').dataset.verificationReady === 'true')) {
            bootstrap.Modal.getOrCreateInstance(existing).show(); return;
        }
        if (!existing && !setup) {
            const response = await fetch(localUrl(modalUrl), { credentials: 'same-origin', redirect: 'error', headers: { Accept: 'text/html' } });
            if (!response.ok) throw new Error('Your session is unavailable. Reload the page and sign in again.');
            host.innerHTML = await response.text();
        }
        if (document.getElementById('face-setup-modal')) {
            if (!window.faceapi) await loadScript(host.dataset.faceApiUrl);
            await loadScript(host.dataset.enrollmentJs);
            await loadScript(host.dataset.setupJs);
            return;
        }
        if (document.getElementById('verification-form')) {
            if (!window.faceapi) await loadScript(host.dataset.faceApiUrl);
            await loadScript(host.dataset.verificationJs);
        }
        await loadScript(host.dataset.modalJs);
    }
    async function run() {
        if (busy) return;
        if (!signedIn && !form.reportValidity()) return;
        busy = true; button.disabled = true; error.hidden = true; error.textContent = '';
        const label = button.textContent; button.textContent = signedIn ? 'Opening camera…' : 'Checking password…';
        try {
            if (!signedIn) {
                const response = await fetch(localUrl(button.formAction), { method: 'POST', credentials: 'same-origin', redirect: 'error',
                    headers: { Accept: 'application/json' }, body: new FormData(form) });
                const result = await response.json().catch(() => ({}));
                if (!response.ok) throw new Error(response.status === 429 ? 'Too many attempts. Wait a minute and try again.' : result.message || 'Unable to sign in. Check your email and password, or try again later.');
                if (!result.modalUrl) throw new Error('Unable to open Face ID. Reload the page.');
                modalUrl = localUrl(result.modalUrl); signedIn = true;
                form.querySelector('input[name="Password"]').value = '';
                button.type = 'button'; continueLink.hidden = false;
            }
            await openModal();
        } catch (failure) { error.textContent = failure.message || 'Unable to open Face ID. Try again.'; error.hidden = false; }
        finally { form.querySelector('input[name="Password"]').value = ''; button.textContent = label; button.disabled = false; busy = false; }
    }
    form.addEventListener('submit', event => {
        if (event.submitter !== button) return;
        event.preventDefault(); void run();
    });
    button.addEventListener('click', event => { if (signedIn) { event.preventDefault(); void run(); } });
})();
