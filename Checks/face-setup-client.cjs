const assert = require('node:assert/strict');
const fs = require('node:fs'), path = require('node:path'), vm = require('node:vm'), crypto = require('node:crypto');
const enrollment = fs.readFileSync(path.join(__dirname, '../wwwroot/js/face-enrollment.js'), 'utf8');
const setupModal = fs.readFileSync(path.join(__dirname, '../wwwroot/js/face-setup-modal.js'), 'utf8');
let count = 0;
function check(value, label) { assert.ok(value, label); count++; console.log(`PASS SETUP JS: ${label}`); }
function setup(mode = 'success') {
    const events = {}, elements = {}, redirects = [], writes = [];
    let stopped = false, camera = 0, shown = 0, sample = 0, timer;
    for (const id of ['face-form','face-video','face-feedback','face-message','face-password','face-progress','face-start','face-cancel','face-consent','face-setup-modal','face-setup-skip'])
        elements[id] = { value: '', textContent: '', dataset: {}, checked: true, disabled: false, addEventListener: (name, fn) => { events[`${id}:${name}`] = fn; }, dispatchEvent: event => events[`${id}:${event.type}`]?.(event) };
    elements['face-password'].value = crypto.randomUUID();
    elements['face-form'].dataset = { saveUrl: '/FaceEnrollment/Save', declineUrl: '/FaceSetup/Decline', successRedirect: '/Account/GoToDashboard', modelUrl: '/models', enrolled: 'false' };
    elements['face-form'].reportValidity = () => !!elements['face-password'].value;
    elements['face-form'].querySelector = () => ({ value: 'synthetic-csrf' });
    Object.assign(elements['face-video'], { videoWidth: 640, videoHeight: 480, play: async () => {} });
    const modal = { show: () => { shown++; }, hide: () => events['face-setup-modal:hide.bs.modal']({ preventDefault() {} }) };
    const bootstrap = { Modal: { getOrCreateInstance: () => modal } };
    const location = { href: 'http://localhost/FaceSetup', origin: 'http://localhost', pathname: '/FaceSetup', assign: url => redirects.push(url), replace: () => { throw new Error('Unexpected standalone-page redirect'); } };
    const faceapi = { nets: Object.fromEntries(['tinyFaceDetector','faceLandmark68Net','faceRecognitionNet'].map(x => [x, { loadFromUri: async () => {} }])), TinyFaceDetectorOptions: class {}, detectAllFaces: () => ({ withFaceLandmarks: () => ({ withFaceDescriptors: async () => [{ descriptor: Array(128).fill(0.01 + ++sample * 0.001), detection: { box: { width: 250, height: 250 } } }] }) }) };
    const context = { URL, Event, FormData: class { append() {} }, window: { location, bootstrap, faceapi, isSecureContext: true, addEventListener: (name, fn) => { events[name] = fn; } }, location, bootstrap, faceapi,
        document: { hidden: false, getElementById: id => elements[id] || null, addEventListener: (name, fn) => { events[name] = fn; } },
        navigator: { mediaDevices: { getUserMedia: async () => { camera++; if (mode === 'close') modal.hide(); return { getTracks: () => [{ stop: () => { stopped = true; } }] }; } } },
        setTimeout: (fn, ms) => { if (ms === 850) timer = fn; else fn(); }, fetch: async (url, options) => {
            writes.push({ url, options });
            if (url.includes('Decline')) return { ok: true };
            if (mode === 'failure') return { ok: false, status: 400, json: async () => ({ message: 'Password verification failed.' }) };
            return { ok: true, json: async () => ({}) };
        } };
    vm.runInNewContext(enrollment, context); vm.runInNewContext(setupModal, context);
    return { elements, redirects, writes, run: () => events['face-form:submit']({ preventDefault() {} }), skip: () => events['face-setup-skip:click'](), timer: () => timer?.(), flush: async () => { for(let i=0;i<10;i++) await new Promise(setImmediate); }, get camera() {return camera;}, get shown() {return shown;}, get stopped() {return stopped;} };
}
(async () => {
    const success = setup(); check(success.shown === 1 && success.camera === 0, 'Setup modal opens without activating camera');
    success.elements['face-consent'].checked = false; await success.run(); check(success.camera === 0 && success.writes.length === 0, 'Explicit consent required before camera');
    success.elements['face-consent'].checked = true; await success.run();
    const saved = JSON.parse(success.writes[0].options.body);
    check(saved.descriptors.length === 5 && saved.consent && !!saved.password, 'Existing enrollment sends five samples with consent and password confirmation');
    check(success.stopped && success.elements['face-password'].value === '', 'Camera stopped and password cleared after saving');
    check(success.elements['face-message'].dataset.state === 'success' && success.elements['face-message'].textContent === 'Face ID is ready!', 'Green success presentation selected');
    success.timer(); check(success.redirects[0] === 'http://localhost/Account/GoToDashboard', 'Successful enrollment automatically continues through role routing');
    const declined = setup(); declined.skip(); await declined.flush();
    check(declined.camera === 0 && declined.writes[0].url === '/FaceSetup/Decline', 'Password-only choice needs no camera or face samples');
    check(declined.writes[0].options.method === 'POST' && declined.writes[0].options.headers.Accept === 'application/json', 'Decline uses protected state-changing POST');
    check(declined.redirects[0] === '/Account/GoToDashboard', 'Decline continues to existing dashboard dispatcher');
    const failed = setup('failure'); await failed.run(); check(failed.stopped && failed.elements['face-message'].dataset.state === 'failed' && !failed.elements['face-start'].disabled, 'Enrollment failure stops camera and permits retry');
    const closing = setup('close'); await closing.run(); await closing.flush();
    check(closing.stopped && !closing.writes.some(x => x.url.includes('/Save')), 'Closing modal cancels delayed camera capture without saving');
    const missingPassword = setup(); missingPassword.elements['face-password'].value = ''; await missingPassword.run();
    check(missingPassword.camera === 0 && missingPassword.writes.length === 0, 'Password confirmation remains required');
    console.log(`Face setup client checks: ${count} passed.`);
})().catch(error => { console.error(error.message); process.exitCode = 1; });
