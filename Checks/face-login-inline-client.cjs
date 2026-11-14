// Same-document login orchestration simulations; HTTP/Identity is exercised by FaceLoginChecks.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const crypto = require('node:crypto');
const source = fs.readFileSync(path.join(__dirname, '../wwwroot/js/face-login-inline.js'), 'utf8');
let count = 0;
function check(value, label) { assert.ok(value, label); count++; console.log(`PASS INLINE JS: ${label}`); }
function setup(mode = 'success') {
    const events = {}, requests = [], scripts = [];
    let modalExists = false, modalShown = 0, authenticated = false;
    const password = { value: crypto.randomUUID() };
    const form = { reportValidity: () => mode !== 'invalid', querySelector: () => password, addEventListener: (name, fn) => { events[name] = fn; } };
    const button = { setAttribute(name, value) { this[name] = String(value); }, removeAttribute(name) { delete this[name]; }, form, formAction: 'http://localhost/Account/LoginWithFaceId', textContent: 'Login with Face ID + Password', disabled: false, type: 'submit', addEventListener: (name, fn) => { events[`button:${name}`] = fn; } };
    const host = { dataset: { faceApiUrl: '/lib/face-api/face-api.js', verificationJs: '/js/face-verification.js', modalJs: '/js/face-login-modal.js', enrollmentJs: '/js/face-enrollment.js', setupJs: '/js/face-setup-modal.js' } };
    Object.defineProperty(host, 'innerHTML', { set: () => { modalExists = true; } });
    const error = { hidden: true, textContent: '' }, continuation = { hidden: true }, verificationForm = { dataset: {} }, setupElement = { dataset: {} };
    const nodes = { 'face-login-button': button, 'face-login-host': host, 'face-login-error': error, 'face-login-continue': continuation };
    const location = { href: 'http://localhost/Account/Login', origin: 'http://localhost' };
    const bootstrap = { Modal: { getOrCreateInstance: () => ({ show: () => { modalShown++; } }) } };
    const window = { location, bootstrap };
    const context = { CustomEvent: class { constructor(type, options) { this.type = type; this.detail = options?.detail; } }, URL, FormData: class { constructor(value) { assert.equal(value, form); } }, Map, window, bootstrap,
        document: { dispatchEvent() {},
            getElementById: id => id === 'face-setup-modal' ? (modalExists && mode === 'setup' ? setupElement : null) : id === 'face-login-modal' ? (modalExists && mode !== 'setup' ? {} : null) : id === 'verification-form' ? (modalExists && mode !== 'missing' && mode !== 'setup' ? verificationForm : null) : nodes[id],
            createElement: () => ({ remove() {} }), body: { appendChild: script => {
                assert.ok(authenticated, 'scripts must not load before password success'); scripts.push(script.src);
                queueMicrotask(() => {
                    if (mode === 'script-error') return script.onerror();
                    if (script.src.includes('face-api.js')) window.faceapi = {};
                    if (script.src.includes('face-verification.js')) verificationForm.dataset.verificationReady = 'true';
                    if (script.src.includes('face-login-modal.js')) modalShown++;
                    if (script.src.includes('face-setup-modal.js')) { setupElement.dataset.setupReady = 'true'; modalShown++; }
                    script.onload();
                });
            } }
        }, fetch: async (url, options) => {
            requests.push({ url, options });
            if (requests.length === 1) {
                if (mode === 'network') throw new Error('Network failure');
                const ok = mode !== 'wrong'; authenticated = ok;
                return { ok, status: ok ? 200 : 400, json: async () => ok ? { modalUrl: '/FaceSetup/Modal' } : { message: 'Unable to sign in.' } };
            }
            return { ok: mode !== 'fragment-error', text: async () => '<div>authenticated modal fragment</div>' };
        } };
    vm.runInNewContext(source, context);
    async function submit(face = true) {
        let prevented = false;
        events.submit({ submitter: face ? button : {}, preventDefault() { prevented = true; } });
        for (let i = 0; i < 20; i++) await new Promise(setImmediate);
        return prevented;
    }
    return { submit, button, password, error, continuation, requests, scripts, location, get shown() { return modalShown; } };
}
(async () => {
    const normal = setup(); check(!(await normal.submit(false)) && normal.requests.length === 0, 'Normal Sign in retains native form submission');
    const invalid = setup('invalid'); await invalid.submit(); check(invalid.requests.length === 0, 'Invalid fields do not authenticate or open camera');
    const success = setup(); check(await success.submit(), 'Face button intercepts native navigation');
    check(success.location.href === 'http://localhost/Account/Login' && success.shown === 1, 'Popup appears while browser remains on original Login document');
    check(success.requests[0].options.headers.Accept === 'application/json' && success.requests[0].options.credentials === 'same-origin', 'Password request uses existing cookie-enabled JSON response flow');
    check(success.requests[1].url === 'http://localhost/FaceSetup/Modal' && success.scripts.length === 3, 'Authenticated fragment and camera scripts loaded after password success');
    check(success.password.value === '' && !success.continuation.hidden, 'Password cleared and safe optional continuation provided');
    const wrong = setup('wrong'); await wrong.submit(); check(wrong.shown === 0 && wrong.scripts.length === 0 && wrong.requests.length === 1 && !wrong.error.hidden, 'Wrong password never opens modal or loads biometric code');
    const missing = setup('missing'); await missing.submit(); check(missing.shown === 1 && missing.scripts.length === 1, 'No enrollment shows modal without face models');
    const failed = setup('script-error'); await failed.submit(); check(failed.shown === 0 && !failed.button.disabled && !failed.error.hidden && failed.password.value === '', 'Script load failure is recoverable and does not retain password');
    const fragment = setup('fragment-error'); await fragment.submit(); check(fragment.scripts.length === 0 && !fragment.error.hidden, 'Protected fragment failure never initializes camera');
    const network = setup('network'); await network.submit(); check(network.shown === 0 && network.password.value === '' && !network.button.disabled, 'Network failure stays on Login and clears password');
    const setupFlow = setup('setup'); await setupFlow.submit(); check(setupFlow.shown === 1 && setupFlow.scripts.some(x => x.endsWith('face-enrollment.js')) && !setupFlow.scripts.some(x => x.endsWith('face-verification.js')), 'Unenrolled inline login reuses enrollment modal scripts on same Login document');
    console.log(`Face inline client checks: ${count} passed.`);
})().catch(error => { console.error(error.message); process.exitCode = 1; });
