(() => {
    'use strict';
    const button = document.getElementById('face-login-button');
    const host = document.getElementById('face-login-host');
    const element = document.getElementById('face-signin-modal');
    if (!button || !host || !element || !window.bootstrap?.Modal) return;
    const form = button.form;
    const password = form.querySelector('input[name="Password"]');
    const error = document.getElementById('face-login-error');
    const video = document.getElementById('face-signin-video');
    const feedback = document.getElementById('face-signin-feedback');
    const preview = document.getElementById('face-signin-preview');
    const snapshot = document.getElementById('face-signin-snapshot');
    const modal = bootstrap.Modal.getOrCreateInstance(element);
    let stream = null, busy = false, submitting = false, generation = 0, scriptPromise = null;
    const mesh = document.getElementById('face-signin-mesh');
    const readout = preview.querySelector('[data-fs-readout]');
    const LINES = [[0, 16], [17, 21], [22, 26], [27, 30], [31, 35], [36, 41, true], [42, 47, true], [48, 59, true], [60, 67, true]];
    let target = null, drawn = null, raf = 0;
    function drawMesh() {
        raf = 0;
        const ctx = mesh.getContext('2d');
        if (!ctx) return;
        ctx.clearRect(0, 0, mesh.width, mesh.height);
        if (!target) { drawn = null; return; }
        drawn = drawn ? drawn.map((p, i) => ({ x: p.x + (target[i].x - p.x) * .28, y: p.y + (target[i].y - p.y) * .28 })) : target.map(p => ({ ...p }));
        const unit = mesh.width / 320;
        ctx.lineWidth = 1.2 * unit; ctx.strokeStyle = 'rgba(239,184,92,.55)';
        for (const [a, b, closed] of LINES) {
            ctx.beginPath();
            for (let i = a; i <= b; i++) i === a ? ctx.moveTo(drawn[i].x, drawn[i].y) : ctx.lineTo(drawn[i].x, drawn[i].y);
            if (closed) ctx.closePath();
            ctx.stroke();
        }
        ctx.fillStyle = '#f6d49a';
        drawn.forEach((p, i) => { ctx.beginPath(); ctx.arc(p.x, p.y, (i % 7 === 0 ? 2.4 : 1.4) * unit, 0, Math.PI * 2); ctx.fill(); });
        if (drawn.some((p, i) => Math.abs(p.x - target[i].x) > .5)) raf = requestAnimationFrame(drawMesh);
    }
    function setMesh(points) {
        target = points;
        if (!raf) raf = requestAnimationFrame(drawMesh);
    }
    function readoutText(text) { if (readout) readout.textContent = text; }
    const signal = state => document.dispatchEvent(new CustomEvent('login-access', { detail: state }));
    function status(text, state = 'pending') {
        feedback.textContent = text;
        preview.dataset.state = state;
    }
    function clearSnapshot() {
        snapshot.hidden = true; snapshot.width = 0; snapshot.height = 0;
        video.hidden = false;
        setMesh(null); delete preview.dataset.live; readoutText('Standby');
    }
    function freezePreview() {
        if (!video.videoWidth || !video.videoHeight) return;
        snapshot.width = video.videoWidth; snapshot.height = video.videoHeight;
        const context = snapshot.getContext('2d');
        if (!context) return;
        context.drawImage(video, 0, 0);
        snapshot.hidden = false; video.hidden = true;
    }
    const localUrl = value => {
        const url = new URL(value, location.href);
        if (url.origin !== location.origin) throw new Error('Unable to load Face ID. Reload the page.');
        return url.href;
    };
    function stopCamera() {
        stream?.getTracks().forEach(track => track.stop());
        stream = null; video.srcObject = null;
    }
    function cancel() {
        generation++; stopCamera(); password.value = '';
        busy = false; button.disabled = false;
        clearSnapshot(); status('Starting camera…');
        signal('idle');
    }
    function loadFaceApi() {
        if (window.faceapi) return Promise.resolve();
        if (!scriptPromise) scriptPromise = new Promise((resolve, reject) => {
            const script = document.createElement('script');
            script.src = localUrl(host.dataset.faceApiUrl);
            script.onload = resolve;
            script.onerror = () => { scriptPromise = null; script.remove(); reject(new Error('Camera controls could not load. Close this window and try again.')); };
            document.body.appendChild(script);
        });
        return scriptPromise;
    }
    form.addEventListener('submit', event => {
        if (event.submitter !== button) return;
        event.preventDefault();
        if (busy || !form.reportValidity()) return;
        error.hidden = true; error.textContent = '';
        clearSnapshot(); status('Starting camera…');
        modal.show();
    });
    element.addEventListener('hide.bs.modal', event => {
        if (submitting) { event.preventDefault(); return; }
        cancel();
    });
    window.addEventListener('pagehide', () => { generation++; stopCamera(); clearSnapshot(); password.value = ''; });
    document.addEventListener('visibilitychange', () => { if (document.hidden && !submitting && busy) { cancel(); modal.hide(); } });
    element.addEventListener('shown.bs.modal', async () => {
        if (busy) return;
        if (!form.reportValidity()) { modal.hide(); return; }
        busy = true; button.disabled = true;
        const current = ++generation;
        signal('pending');
        try {
            if (!window.isSecureContext || !navigator.mediaDevices?.getUserMedia) throw new Error('Camera access requires HTTPS or localhost and a supported browser.');
            status('Starting camera…');
            await loadFaceApi();
            await Promise.all([faceapi.nets.tinyFaceDetector.loadFromUri(host.dataset.modelUrl), faceapi.nets.faceLandmark68Net.loadFromUri(host.dataset.modelUrl), faceapi.nets.faceRecognitionNet.loadFromUri(host.dataset.modelUrl)]);
            if (current !== generation) return;
            const camera = await navigator.mediaDevices.getUserMedia({ video: { facingMode: 'user', width: { ideal: 640 }, height: { ideal: 480 } }, audio: false });
            if (current !== generation) { camera.getTracks().forEach(track => track.stop()); return; }
            stream = camera; video.srcObject = stream; await video.play();
            mesh.width = video.videoWidth; mesh.height = video.videoHeight;
            preview.dataset.live = 'searching'; readoutText('Searching');
            const deadline = Date.now() + 45000;
            let descriptor;
            while (current === generation) {
                if (Date.now() > deadline) throw new Error('Capture timed out. Close this window, improve the lighting and try again.');
                const faces = await faceapi.detectAllFaces(video, new faceapi.TinyFaceDetectorOptions({ inputSize: 416, scoreThreshold: 0.6 })).withFaceLandmarks().withFaceDescriptors();
                if (current !== generation) return;
                setMesh(faces.length === 1 ? faces[0].landmarks.positions.map(p => ({ x: p.x, y: p.y })) : null);
                preview.dataset.live = faces.length === 1 ? 'locked' : 'searching';
                readoutText(faces.length === 1 ? 'Face locked' : faces.length > 1 ? 'Multiple faces' : 'Searching');
                if (faces.length === 1 && faces[0].detection.box.width >= video.videoWidth * 0.22 && faces[0].detection.box.height >= video.videoHeight * 0.22) {
                    descriptor = Array.from(faces[0].descriptor);
                    break;
                }
                status(faces.length > 1 ? 'One face only' : 'Look at the camera. Move closer.');
                await new Promise(resolve => setTimeout(resolve, 400));
            }
            if (current !== generation) return;
            if (!descriptor || descriptor.length !== 128 || descriptor.some(x => !Number.isFinite(x) || Math.abs(x) > 3)) throw new Error('Invalid camera sample. Close this window and try again.');
            freezePreview(); stopCamera();
            const payload = new FormData(form);
            descriptor.forEach((value, index) => payload.append(`Descriptor[${index}]`, String(value)));
            payload.append('ModelIdentifier', host.dataset.modelIdentifier);
            submitting = true;
            status('Verifying…'); preview.dataset.live = 'verifying'; readoutText('Matching');
            const response = await fetch(localUrl(button.formAction), { method: 'POST', credentials: 'same-origin', redirect: 'error', headers: { Accept: 'application/json' }, body: payload });
            const result = await response.json().catch(() => ({}));
            if (!response.ok) {
                const failure = new Error(response.status === 429 ? 'Too many attempts. Try later.' : result.code === 'face_not_matched' ? 'Face not matched' : result.code === 'face_unavailable' ? 'Face enrollment unavailable' : 'Sign-in not verified');
                throw failure;
            }
            if (!result.redirectUrl) throw new Error('Sign-in could not finish. Reload the page.');
            status('Face verified', 'matched'); readoutText('Identity confirmed');
            signal('verified');
            await new Promise(resolve => setTimeout(resolve, 450));
            await (window.tiryaqBiometricWelcome?.('face', button) ?? Promise.resolve());
            if (current === generation) location.assign(localUrl(result.redirectUrl));
        } catch (failure) {
            if (current !== generation) return;
            if (snapshot.hidden) freezePreview();
            const message = failure.name === 'NotAllowedError' ? 'Camera access denied' : failure.name === 'NotFoundError' ? 'Camera not found' : failure.message || 'Unable to verify';
            status(message, 'failed'); readoutText('Not verified');
            signal('failed');
        } finally {
            if (current === generation) {
                stopCamera(); password.value = ''; submitting = false; busy = false;
                button.disabled = false;
            }
        }
    });
})();
