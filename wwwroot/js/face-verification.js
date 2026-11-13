(() => {
    'use strict';
    const form = document.getElementById('verification-form');
    if (!form) return;
    const video = document.getElementById('verification-video');
    const feedback = document.getElementById('verification-feedback');
    const result = document.getElementById('verification-result');
    const progress = document.getElementById('verification-progress');
    const start = document.getElementById('verification-start');
    const cancel = document.getElementById('verification-cancel');
    let stream = null, busy = false, generation = 0, request = null;
    const setResult = (text, state = '') => { result.textContent = text; result.dataset.state = state; };
    const stopCamera = () => { stream?.getTracks().forEach(track => track.stop()); stream = null; video.srcObject = null; };
    const pause = () => new Promise(resolve => setTimeout(resolve, 400));
    const cancelCapture = () => { generation++; request?.abort(); stopCamera(); feedback.textContent = 'Camera is off. Verification cancelled.'; setResult(''); progress.value = 0; };
    cancel.addEventListener('click', cancelCapture);
    form.addEventListener('faceverification:cancel', cancelCapture);
    window.addEventListener('pagehide', cancelCapture);
    document.addEventListener('visibilitychange', () => { if (document.hidden) cancelCapture(); });
    form.addEventListener('submit', async event => {
        event.preventDefault();
        if (busy) return;
        busy = true; start.disabled = true; cancel.disabled = false; setResult(''); progress.value = 0;
        const current = ++generation;
        let descriptor = null;
        try {
            if (!window.isSecureContext || !navigator.mediaDevices?.getUserMedia) throw new Error('Camera access requires HTTPS or localhost and a supported browser.');
            if (!window.faceapi) throw new Error('Face detection could not load. Reload the page.');
            feedback.textContent = 'Loading face detection models…';
            await Promise.all([faceapi.nets.tinyFaceDetector.loadFromUri(form.dataset.modelUrl), faceapi.nets.faceLandmark68Net.loadFromUri(form.dataset.modelUrl), faceapi.nets.faceRecognitionNet.loadFromUri(form.dataset.modelUrl)]);
            if (current !== generation) return;
            stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: 'user', width: { ideal: 640 }, height: { ideal: 480 } }, audio: false });
            if (current !== generation) return;
            video.srcObject = stream; await video.play(); progress.value = 1;
            const deadline = Date.now() + 45000;
            while (current === generation) {
                if (Date.now() > deadline) throw new Error('Capture timed out. Improve the lighting and try again.');
                const faces = await faceapi.detectAllFaces(video, new faceapi.TinyFaceDetectorOptions({ inputSize: 416, scoreThreshold: 0.6 })).withFaceLandmarks().withFaceDescriptors();
                if (current !== generation) return;
                if (faces.length !== 1) { feedback.textContent = faces.length ? 'Keep only one face in view.' : 'No face detected. Look toward the camera in good light.'; await pause(); continue; }
                const face = faces[0];
                if (face.detection.box.width < video.videoWidth * 0.22 || face.detection.box.height < video.videoHeight * 0.22) { feedback.textContent = 'Move a little closer to the camera.'; await pause(); continue; }
                descriptor = Array.from(face.descriptor);
                if (descriptor.length !== 128 || descriptor.some(x => !Number.isFinite(x) || Math.abs(x) > 3)) throw new Error('Invalid camera sample. Try again.');
                break;
            }
            if (current !== generation) return;
            stopCamera(); progress.value = 2; feedback.textContent = 'Camera is off. Comparing with your enrolled samples…';
            request = new AbortController();
            const response = await fetch(form.dataset.verifyUrl, { method: 'POST', credentials: 'same-origin', redirect: 'error', signal: request.signal,
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': form.querySelector('input[name="__RequestVerificationToken"]').value },
                body: JSON.stringify({ descriptor, modelIdentifier: form.dataset.modelIdentifier }) });
            const body = await response.json().catch(() => ({}));
            if (current !== generation) return;
            if (!response.ok) throw new Error(response.status === 429 ? 'Too many attempts. Wait a minute and try again.' : body.message || 'Check your session and enrollment, then try again.');
            if (!['Matched', 'Not Matched'].includes(body.status)) throw new Error('Unexpected verification response. Try again.');
            setResult(`${body.status}. Informational similarity only; your permissions are unchanged.`, body.status === 'Matched' ? 'matched' : 'not-matched');
            progress.value = 3;
            if (body.status === 'Matched' && form.dataset.successRedirect) {
                const destination = new URL(form.dataset.successRedirect, window.location.href);
                if (destination.origin === window.location.origin) setTimeout(() => {
                    if (current === generation) window.location.assign(destination.href);
                }, 850);
            }
        } catch (error) {
            if (current !== generation || error.name === 'AbortError') return;
            const detail = error.name === 'NotAllowedError' ? 'Camera permission was denied. Allow access in browser settings to try again.' : error.name === 'NotFoundError' ? 'No camera is available.' : error.message || 'Try again later.';
            setResult(`Verification Failed. ${detail}`, 'failed');
        } finally {
            descriptor = null; request = null; stopCamera(); busy = false; start.disabled = false; cancel.disabled = true;
            if (current === generation) feedback.textContent = 'Camera is off.';
        }
    });
    form.dataset.verificationReady = 'true';
    start.disabled = false;
})();
