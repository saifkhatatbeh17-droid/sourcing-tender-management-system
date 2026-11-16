// Integrates the shared camera code and modal wrapper with synthetic DOM/Bootstrap/camera doubles.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const cameraCode = fs.readFileSync(path.join(__dirname, '../wwwroot/js/face-verification.js'), 'utf8');
const modalCode = fs.readFileSync(path.join(__dirname, '../wwwroot/js/face-login-modal.js'), 'utf8');
let count = 0;
function check(value, label) { assert.ok(value, label); count++; console.log(`PASS MODAL JS: ${label}`); }
function setup(mode = 'match') {
    const events = {}, elements = {};
    let cameraCalls = 0, stopped = false, posted = null, signal = null, shown = 0, pending, redirectTimer;
    const redirects = [];
    for (const id of ['face-login-modal', 'verification-form', 'verification-video', 'verification-feedback', 'verification-result', 'verification-progress', 'verification-start', 'verification-cancel']) {
        elements[id] = { textContent: '', value: 0, dataset: {}, disabled: false,
            addEventListener: (event, fn) => { events[`${id}:${event}`] = fn; },
            dispatchEvent: event => events[`${id}:${event.type}`]?.(event) };
    }
    if (mode === 'missing') delete elements['verification-form'];
    else {
        elements['verification-form'].dataset = { verifyUrl: '/FaceVerification/Verify', modelUrl: '/models', modelIdentifier: 'synthetic-model' };
        if (mode.startsWith('auto-')) elements['verification-form'].dataset.successRedirect = '/Account/GoToDashboard';
        elements['verification-form'].querySelector = () => ({ value: 'synthetic-csrf' });
        elements['verification-form'].requestSubmit = () => { pending = events['verification-form:submit']({ preventDefault() {} }); };
    }
    Object.assign(elements['verification-video'], { videoWidth: 640, videoHeight: 480, play: async () => {} });
    const modal = { show: () => { shown++; events['face-login-modal:shown.bs.modal']?.(); }, hide: () => events['face-login-modal:hide.bs.modal']?.() };
    const bootstrap = { Modal: { getOrCreateInstance: () => modal } };
    const faceapi = { nets: Object.fromEntries(['tinyFaceDetector', 'faceLandmark68Net', 'faceRecognitionNet'].map(key => [key, { loadFromUri: async () => {} }])), TinyFaceDetectorOptions: class {}, detectAllFaces: () => ({ withFaceLandmarks: () => ({ withFaceDescriptors: async () => {
        if (mode === 'close-stream') modal.hide();
        return [{ descriptor: Array(128).fill(0.01), detection: { box: { width: 250, height: 250 } } }];
    } }) }) };
    const context = { Event, AbortController, URL, bootstrap, faceapi,
        window: { location: { href: 'http://localhost/Account/Login', origin: 'http://localhost', assign: url => redirects.push(url) }, isSecureContext: true, faceapi, bootstrap, addEventListener: (event, fn) => { events[event] = fn; } },
        document: { hidden: false, getElementById: id => elements[id], addEventListener: (event, fn) => { events[event] = fn; } },
        navigator: { mediaDevices: { getUserMedia: async () => {
            cameraCalls++;
            if (mode === 'denied') throw Object.assign(new Error('permission'), { name: 'NotAllowedError' });
            if (mode === 'close-permission') modal.hide();
            return { getTracks: () => [{ stop: () => { stopped = true; } }] };
        } } }, setTimeout: (resolve, delay) => { if (delay === 850) redirectTimer = resolve; else resolve(); }, fetch: async (url, options) => {
            posted = JSON.parse(options.body); signal = options.signal;
            if (mode === 'network') throw new Error('Network unavailable');
            if (mode === 'close-request') modal.hide();
            return { ok: true, json: async () => ({ status: mode.endsWith('mismatch') ? 'Not Matched' : 'Matched' }) };
        } };
    if (mode !== 'missing-script') vm.runInNewContext(cameraCode, context);
    const beforeModal = cameraCalls;
    vm.runInNewContext(modalCode, context);
    return { elements, modal, beforeModal, redirects, fireRedirect: () => redirectTimer?.(), finished: async () => { if (pending) await pending; }, get shown() { return shown; }, get cameraCalls() { return cameraCalls; }, get stopped() { return stopped; }, get posted() { return posted; }, get signal() { return signal; } };
}
(async () => {
    const match = setup(); await match.finished();
    check(match.beforeModal === 0, 'No camera initialization before requested modal flow');
    check(match.shown === 1 && match.cameraCalls === 1, 'Modal opens automatically and starts requested camera flow');
    check(match.elements['verification-result'].dataset.state === 'matched', 'Match selects animated green success state');
    check(match.stopped && match.elements['verification-video'].srcObject === null, 'Camera tracks stop after verification');
    check(match.posted.descriptor.length === 128 && !Object.hasOwn(match.posted, 'userId'), 'Protected comparison reuses descriptor-only request');
    match.modal.hide(); match.modal.show(); await match.finished();
    check(match.cameraCalls === 1, 'Reopening modal waits for explicit Start camera retry');
    const mismatch = setup('mismatch'); await mismatch.finished();
    check(mismatch.elements['verification-result'].dataset.state === 'not-matched', 'Nonmatch selects red failure state');
    const denied = setup('denied'); await denied.finished();
    check(denied.elements['verification-result'].dataset.state === 'failed' && !denied.elements['verification-start'].disabled, 'Permission failure leaves Start camera fallback available');
    const missing = setup('missing'); await missing.finished();
    check(missing.shown === 1 && missing.cameraCalls === 0 && !missing.posted, 'Missing enrollment opens modal without camera or comparison');
    const pending = setup('close-permission'); await pending.finished();
    check(pending.stopped && !pending.posted, 'Closing during permission stops delayed camera stream');
    const active = setup('close-stream'); await active.finished();
    check(active.stopped && !active.posted, 'Closing modal stops active camera before comparison');
    const inflight = setup('close-request'); await inflight.finished();
    check(inflight.signal.aborted && inflight.elements['verification-result'].textContent === '', 'Closing aborts request and ignores late response');
    const network = setup('network'); await network.finished();
    check(network.stopped && network.elements['verification-result'].dataset.state === 'failed', 'Network error stops camera and shows failure');
    const noScript = setup('missing-script'); await noScript.finished();
    check(noScript.cameraCalls === 0 && !noScript.posted && noScript.elements['verification-start'].disabled && noScript.elements['verification-result'].dataset.state === 'failed', 'Missing camera script fails safely without native form submission');
    const automatic = setup('auto-match'); await automatic.finished(); automatic.fireRedirect();
    check(automatic.stopped && automatic.redirects[0] === 'http://localhost/Account/GoToDashboard', 'Matching automatically continues through role dispatcher after stopping camera');
    const cancelledSuccess = setup('auto-cancel'); await cancelledSuccess.finished(); cancelledSuccess.modal.hide(); cancelledSuccess.fireRedirect();
    check(cancelledSuccess.redirects.length === 0, 'Closing modal cancels pending success navigation');
    const automaticMismatch = setup('auto-mismatch'); await automaticMismatch.finished(); automaticMismatch.fireRedirect();
    check(automaticMismatch.redirects.length === 0, 'Nonmatch does not automatically navigate');
    console.log(`Face modal client checks: ${count} passed.`);
})().catch(error => { console.error(error.message); process.exitCode = 1; });
