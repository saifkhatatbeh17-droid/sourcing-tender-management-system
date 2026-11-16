(() => {
    'use strict';
    const form = document.getElementById('face-form');
    if (!form) return;
    const video = document.getElementById('face-video');
    const feedback = document.getElementById('face-feedback');
    const message = document.getElementById('face-message');
    const password = document.getElementById('face-password');
    const progress = document.getElementById('face-progress');
    const start = document.getElementById('face-start');
    const cancel = document.getElementById('face-cancel');
    const remove = document.getElementById('face-delete');
    let stream = null, busy = false, cancelled = false, submitting = false;
    let completed = false;
    const stopCamera = () => {
        if (stream) stream.getTracks().forEach(track => track.stop());
        stream = null;
        video.srcObject = null;
    };
    const setBusy = value => {
        busy = value;
        start.disabled = value;
        if (remove) remove.disabled = value;
        cancel.disabled = !value || submitting;
    };
    const pause = () => new Promise(resolve => setTimeout(resolve, 500));
    const distance = (a, b) => Math.sqrt(a.reduce((sum, x, i) => sum + (x - b[i]) ** 2, 0));
    async function send(action, descriptors) {
        submitting = true;
        cancel.disabled = true;
        const response = await fetch(action, {
            method: 'POST', credentials: 'same-origin', redirect: 'error',
            headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': form.querySelector('input[name="__RequestVerificationToken"]').value },
            body: JSON.stringify({ password: password.value, consent: document.getElementById('face-consent').checked,
                replace: form.dataset.enrolled === 'true', rowVersion: form.dataset.rowVersion || null, descriptors })
        });
        const result = await response.json().catch(() => ({}));
        if (!response.ok) throw new Error(response.status === 429 ? 'Too many attempts. Wait a minute and try again.' : result.message || 'Request failed. Reload the page and try again.');
    }
    form.addEventListener('submit', async event => {
        event.preventDefault();
        if (busy || !form.reportValidity()) return;
        if (!document.getElementById('face-consent').checked) { message.textContent = 'Please read and accept the biometric consent before capturing.'; return; }
        if (form.dataset.enrolled === 'true' && !await window.TiryaqConfirm.ask({ title: 'Replace your face enrollment?', description: 'A successful new capture will replace your saved facial descriptors for sign-in.', confirm: 'Capture & replace' })) return;
        setBusy(true); cancelled = false; message.textContent = ''; message.dataset.state = ''; progress.value = 0;
        const samples = [];
        try {
            if (!window.isSecureContext || !navigator.mediaDevices?.getUserMedia) throw new Error('Camera access needs HTTPS or localhost and a supported browser.');
            if (!window.faceapi) throw new Error('Face detection could not load. Reload the page.');
            feedback.textContent = 'Loading face detection models…';
            await Promise.all([faceapi.nets.tinyFaceDetector.loadFromUri(form.dataset.modelUrl), faceapi.nets.faceLandmark68Net.loadFromUri(form.dataset.modelUrl), faceapi.nets.faceRecognitionNet.loadFromUri(form.dataset.modelUrl)]);
            if (cancelled) return;
            stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: 'user', width: { ideal: 640 }, height: { ideal: 480 } }, audio: false });
            if (cancelled) return;
            video.srcObject = stream;
            await video.play();
            const deadline = Date.now() + 60000;
            while (samples.length < 5 && !cancelled) {
                if (Date.now() > deadline) throw new Error('Capture timed out. Improve the lighting and try again.');
                const faces = await faceapi.detectAllFaces(video, new faceapi.TinyFaceDetectorOptions({ inputSize: 416, scoreThreshold: 0.6 })).withFaceLandmarks().withFaceDescriptors();
                if (cancelled) return;
                if (faces.length !== 1) { feedback.textContent = faces.length ? 'Keep only one face in view.' : 'No face detected. Face the camera in good light.'; await pause(); continue; }
                const face = faces[0], box = face.detection.box;
                if (box.width < video.videoWidth * 0.22 || box.height < video.videoHeight * 0.22) { feedback.textContent = 'Move a little closer to the camera.'; await pause(); continue; }
                const descriptor = Array.from(face.descriptor);
                if (descriptor.length !== 128 || descriptor.some(x => !Number.isFinite(x) || Math.abs(x) > 3)) throw new Error('Invalid camera sample. Try again.');
                if (samples.some(sample => distance(sample, descriptor) > 0.5)) throw new Error('Face samples changed too much. Keep the same face in view and try again.');
                if (samples.length && distance(samples[samples.length - 1], descriptor) < 0.00001) { feedback.textContent = 'Turn your head slightly for the next sample.'; await pause(); continue; }
                samples.push(descriptor); progress.value = samples.length;
                feedback.textContent = `${samples.length} of 5 samples captured. Turn your head slightly.`;
                await pause();
            }
            if (cancelled) return;
            stopCamera(); feedback.textContent = 'Camera is off. Verifying password and saving encrypted samples…';
            await send(form.dataset.saveUrl, samples);
            if (form.dataset.successRedirect) {
                completed = true;
                message.textContent = 'Face ID is ready!'; message.dataset.state = 'success';
                const destination = new URL(form.dataset.successRedirect, window.location.href);
                if (destination.origin === window.location.origin) setTimeout(() => {
                    if (!cancelled) window.location.assign(destination.href);
                }, 850);
            } else location.replace(`${location.pathname}?saved=1`);
        } catch (error) {
            message.textContent = error.name === 'NotAllowedError' ? 'Camera permission was denied. Allow it in your browser settings to enroll.' : error.name === 'NotFoundError' ? 'No camera is available on this device.' : error.message || 'Enrollment failed. Try again.';
            if (form.dataset.successRedirect) { message.dataset.state = 'failed'; message.textContent = "Couldn't complete setup. Try again. " + message.textContent; }
        } finally { samples.length = 0; stopCamera(); password.value = ''; submitting = false; setBusy(false); if (completed) start.disabled = true; feedback.textContent = 'Camera is off.'; }
    });
    cancel.addEventListener('click', () => { if (submitting) return; cancelled = true; stopCamera(); password.value = ''; feedback.textContent = 'Camera is off. Capture cancelled.'; });
    form.addEventListener('faceenrollment:cancel', () => { cancelled = true; stopCamera(); password.value = ''; });
    remove?.addEventListener('click', async () => {
        if (busy || !password.reportValidity() || !await window.TiryaqConfirm.ask({ title: 'Remove your face enrollment?', description: 'All stored facial descriptors for your account will be deleted. You can enroll again later.', confirm: 'Remove enrollment', destructive: true })) return;
        setBusy(true); message.textContent = '';
        try { await send(form.dataset.deleteUrl, null); location.replace(`${location.pathname}?removed=1`); }
        catch (error) { message.textContent = error.message; }
        finally { password.value = ''; submitting = false; setBusy(false); }
    });
    window.addEventListener('pagehide', () => { cancelled = true; stopCamera(); password.value = ''; });
    document.addEventListener('visibilitychange', () => { if (document.hidden) { cancelled = true; stopCamera(); password.value = ''; } });
    form.dataset.enrollmentReady = 'true'; start.disabled = false;
})();
