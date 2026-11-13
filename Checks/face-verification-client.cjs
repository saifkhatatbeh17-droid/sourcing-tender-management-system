// Synthetic DOM/camera simulations; not a real-browser or liveness test.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const source = fs.readFileSync(path.join(__dirname, '../wwwroot/js/face-verification.js'), 'utf8');
let count = 0;
function check(value, label) { assert.ok(value, label); count++; console.log(`PASS VERIFICATION JS: ${label}`); }
function setup(mode = 'match') {
    const events = {}, elements = {};
    let cameraCalls = 0, stopped = false, posted = null, detectionCalls = 0, signal = null;
    for (const id of ['verification-form', 'verification-video', 'verification-feedback', 'verification-result', 'verification-progress', 'verification-start', 'verification-cancel'])
        elements[id] = { textContent: '', dataset: {}, value: 0, disabled: false, addEventListener: (event, fn) => { events[`${id}:${event}`] = fn; } };
    elements['verification-form'].dataset = { verifyUrl: '/FaceVerification/Verify', modelUrl: '/models', modelIdentifier: 'synthetic-model' };
    elements['verification-form'].querySelector = () => ({ value: 'synthetic-csrf-token' });
    Object.assign(elements['verification-video'], { videoWidth: 640, videoHeight: 480, play: async () => {} });
    const faceapi = { nets: Object.fromEntries(['tinyFaceDetector', 'faceLandmark68Net', 'faceRecognitionNet'].map(key => [key, { loadFromUri: async () => {} }])), TinyFaceDetectorOptions: class {}, detectAllFaces: () => ({ withFaceLandmarks: () => ({ withFaceDescriptors: async () => {
        detectionCalls++;
        const face = { descriptor: Array(mode === 'invalid' ? 127 : 128).fill(0.01), detection: { box: { width: 250, height: 250 } } };
        if (mode === 'multiple' && detectionCalls === 1) return [face, face];
        if (mode === 'no-face' && detectionCalls === 1) return [];
        return [face];
    } }) }) };
    const context = { AbortController, window: { isSecureContext: mode !== 'insecure', faceapi, addEventListener: (event, fn) => { events[event] = fn; } }, faceapi,
        document: { hidden: false, getElementById: id => elements[id], addEventListener: (event, fn) => { events[event] = fn; } },
        navigator: { mediaDevices: { getUserMedia: async constraints => {
            cameraCalls++; assert.equal(constraints.audio, false);
            if (mode === 'denied') throw Object.assign(new Error('denied'), { name: 'NotAllowedError' });
            if (mode === 'unavailable') throw Object.assign(new Error('missing'), { name: 'NotFoundError' });
            if (mode === 'cancel') events['verification-cancel:click']();
            return { getTracks: () => [{ stop: () => { stopped = true; } }] };
        } } }, setTimeout: resolve => resolve(), fetch: async (url, options) => {
            posted = JSON.parse(options.body); signal = options.signal;
            assert.equal(options.headers.RequestVerificationToken, 'synthetic-csrf-token');
            assert.equal(options.credentials, 'same-origin');
            if (mode === 'cancel-request') events['verification-cancel:click']();
            return { ok: mode !== 'limited' && mode !== 'server-failure', status: mode === 'limited' ? 429 : mode === 'server-failure' ? 400 : 200,
                json: async () => ({ status: mode === 'mismatch' ? 'Not Matched' : mode === 'unexpected' ? 'Anything' : 'Matched' }) };
        } };
    vm.runInNewContext(source, context);
    return { elements, run: () => events['verification-form:submit']({ preventDefault() {} }), get cameraCalls() { return cameraCalls; }, get stopped() { return stopped; }, get posted() { return posted; }, get detectionCalls() { return detectionCalls; }, get signal() { return signal; } };
}
(async () => {
    const match = setup(); await match.run();
    check(match.elements['verification-result'].textContent.startsWith('Matched.'), 'Match displayed with informational warning');
    check(match.posted.descriptor.length === 128 && match.posted.modelIdentifier === 'synthetic-model', 'Single 128D descriptor and model sent');
    check(!Object.hasOwn(match.posted, 'userId') && !Object.hasOwn(match.posted, 'matched'), 'No owner ID or client match decision submitted');
    check(match.stopped && match.elements['verification-video'].srcObject === null, 'Camera stopped after success');
    check(match.elements['verification-progress'].value === 3, 'Progress completed');
    const mismatch = setup('mismatch'); await mismatch.run(); check(mismatch.elements['verification-result'].textContent.startsWith('Not Matched.'), 'Nonmatch distinguished from failure');
    const denied = setup('denied'); await denied.run(); check(denied.elements['verification-result'].textContent.includes('permission') && !denied.posted, 'Permission denial handled');
    const unavailable = setup('unavailable'); await unavailable.run(); check(unavailable.elements['verification-result'].textContent.includes('No camera'), 'Unavailable device handled');
    const insecure = setup('insecure'); await insecure.run(); check(insecure.cameraCalls === 0 && !insecure.posted, 'Insecure context blocked');
    const cancel = setup('cancel'); await cancel.run(); check(cancel.stopped && !cancel.posted, 'Delayed camera stream stopped after cancellation');
    const cancelRequest = setup('cancel-request'); await cancelRequest.run(); check(cancelRequest.signal.aborted && cancelRequest.elements['verification-result'].textContent === '', 'Cancellation aborts request and ignores late response');
    const multiple = setup('multiple'); await multiple.run(); check(multiple.detectionCalls === 2 && multiple.posted, 'Multiple faces skipped before single-face capture');
    const noFace = setup('no-face'); await noFace.run(); check(noFace.detectionCalls === 2 && noFace.posted, 'No-face frame skipped');
    const invalid = setup('invalid'); await invalid.run(); check(!invalid.posted && invalid.stopped, 'Invalid descriptor not submitted and camera stopped');
    const limited = setup('limited'); await limited.run(); check(limited.elements['verification-result'].textContent.includes('Too many attempts'), 'Rate-limit feedback');
    const failure = setup('server-failure'); await failure.run(); check(failure.elements['verification-result'].textContent.startsWith('Verification Failed.'), 'Safe server failure feedback');
    const unexpected = setup('unexpected'); await unexpected.run(); check(unexpected.elements['verification-result'].textContent.startsWith('Verification Failed.'), 'Unknown server result rejected');
    check(!match.elements['verification-start'].disabled && match.elements['verification-cancel'].disabled, 'Controls reset after completion');
    console.log(`Face verification client checks: ${count} passed.`);
})().catch(error => { console.error(error.message); process.exitCode = 1; });
