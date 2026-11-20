(() => {
    const Recognition = window.SpeechRecognition || window.webkitSpeechRecognition;
    const controls = [...document.querySelectorAll('[data-voice-search]')];
    const languageStorageKey = 'pharmacare-voice-search-language';
    let activeRecognition = null;
    let activeControl = null;

    if (!controls.length) return;

    function setStatus(control, message, state = '') {
        const status = control.group.querySelector('[data-voice-search-status]');
        if (!status) return;
        status.textContent = message;
        status.dataset.state = state;
        if (control.dictation)
            control.input.dispatchEvent(new CustomEvent('dictationstate', { detail: { message, state } }));
    }

    function setListening(control, listening) {
        const label = listening ? `Stop ${control.label}` : `Start ${control.label}`;
        control.button.setAttribute('aria-pressed', String(listening));
        control.button.setAttribute('aria-label', label);
        control.button.title = label;
        if (!control.dictation) {
            control.button.classList.toggle('btn-outline-primary', !listening);
            control.button.classList.toggle('btn-danger', listening);
        }
        const micIcon = control.button.querySelector('[data-voice-icon="mic"]');
        const stopIcon = control.button.querySelector('[data-voice-icon="stop"]');
        if (micIcon) micIcon.hidden = listening;
        if (stopIcon) stopIcon.hidden = !listening;
    }

    function finish(control, recognition, message, state = '') {
        if (activeRecognition !== recognition) return;
        activeRecognition = null;
        activeControl = null;
        setListening(control, false);
        if (message) setStatus(control, message, state);
    }

    function cancelActive(control) {
        if (activeControl !== control) return;
        const recognition = activeRecognition;
        finish(control, recognition);
        setStatus(control, '');
        try { recognition.abort(); } catch { /* The browser may have already stopped. */ }
    }

    function submitSearch(control) {
        control.input.dispatchEvent(new Event('input', { bubbles: true }));
        if (control.input.dataset.voiceSearchMode === 'submit' && control.input.form?.requestSubmit) {
            control.input.form.requestSubmit();
        }
    }

    function errorMessage(error, label) {
        switch (error) {
            case 'not-allowed':
            case 'service-not-allowed':
                return 'Microphone permission was denied. Allow microphone access in your browser settings and try again.';
            case 'no-speech':
                return 'No speech was detected. Try speaking again.';
            case 'audio-capture':
                return 'No microphone is available. Connect or enable a microphone and try again.';
            case 'network':
                return 'Speech recognition could not reach the browser speech service. Check your connection and try again.';
            case 'transcription':
                return 'Auto transcription is unavailable. Choose English or العربية to keep talking.';
            case 'aborted':
                return 'Speech recognition was stopped.';
            default:
                return `Could not start ${label}. Try again or continue typing.`;
        }
    }

    function autoRecognition(form) {
        let recorder, stream, context, timer;
        let stopped = false;
        let aborted = false;
        let heardSpeech = false;
        const cancellation = new AbortController();
        const chunks = [];
        const recognition = { start, stop, abort };

        function release() {
            window.clearInterval(timer);
            stream?.getTracks().forEach(track => track.stop());
            if (context && context.state !== 'closed') void context.close().catch(() => {});
        }

        function stop() {
            stopped = true;
            window.clearInterval(timer);
            if (recorder && recorder.state !== 'inactive') recorder.stop();
            else release();
        }

        function abort() {
            aborted = true;
            cancellation.abort();
            stop();
            release();
        }

        async function finish() {
            release();
            if (aborted) return;
            try {
                if (!heardSpeech || !chunks.length) return;
                recognition.onprocessing?.();
                const type = recorder.mimeType || chunks[0].type;
                const extension = type.includes('mp4') ? 'mp4' : type.includes('ogg') ? 'ogg' : 'webm';
                const data = new FormData();
                data.append('workspace', form.dataset.workspace);
                data.append('audio', new Blob(chunks, { type }), `turn.${extension}`);
                const response = await fetch(form.dataset.transcribeUrl, {
                    method: 'POST',
                    headers: { RequestVerificationToken: form.elements.__RequestVerificationToken.value },
                    body: data,
                    signal: cancellation.signal
                });
                if (!response.ok) throw new Error('Transcription failed');
                const { text } = await response.json();
                if (!aborted && typeof text === 'string' && text.trim())
                    recognition.onresult?.({ resultIndex: 0, results: [{ isFinal: true, 0: { transcript: text } }] });
                else if (!aborted) recognition.onerror?.({ error: 'transcription' });
            } catch {
                if (!aborted) recognition.onerror?.({ error: 'transcription' });
            } finally {
                if (!aborted) recognition.onend?.();
            }
        }

        async function start() {
            try {
                context = new (window.AudioContext || window.webkitAudioContext)();
                await context.resume();
                stream = await navigator.mediaDevices.getUserMedia({
                    audio: { channelCount: 1, echoCancellation: true, noiseSuppression: true }
                });
                if (stopped) {
                    release();
                    if (!aborted) recognition.onend?.();
                    return;
                }
                const analyser = context.createAnalyser();
                analyser.fftSize = 1024;
                context.createMediaStreamSource(stream).connect(analyser);
                const samples = new Float32Array(analyser.fftSize);
                const mimeType = ['audio/webm;codecs=opus', 'audio/ogg;codecs=opus', 'audio/mp4']
                    .find(type => MediaRecorder.isTypeSupported(type));
                recorder = new MediaRecorder(stream, mimeType ? { mimeType, audioBitsPerSecond: 32000 } : {});
                recorder.ondataavailable = event => { if (event.data.size) chunks.push(event.data); };
                recorder.onstop = () => void finish();
                recorder.onerror = () => {
                    recognition.onerror?.({ error: 'audio-capture' });
                    abort();
                    recognition.onend?.();
                };
                recorder.start();
                recognition.onstart?.();
                const startedAt = performance.now();
                let lastSpeech = startedAt;
                let voicedFrames = 0;
                timer = window.setInterval(() => {
                    analyser.getFloatTimeDomainData(samples);
                    let energy = 0;
                    for (const signal of samples) energy += signal * signal;
                    const now = performance.now();
                    if (Math.sqrt(energy / samples.length) >= 0.012) {
                        if (++voicedFrames >= 2) heardSpeech = true;
                        lastSpeech = now;
                    } else {
                        voicedFrames = 0;
                    }
                    if ((heardSpeech && now - lastSpeech > 850) ||
                        now - startedAt > (heardSpeech ? 18000 : 7000)) stop();
                }, 50);
            } catch (error) {
                release();
                if (aborted) return;
                recognition.onerror?.({ error: error.name === 'NotAllowedError' ? 'not-allowed' : 'audio-capture' });
                recognition.onend?.();
            }
        }
        return recognition;
    }

    controls.forEach(input => {
        const group = input.closest('[data-voice-dictation], .voice-search-control, .ev-tender-search-field');
        const button = group?.querySelector('[data-voice-search-button]');
        const status = group?.querySelector('[data-voice-search-status]');
        const language = group?.querySelector('[data-voice-search-language]');
        if (!group || !button || !status || !language) return;

        const dictation = group.hasAttribute('data-voice-dictation');
        const autoOption = language.querySelector('option[value="auto"]');
        const supportsAuto = Boolean(dictation && autoOption && group.dataset.transcribeUrl && window.MediaRecorder &&
            (window.AudioContext || window.webkitAudioContext) && navigator.mediaDevices?.getUserMedia);
        if (autoOption) autoOption.disabled = !supportsAuto;
        if (!Recognition && supportsAuto) {
            for (const option of language.options) if (option.value !== 'auto') option.disabled = true;
        }
        const control = { input, group, button, language, dictation, label: dictation ? 'dictation' : 'voice search' };
        const pageLanguage = document.documentElement.lang?.toLowerCase() ?? '';
        let savedLanguage = '';
        const preferenceKey = dictation ? 'tiryaq-safi-voice-language' : languageStorageKey;
        try { savedLanguage = localStorage.getItem(preferenceKey) ?? ''; } catch { /* Storage may be disabled. */ }
        language.value = Recognition && (savedLanguage === 'ar-JO' || savedLanguage === 'en-US')
            ? savedLanguage : supportsAuto ? 'auto' : pageLanguage.startsWith('ar') ? 'ar-JO' : 'en-US';
        language.addEventListener('change', () => {
            try { localStorage.setItem(preferenceKey, language.value); } catch { /* Storage may be disabled. */ }
        });
        setListening(control, false);

        if (!Recognition && !supportsAuto) {
            button.disabled = language.disabled = true;
            setStatus(control, `This browser does not support ${control.label}. You can still type.`, 'unsupported');
            return;
        }
        if (!window.isSecureContext && location.hostname !== 'localhost') {
            button.disabled = language.disabled = true;
            setStatus(control, `Use HTTPS to enable ${control.label}. You can still type.`, 'unsupported');
            return;
        }

        if (dictation) {
            group.closest('dialog')?.addEventListener('close', () => {
                if (!group.closest('[data-ai-chat]')?.dataset.voiceMode) cancelActive(control);
            });
            input.addEventListener('canceldictation', () => cancelActive(control));
            input.form?.addEventListener('submit', () => {
                cancelActive(control);
                setStatus(control, '');
            }, { capture: true });
        }

        button.addEventListener('click', () => {
            if (input.readOnly || input.disabled) return;
            if (activeRecognition && activeControl === control) {
                control.userStopped = true;
                setStatus(control, `Stopping ${control.label}…`, 'stopping');
                try { activeRecognition.stop(); } catch { cancelActive(control); }
                return;
            }

            if (activeControl) cancelActive(activeControl);

            let recognition;
            const auto = supportsAuto && language.value === 'auto';
            try {
                recognition = auto ? autoRecognition(group) : new Recognition();
                if (!auto) recognition.lang = language.value;
                recognition.continuous = false;
                recognition.interimResults = false;
                recognition.maxAlternatives = 1;
            } catch {
                setStatus(control, errorMessage('start-failed', control.label), 'error');
                return;
            }

            control.userStopped = false;
            control.resultReceived = false;
            control.errorReceived = false;
            activeRecognition = recognition;
            activeControl = control;

            recognition.onstart = () => {
                if (activeRecognition !== recognition) return;
                setListening(control, true);
                const languageName = language.value === 'ar-JO' ? 'Arabic' : 'English';
                setStatus(control, auto ? 'Listening · Arabic or English (Groq)'
                    : `Listening in ${languageName}. Tap the microphone again to stop.`, 'listening');
            };
            recognition.onprocessing = () => setStatus(control, 'Detecting language and transcribing…', 'transcribing');

            recognition.onresult = event => {
                if (activeRecognition !== recognition || control.resultReceived) return;
                let transcript = '';
                for (let index = event.resultIndex; index < event.results.length; index++) {
                    const result = event.results[index];
                    if (result.isFinal) transcript = result[0]?.transcript?.trim() ?? '';
                }
                if (!transcript) {
                    control.errorReceived = true;
                    setStatus(control, 'No speech was detected. Try speaking again.', 'no-speech');
                    return;
                }
                control.resultReceived = true;
                if (dictation) {
                    const hadDraft = Boolean(input.value.trim());
                    const separator = input.value && !/\s$/.test(input.value) ? ' ' : '';
                    const available = input.maxLength - input.value.length - separator.length;
                    if (available <= 0) {
                        setStatus(control, 'Your draft is full. Shorten it before adding more text.', 'error');
                        return;
                    }
                    input.value += separator + transcript.slice(0, available).trimEnd();
                    input.dispatchEvent(new Event('input', { bubbles: true }));
                    setStatus(control, transcript.length > available
                        ? 'Dictation was shortened to fit. Review your message before sending.'
                        : 'Dictation added. Review your message before sending.', 'complete');
                    input.dispatchEvent(new CustomEvent('dictationcomplete', {
                        detail: { hadDraft, truncated: transcript.length > available }
                    }));
                    return;
                }
                input.value = transcript;
                submitSearch(control);
                setStatus(control, 'Search updated from your voice.', 'complete');
            };

            recognition.onerror = event => {
                if (activeRecognition !== recognition) return;
                if (control.userStopped && event.error === 'aborted') return;
                if (event.error === 'no-speech') return; // onend clears the old recognizer before retrying.
                control.errorReceived = true;
                const state = event.error === 'not-allowed' || event.error === 'service-not-allowed' ? 'permission-denied' : 'error';
                setStatus(control, errorMessage(event.error, control.label), state);
            };

            recognition.onend = () => {
                let message = '';
                let state = '';
                if (control.userStopped && !control.resultReceived) {
                    message = 'Speech recognition was stopped.';
                    state = 'stopped';
                } else if (!control.resultReceived && !control.errorReceived) {
                    message = 'No speech was detected. Try speaking again.';
                    state = 'no-speech';
                }
                finish(control, recognition, message, state);
            };

            try {
                setListening(control, true);
                setStatus(control, 'Starting microphone…', 'starting');
                recognition.start();
            } catch (error) {
                control.errorReceived = true;
                finish(control, recognition, errorMessage(error.name === 'NotAllowedError' ? 'not-allowed' : 'start-failed', control.label), error.name === 'NotAllowedError' ? 'permission-denied' : 'error');
            }
        });
    });
})();
