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
    }

    function setListening(control, listening) {
        control.button.setAttribute('aria-pressed', String(listening));
        control.button.setAttribute('aria-label', listening ? 'Stop voice search' : 'Start voice search');
        control.button.title = listening ? 'Stop voice search' : 'Start voice search';
        control.button.classList.toggle('btn-outline-primary', !listening);
        control.button.classList.toggle('btn-danger', listening);
        control.button.querySelector('[data-voice-icon="mic"]').hidden = listening;
        control.button.querySelector('[data-voice-icon="stop"]').hidden = !listening;
    }

    function finish(control, recognition, message, state = '') {
        if (activeRecognition === recognition) {
            activeRecognition = null;
            activeControl = null;
        }
        setListening(control, false);
        if (message) setStatus(control, message, state);
    }

    function submitSearch(control) {
        control.input.dispatchEvent(new Event('input', { bubbles: true }));
        if (control.input.dataset.voiceSearchMode === 'submit' && control.input.form?.requestSubmit) {
            control.input.form.requestSubmit();
        }
    }

    function errorMessage(error) {
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
            case 'aborted':
                return 'Voice search was stopped.';
            default:
                return `Voice search error${error ? ` (${error})` : ''}. Try again or type your search.`;
        }
    }

    controls.forEach(input => {
        const group = input.closest('.voice-search-control, .ev-tender-search-field');
        const button = group?.querySelector('[data-voice-search-button]');
        const status = group?.querySelector('[data-voice-search-status]');
        const language = group?.querySelector('[data-voice-search-language]');
        if (!group || !button || !status || !language) return;

        const control = { input, group, button, language };
        const pageLanguage = document.documentElement.lang?.toLowerCase() ?? '';
        let savedLanguage = '';
        try { savedLanguage = localStorage.getItem(languageStorageKey) ?? ''; } catch { /* Storage may be disabled. */ }
        language.value = savedLanguage === 'ar-JO' || savedLanguage === 'en-US'
            ? savedLanguage
            : pageLanguage.startsWith('ar') ? 'ar-JO' : 'en-US';
        language.addEventListener('change', () => {
            try { localStorage.setItem(languageStorageKey, language.value); } catch { /* Storage may be disabled. */ }
        });
        setListening(control, false);

        if (!Recognition) {
            button.disabled = true;
            setStatus(control, 'Voice search is not supported by this browser. You can still type your search.', 'unsupported');
            return;
        }
        if (!window.isSecureContext && location.hostname !== 'localhost') {
            button.disabled = true;
            setStatus(control, 'Voice search requires a secure connection (HTTPS). You can still type your search.', 'unsupported');
            return;
        }

        button.addEventListener('click', () => {
            if (activeRecognition && activeControl === control) {
                control.userStopped = true;
                setStatus(control, 'Stopping voice search…', 'stopping');
                try { activeRecognition.stop(); } catch { finish(control, activeRecognition, 'Voice search was stopped.', 'stopped'); }
                return;
            }

            if (activeRecognition && activeControl) {
                const previousRecognition = activeRecognition;
                const previousControl = activeControl;
                previousControl.userStopped = true;
                try { previousRecognition.abort(); } catch { finish(previousControl, previousRecognition, 'Voice search was stopped.', 'stopped'); }
            }

            let recognition;
            try {
                recognition = new Recognition();
                recognition.lang = language.value;
                recognition.continuous = false;
                recognition.interimResults = false;
                recognition.maxAlternatives = 1;
            } catch {
                setStatus(control, 'Voice search could not start in this browser. You can still type your search.', 'error');
                return;
            }

            control.userStopped = false;
            control.resultReceived = false;
            control.errorReceived = false;
            activeRecognition = recognition;
            activeControl = control;

            recognition.onstart = () => {
                setListening(control, true);
                const languageName = language.value === 'ar-JO' ? 'Arabic' : 'English';
                setStatus(control, `Listening in ${languageName}. Speak now, or select Stop voice search.`, 'listening');
            };

            recognition.onresult = event => {
                if (control.resultReceived) return;
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
                input.value = transcript;
                submitSearch(control);
                setStatus(control, 'Search updated from your voice.', 'complete');
            };

            recognition.onerror = event => {
                if (control.userStopped && event.error === 'aborted') return;
                control.errorReceived = true;
                setStatus(control, errorMessage(event.error), event.error === 'not-allowed' || event.error === 'service-not-allowed' ? 'permission-denied' : 'error');
            };

            recognition.onend = () => {
                let message = '';
                let state = '';
                if (control.userStopped && !control.resultReceived) {
                    message = 'Voice search was stopped.';
                    state = 'stopped';
                } else if (!control.resultReceived && !control.errorReceived) {
                    message = 'No speech was detected. Try speaking again.';
                    state = 'no-speech';
                }
                finish(control, recognition, message, state);
            };

            try {
                recognition.start();
            } catch (error) {
                control.errorReceived = true;
                finish(control, recognition, errorMessage(error.name === 'NotAllowedError' ? 'not-allowed' : 'start-failed'), error.name === 'NotAllowedError' ? 'permission-denied' : 'error');
            }
        });
    });
})();
