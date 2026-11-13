// DOM/camera simulations only: no real camera, biometric data, network or browser.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const crypto = require('node:crypto');
const source = fs.readFileSync(require('node:path').join(__dirname, '../wwwroot/js/face-enrollment.js'), 'utf8');
let checks = 0;
function check(value, label) { assert.ok(value, label); checks++; console.log(`PASS JS: ${label}`); }
function setup(mode) {
    const events = {}, elements = {};
    let cameraCalls = 0, stopped = false, posted = null, sample = 0;
    for (const id of ['face-form', 'face-video', 'face-feedback', 'face-message', 'face-password', 'face-progress', 'face-start', 'face-cancel', 'face-consent']) {
        elements[id] = { value: '', dataset: {}, checked: true, disabled: false, textContent: '', addEventListener: (event, fn) => { events[`${id}:${event}`] = fn; }, reportValidity: () => true };
    }
    elements['face-form'].dataset = { saveUrl: '/FaceEnrollment/Save', modelUrl: '/models', enrolled: 'false' };
    elements['face-form'].querySelector = () => ({ value: 'synthetic-antiforgery-token' });
    elements['face-password'].value = crypto.randomUUID();
    elements['face-video'].videoWidth = 640; elements['face-video'].videoHeight = 480;
    elements['face-video'].play = async () => {};
    if (mode === 'consent') elements['face-consent'].checked = false;
    const location = { pathname: '/FaceEnrollment/Index', replace: url => { location.destination = url; } };
    const faceapi = { nets: Object.fromEntries(['tinyFaceDetector', 'faceLandmark68Net', 'faceRecognitionNet'].map(key => [key, { loadFromUri: async () => {} }])), TinyFaceDetectorOptions: class {}, detectAllFaces: () => ({ withFaceLandmarks: () => ({ withFaceDescriptors: async () => {
        sample++;
        return [{ descriptor: Array(128).fill(0.01 + sample * 0.001), detection: { box: { width: 250, height: 250 } } }];
    } }) }) };
    const window = { isSecureContext: mode !== 'insecure', faceapi, addEventListener: (event, fn) => { events[event] = fn; } };
    const context = { window, faceapi, location, document: { hidden: false, getElementById: id => elements[id] || null, addEventListener: (event, fn) => { events[event] = fn; } }, navigator: { mediaDevices: { getUserMedia: async constraints => {
        cameraCalls++; assert.equal(constraints.audio, false);
        if (mode === 'denied') throw Object.assign(new Error('denied'), { name: 'NotAllowedError' });
        if (mode === 'cancel') events['face-cancel:click']();
        return { getTracks: () => [{ stop: () => { stopped = true; } }] };
    } } }, setTimeout: resolve => resolve(), fetch: async (url, options) => { posted = JSON.parse(options.body); return { ok: true, json: async () => ({}) }; } };
    vm.runInNewContext(source, context);
    return { run: () => events['face-form:submit']({ preventDefault() {} }), elements, location, get cameraCalls() { return cameraCalls; }, get stopped() { return stopped; }, get posted() { return posted; } };
}
(async () => {
    const consent = setup('consent'); await consent.run(); check(consent.cameraCalls === 0 && !consent.posted, 'Missing consent never opens camera or posts');
    const insecure = setup('insecure'); await insecure.run(); check(insecure.cameraCalls === 0 && insecure.elements['face-message'].textContent.includes('HTTPS'), 'Insecure context rejected');
    const denied = setup('denied'); await denied.run(); check(!denied.posted && denied.elements['face-message'].textContent.includes('permission'), 'Permission denial handled');
    check(denied.elements['face-password'].value === '', 'Password cleared after failure');
    const cancel = setup('cancel'); await cancel.run(); check(cancel.stopped && !cancel.posted, 'Cancellation during camera permission stops delayed stream without saving');
    const success = setup('success'); await success.run();
    check(success.posted?.descriptors.length === 5 && success.posted.descriptors.every(x => x.length === 128), 'Five descriptor samples submitted');
    check(!Object.hasOwn(success.posted, 'userId'), 'No owner ID submitted');
    check(success.stopped && success.elements['face-video'].srcObject === null, 'Camera stopped after enrollment');
    check(success.elements['face-password'].value === '', 'Password cleared after success');
    check(success.location.destination === '/FaceEnrollment/Index?saved=1', 'Success reloads safe enrollment route');
    console.log(`Face client simulations: ${checks} passed.`);
})().catch(error => { console.error(error.message); process.exitCode = 1; });
