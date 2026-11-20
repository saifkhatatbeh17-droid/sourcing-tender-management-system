(() => {
    const chat = document.querySelector("[data-ai-chat]");
    if (!chat) return;

    const dialog = chat.querySelector("dialog");
    const launcher = chat.querySelector(".ai-chat-launcher");
    const speech = chat.querySelector("[data-safi-speech]");
    const speechLine = chat.querySelector("[data-safi-line]");
    const speechOpen = chat.querySelector("[data-safi-open]");
    const speechDismiss = chat.querySelector("[data-safi-dismiss]");
    const stateBadge = chat.querySelector("[data-safi-state]");
    const clearButton = chat.querySelector("[data-chat-clear]");
    const form = chat.querySelector("form");
    const questionInput = form.elements.question;
    const send = form.querySelector("button[type='submit']");
    const sendLabel = send.querySelector("span");
    const messages = chat.querySelector("[role='log']");
    const latest = chat.querySelector("[data-chat-latest]");
    const status = chat.querySelector("[role='status']");
    const start = chat.querySelector("[data-chat-start]");
    const historyLimit = Number(chat.dataset.historyLimit);
    const voiceToggle = chat.querySelector("[data-chat-voice-toggle]");
    const voicePanel = chat.querySelector("[data-chat-voice-panel]");
    const voiceOrb = chat.querySelector("[data-chat-voice-orb]");
    const voicePrompt = chat.querySelector("[data-chat-voice-prompt]");
    const voiceSource = chat.querySelector("[data-chat-voice-source]");
    const voiceMute = chat.querySelector("[data-chat-voice-mute]");
    const voiceMuteLabel = voiceMute.querySelector("[data-chat-voice-mute-label]");
    const voiceEnd = chat.querySelector("[data-chat-voice-end]");
    const microphone = form.querySelector("[data-voice-search-button]");
    const attachmentInput = form.querySelector("[data-chat-attachment]");
    const attachButton = form.querySelector("[data-chat-attach]");
    attachButton?.addEventListener("click", () => { if (!pending) attachmentInput.click(); });
    const attachmentName = form.querySelector("[data-chat-file-name]");
    const removeAttachment = form.querySelector("[data-chat-remove-file]");
    const importOptions = form.querySelector("[data-chat-import-options]");
    const importRole = form.querySelector("[data-chat-import-role]");
    const excelMode = form.querySelector("[data-chat-excel-mode]");
    const excelModeOptions = form.querySelector("[data-chat-excel-mode-options]");
    const attachmentHelp = form.querySelector("[data-chat-file-help]");
    let importReady = false;
    function resetAttachment() {
        attachmentInput.value = ""; importReady = false;
        attachmentName.textContent = ""; removeAttachment.hidden = true;
        attachmentHelp.hidden = true;
        if (importOptions) importOptions.hidden = true;
        if (excelModeOptions) excelModeOptions.hidden = true;
    }
    attachmentInput.addEventListener("change", () => {
        importReady = false;
        const file = attachmentInput.files[0];
        if (!file) { resetAttachment(); updateComposer(); return; }
        closeVoice(); // File import confirmation must be an explicit composer action, never an automatic voice turn.
        if (file.size > 5 * 1024 * 1024) {
            resetAttachment(); status.textContent = "Attach a file up to 5 MB."; updateComposer(); return;
        }
        const workbook = /\.xlsx$/i.test(file.name);
        attachmentName.textContent = file.name; removeAttachment.hidden = false;
        if (excelModeOptions) excelModeOptions.hidden = !workbook;
        if (importOptions) importOptions.hidden = !workbook || excelMode?.value !== "accounts";
        attachmentHelp.textContent = workbook
            ? excelMode?.value === "accounts" ? "Email and Password are required. Passwords stay local. Preview and confirm before creating accounts (Admin only)."
                : "Analyze this workbook without creating accounts. Sensitive password columns are omitted from AI analysis. Only Admin can switch to account creation."
            : "PDF text or your image and question are sent to the configured AI provider for analysis. Remove private details before attaching. PDFs: up to 30 pages; scans can be attached as images.";
        attachmentHelp.hidden = false;
        updateComposer();
    });
    removeAttachment.addEventListener("click", () => { if (!pending) { resetAttachment(); updateComposer(); } });
    importRole?.addEventListener("change", () => { importReady = false; updateComposer(); });
    excelMode?.addEventListener("change", () => { importReady = false; attachmentInput.dispatchEvent(new Event("change")); });

    const storageKey = `tiryaq.aiChat.v3.${chat.dataset.user || "anon"}.${chat.dataset.workspace || "default"}`;
    const openFlagKey = `${storageKey}.open`;
    const voiceFlagKey = `${storageKey}.voice`;
    const muteFlagKey = `${storageKey}.voiceMuted`;
    const dismissSpeechKey = `${storageKey}.speechMuted`;
    const storedEntries = [];
    let pending = false;
    let failedQuestion = "";
    let typingIndicator;
    let voiceEnabled = false;
    let micMuted = false;
    let orbListening = false;
    let stopSpeech;
    let listenTimer;
    let resumeWhenVisible = false;
    let followConversation = true;

    function scrollToLatest(force = false) {
        if (force) followConversation = true;
        if (followConversation) messages.scrollTop = messages.scrollHeight;
        latest.hidden = followConversation;
    }

    messages.addEventListener("scroll", () => {
        followConversation = messages.scrollHeight - messages.clientHeight - messages.scrollTop < 36;
        latest.hidden = followConversation;
    }, { passive: true });
    latest.addEventListener("click", () => {
        messages.focus({ preventScroll: true });
        scrollToLatest(true);
    });

    function setVoiceState(state, message) {
        if (state === "ready" && micMuted) {
            state = "muted";
            message = "Microphone muted";
        }
        voicePanel.dataset.state = state;
        voiceOrb.disabled = state === "thinking";
        voiceOrb.setAttribute("aria-label", state === "listening"
            ? "Finish speaking"
            : state === "speaking" ? "Stop Safi speaking" : state === "muted" ? "Unmute microphone" : "Start speaking");
        if (voicePrompt.textContent !== message) voicePrompt.textContent = message;
    }

    function setMicMuted(muted) {
        micMuted = muted;
        try {
            if (muted) sessionStorage.setItem(muteFlagKey, "1");
            else sessionStorage.removeItem(muteFlagKey);
        } catch { /* ignore */ }
        voiceMute.setAttribute("aria-pressed", String(muted));
        voiceMuteLabel.textContent = muted ? "Unmute" : "Mute mic";
        if (muted) {
            resumeWhenVisible = false;
            orbListening = false;
            window.clearTimeout(listenTimer);
            listenTimer = undefined;
            questionInput.dispatchEvent(new Event("canceldictation"));
            if (!pending && !stopSpeech) setVoiceState("ready", "");
        } else if (voiceEnabled && !pending && !stopSpeech) {
            setVoiceState("ready", "Tap to speak");
            startListening();
        }
    }

    function closeVoice() {
        voiceEnabled = false;
        setMicMuted(false);
        resumeWhenVisible = false;
        window.clearTimeout(listenTimer);
        listenTimer = undefined;
        orbListening = false;
        voicePanel.hidden = true;
        delete chat.dataset.voiceMode;
        voiceToggle.setAttribute("aria-expanded", "false");
        voiceToggle.setAttribute("aria-label", "Start voice conversation");
        voiceToggle.title = "Start voice conversation";
        try { sessionStorage.removeItem(voiceFlagKey); } catch { /* ignore */ }
        questionInput.dispatchEvent(new Event("canceldictation"));
        stopSpeech?.();
    }

    const supportsVoiceInput = window.SpeechRecognition || window.webkitSpeechRecognition ||
        (window.MediaRecorder && (window.AudioContext || window.webkitAudioContext) && navigator.mediaDevices?.getUserMedia);
    voiceToggle.disabled = !supportsVoiceInput || !window.isSecureContext;

    function openVoice(focusOrb) {
        if (voiceToggle.disabled) return;
        voiceEnabled = true;
        voicePanel.hidden = false;
        chat.dataset.voiceMode = "true";
        voiceToggle.setAttribute("aria-expanded", "true");
        voiceToggle.setAttribute("aria-label", "End voice conversation");
        voiceToggle.title = "End voice conversation";
        try { sessionStorage.setItem(voiceFlagKey, "1"); } catch { /* ignore */ }
        if (dialog.open) dialog.close();
        setVoiceState(pending ? "thinking" : "ready", pending ? "Preparing a reply…" : "Tap to speak");
        if (!pending) {
            if (focusOrb) voiceOrb.focus({ preventScroll: true });
            startListening();
        }
    }

    voiceToggle.addEventListener("click", () => {
        if (voiceEnabled) closeVoice();
        else openVoice(true);
    });
    voiceMute.addEventListener("click", () => setMicMuted(!micMuted));
    voiceEnd.addEventListener("click", () => {
        closeVoice();
        (dialog.open ? voiceToggle : launcher).focus({ preventScroll: true });
    });

    function startListening() {
        if (!voiceEnabled || micMuted || pending || stopSpeech || microphone.disabled) return;
        if (document.hidden) {
            resumeWhenVisible = true;
            return;
        }
        if (questionInput.value.trim()) {
            setVoiceState("ready", "Review your draft, then press Send.");
            return;
        }
        orbListening = true;
        microphone.click();
    }

    function listenAgain() {
        window.clearTimeout(listenTimer);
        if (!voiceEnabled || micMuted) return;
        listenTimer = window.setTimeout(() => {
            listenTimer = undefined;
            startListening();
        }, 350);
    }

    document.addEventListener("visibilitychange", () => {
        if (!voiceEnabled) return;
        if (document.hidden) {
            if (!orbListening && listenTimer === undefined) return;
            resumeWhenVisible = true;
            orbListening = false;
            window.clearTimeout(listenTimer);
            listenTimer = undefined;
            questionInput.dispatchEvent(new Event("canceldictation"));
            setVoiceState("ready", "Microphone paused while this tab is hidden");
        } else if (resumeWhenVisible) {
            resumeWhenVisible = false;
            listenAgain();
        }
    });

    voiceOrb.addEventListener("click", () => {
        if (stopSpeech) {
            stopSpeech();
            return;
        }
        if (micMuted) {
            setMicMuted(false);
            return;
        }
        window.clearTimeout(listenTimer);
        listenTimer = undefined;
        if (orbListening) microphone.click();
        else startListening();
    });

    questionInput.addEventListener("dictationstate", event => {
        if (!voiceEnabled || !orbListening) return;
        const { message, state } = event.detail;
        if (state === "listening" || state === "starting" || state === "stopping") {
            setVoiceState("listening", message);
        } else if (state === "transcribing") {
            setVoiceState("thinking", message);
        } else if (state !== "complete") {
            orbListening = false;
            if (state === "no-speech") {
                listenAgain();
            } else {
                setVoiceState("ready", message || "Tap to speak");
            }
        }
    });

    questionInput.addEventListener("dictationcomplete", event => {
        if (!voiceEnabled || !orbListening || pending) return;
        orbListening = false;
        if (event.detail.hadDraft || event.detail.truncated) {
            setVoiceState("ready", "Review your draft, then press Send.");
            return;
        }
        form.requestSubmit();
    });

    function speechChunks(text) {
        const limit = Number(form.dataset.speechClipLimit) || 200;
        const chunks = [];
        let remaining = text;
        while (remaining.length > limit) {
            const head = remaining.slice(0, limit);
            const sentenceEnd = Math.max(...[".", "!", "?", "؟", "\n"].map(mark => head.lastIndexOf(mark))) + 1;
            const boundary = sentenceEnd > 50 ? sentenceEnd : head.lastIndexOf(" ");
            const length = boundary > 0 ? boundary : /[\uD800-\uDBFF]$/.test(head) ? limit - 1 : limit;
            chunks.push(remaining.slice(0, length).trim());
            remaining = remaining.slice(length).trimStart();
        }
        if (remaining) chunks.push(remaining);
        return chunks;
    }

    async function fetchSpeechClip(text, signal) {
        const response = await fetch(form.dataset.speechUrl, {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
                "RequestVerificationToken": form.elements.__RequestVerificationToken.value
            },
            body: JSON.stringify({ workspace: chat.dataset.workspace, text }),
            signal
        });
        if (!response.ok || !response.headers.get("Content-Type")?.startsWith("audio/"))
            throw new Error("Natural voice unavailable.");

        return { blob: await response.blob(), provider: response.headers.get("X-Safi-Speech-Provider") };
    }

    async function playSpeechClip({ blob, provider }, text, signal, onPlaying, onProgress) {
        signal.throwIfAborted();
        const url = URL.createObjectURL(blob);
        const audio = new Audio(url);
        try {
            await new Promise((resolve, reject) => {
                const timedOut = () => complete(new Error("Audio playback timed out."));
                let timeout = window.setTimeout(timedOut, 30000);
                let started = false;
                function complete(error) {
                    window.clearTimeout(timeout);
                    signal.removeEventListener("abort", cancel);
                    audio.onended = audio.onerror = audio.onplaying = audio.ontimeupdate = null;
                    if (error) reject(error);
                    else resolve();
                }
                function cancel() { complete(signal.reason); }
                signal.addEventListener("abort", cancel, { once: true });
                audio.onended = () => complete();
                audio.onerror = () => complete(new Error("Audio could not be played."));
                audio.onplaying = () => {
                    window.clearTimeout(timeout);
                    const playbackMs = Number.isFinite(audio.duration) ? audio.duration * 1000 : text.length * 120;
                    timeout = window.setTimeout(timedOut, Math.max(30000, playbackMs + 10000));
                    started = true;
                    onPlaying(provider);
                    onProgress(0);
                };
                audio.ontimeupdate = () => {
                    if (started && Number.isFinite(audio.duration) && audio.duration > 0)
                        onProgress(Math.min(1, audio.currentTime / audio.duration));
                };
                audio.play().catch(complete);
            });
        } finally {
            audio.pause();
            URL.revokeObjectURL(url);
        }
    }

    function speakReply(answer, onProgress, onDone) {
        if (!voiceEnabled) return false;
        stopSpeech?.();
        questionInput.dispatchEvent(new Event("canceldictation"));
        const text = answer
            .replace(/!?\[([^\]]+)\]\([^)]+\)/g, "$1")
            .replace(/^[ \t]*[>#*-]+[ \t]*/gm, "")
            .replace(/[*_`]/g, "")
            .trim();
        if (!text) return false;
        const controller = new AbortController();
        let browserSpeech = false;
        let timeout;
        function finish(message, cancel = false) {
            if (stopSpeech !== cancelSpeech) return;
            stopSpeech = undefined;
            window.clearTimeout(timeout);
            if (cancel) {
                controller.abort();
                if (browserSpeech) window.speechSynthesis.cancel();
            }
            if (voiceEnabled) setVoiceState("ready", message);
            onDone?.();
        }
        function cancelSpeech() { finish("Tap to speak", true); }
        stopSpeech = cancelSpeech;
        setVoiceState("speaking", "Preparing voice · tap to stop");

        function useBrowserVoice(remainder, prefix) {
            if (controller.signal.aborted) return;
            if (!window.speechSynthesis || !window.SpeechSynthesisUtterance) {
                finish("Audio unavailable. You can read the reply below.");
                return;
            }
            const speech = new SpeechSynthesisUtterance(remainder);
            speech.lang = /\p{Script=Arabic}/u.test(remainder) ? "ar-JO" : "en-US";
            const language = speech.lang.slice(0, 2);
            const voices = window.speechSynthesis.getVoices().filter(voice =>
                voice.lang.slice(0, 2).toLowerCase() === language);
            const natural = voice => /\b(?:natural|neural|enhanced|premium)\b/i.test(voice.name);
            const selected = voices.find(voice => natural(voice) && voice.lang === speech.lang)
                ?? voices.find(natural)
                ?? voices.find(voice => voice.lang === speech.lang)
                ?? voices[0];
            if (selected) speech.voice = selected;
            const words = Array.from(remainder.matchAll(/\S+/gu));
            const revealThrough = word => {
                if (!word || stopSpeech !== cancelSpeech) return;
                onProgress(prefix + remainder.slice(0, word.index + word[0].length));
            };
            speech.onstart = () => {
                if (stopSpeech !== cancelSpeech) return;
                window.clearTimeout(timeout);
                setVoiceState("speaking", "Speaking with browser voice");
                if (voiceSource) voiceSource.textContent = natural(selected ?? { name: "" })
                    ? "AI-generated · browser neural voice"
                    : "AI-generated · standard browser voice";
                revealThrough(words[0]);
            };
            let boundaryWord = 0;
            speech.onboundary = event => {
                if (event.name !== "word") return;
                while (boundaryWord + 1 < words.length && words[boundaryWord + 1].index <= event.charIndex)
                    boundaryWord++;
                revealThrough(words[boundaryWord]);
            };
            speech.onend = () => finish("Tap to speak");
            speech.onerror = () => finish("Audio unavailable. You can read the reply below.");
            browserSpeech = true;
            timeout = window.setTimeout(() => finish("Audio unavailable. You can read the reply below.", true), 4000);
            try {
                window.speechSynthesis.speak(speech);
            } catch {
                finish("Audio unavailable. You can read the reply below.");
            }
        }

        void (async () => {
            const chunks = speechChunks(text);
            // Give the synthesizer a head start so playback does not catch up at the first clip boundary.
            const bufferSize = Math.min(3, chunks.length);
            const queued = [];
            let preparation = Promise.resolve(true);
            function queueClip(index) {
                if (index >= chunks.length) return;
                const next = preparation.then(available => available && !controller.signal.aborted
                    ? fetchSpeechClip(chunks[index], controller.signal)
                    : null).catch(() => null);
                queued[index] = next;
                preparation = next.then(Boolean);
            }
            for (let index = 0; index < bufferSize; index++) queueClip(index);
            await preparation;
            let prefix = "";
            for (const [index, chunk] of chunks.entries()) {
                try {
                    if (index >= bufferSize && stopSpeech === cancelSpeech)
                        setVoiceState("speaking", "Preparing the next part…");
                    const clip = await queued[index];
                    queued[index] = null;
                    if (!clip) throw new Error("Natural voice unavailable.");
                    controller.signal.throwIfAborted();
                    queueClip(index + bufferSize);
                    const words = Array.from(chunk.matchAll(/\S+/gu));
                    await playSpeechClip(clip, chunk, controller.signal, provider => {
                        setVoiceState("speaking", "Safi is speaking");
                        if (voiceSource) voiceSource.textContent = `AI-generated · ${provider || "natural voice"}`;
                    }, progress => {
                        if (stopSpeech !== cancelSpeech || !words.length) return;
                        const word = words[Math.min(words.length - 1, Math.floor(progress * words.length))];
                        onProgress(prefix + chunk.slice(0, word.index + word[0].length));
                    });
                    if (stopSpeech !== cancelSpeech) return;
                    onProgress(prefix + chunk);
                    prefix += chunk + " ";
                } catch {
                    useBrowserVoice(chunks.slice(index).join(" "), prefix);
                    return;
                }
            }
            finish("Tap to speak");
        })();
        return true;
    }

    let greetings = [];
    try {
        greetings = JSON.parse(chat.dataset.greetings || "[]");
    } catch {
        greetings = [];
    }
    let greetingIndex = 0;

    function setSpeechHidden(hidden) {
        if (!speech) return;
        speech.dataset.hidden = hidden ? "true" : "false";
    }

    // Rotate Safi's speech bubble gently every 9s while closed & not muted
    if (greetings.length > 1 && speechLine) {
        window.setInterval(() => {
            if (dialog.open || voiceEnabled || speech?.dataset.hidden === "true" || document.hidden) return;
            greetingIndex = (greetingIndex + 1) % greetings.length;
            speech.dataset.swapping = "true";
            window.setTimeout(() => {
                speechLine.textContent = greetings[greetingIndex];
                delete speech.dataset.swapping;
            }, 160);
        }, 9000);
    }

    try {
        if (sessionStorage.getItem(dismissSpeechKey) === "1") {
            setSpeechHidden(true);
        }
    } catch { /* ignore */ }

    speechDismiss?.addEventListener("click", event => {
        event.stopPropagation();
        setSpeechHidden(true);
        try { sessionStorage.setItem(dismissSpeechKey, "1"); } catch { /* ignore */ }
    });

    function sizeForKeyboard() {
        const viewport = window.visualViewport;
        if (!viewport || !dialog.open) return;
        const keyboard = Math.max(0, window.innerHeight - viewport.height - viewport.offsetTop);
        dialog.style.setProperty("--ai-chat-keyboard", `${keyboard}px`);
        dialog.style.setProperty("--ai-chat-viewport", `${viewport.height}px`);
    }

    function openChat(focusInput = true) {
        if (!dialog.open) {
            if (voiceEnabled) dialog.show();
            else dialog.showModal();
        }
        chat.dataset.open = "true";
        launcher.setAttribute("aria-expanded", "true");
        try { sessionStorage.setItem(openFlagKey, "1"); } catch { /* ignore */ }
        sizeForKeyboard();
        scrollToLatest(true);
        if (focusInput && window.matchMedia("(pointer: fine)").matches) questionInput.focus();
    }

    launcher.addEventListener("click", () => openChat(true));
    speechOpen?.addEventListener("click", () => openChat(true));
    chat.querySelector(".ai-chat-close").addEventListener("click", () => dialog.close());
    dialog.addEventListener("close", () => {
        if (dialog.open) return;
        delete chat.dataset.open;
        launcher.setAttribute("aria-expanded", "false");
        try { sessionStorage.removeItem(openFlagKey); } catch { /* ignore */ }
        if (!voiceEnabled) launcher.focus();
    });
    window.visualViewport?.addEventListener("resize", sizeForKeyboard);
    window.visualViewport?.addEventListener("scroll", sizeForKeyboard);

    clearButton?.addEventListener("click", () => {
        if (pending) return;
        closeVoice();
        storedEntries.length = 0;
        messages.replaceChildren();
        scrollToLatest(true);
        start.hidden = false;
        clearButton.hidden = true;
        status.textContent = "";
        failedQuestion = "";
        questionInput.value = "";
        resetAttachment();
        updateComposer();
        try { sessionStorage.removeItem(storageKey); } catch { /* ignore */ }
        questionInput.focus();
    });

    function setThinkingState(isThinking) {
        if (isThinking) {
            chat.dataset.thinking = "true";
            if (stateBadge) stateBadge.textContent = "Brewing…";
            typingIndicator = document.createElement("div");
            typingIndicator.className = "ai-chat-message";
            typingIndicator.setAttribute("aria-hidden", "true");

            const label = document.createElement("strong");
            label.textContent = "Safi";
            const dots = document.createElement("div");
            dots.className = "ai-chat-bubble ai-chat-typing";
            for (let i = 0; i < 3; i++) dots.append(document.createElement("span"));

            typingIndicator.append(label, dots);
            messages.append(typingIndicator);
            scrollToLatest();
        } else {
            typingIndicator?.remove();
            typingIndicator = undefined;
            delete chat.dataset.thinking;
            if (stateBadge) stateBadge.textContent = "Ready";
        }
    }

    function updateComposer() {
        const text = questionInput.value.trim();
        send.disabled = pending || (text.length === 0 && !attachmentInput.files.length);
        questionInput.required = attachmentInput.files.length === 0;
        attachmentInput.disabled = pending;
        if (attachButton) attachButton.disabled = pending;
        removeAttachment.disabled = pending;
        if (importRole) importRole.disabled = pending;
        if (excelMode) excelMode.disabled = pending;
        if (clearButton) clearButton.disabled = pending;
        sendLabel.textContent = importReady ? "Confirm account creation" : !pending && failedQuestion && text === failedQuestion ? "Retry" : "Send";
    }

    questionInput.addEventListener("input", updateComposer);
    questionInput.addEventListener("keydown", event => {
        if (event.key !== "Enter" || event.shiftKey || event.ctrlKey || event.metaKey ||
            event.altKey || event.isComposing || event.keyCode === 229) return;
        event.preventDefault();
        if (!event.repeat && !send.disabled) form.requestSubmit();
    });

    start.querySelectorAll("[data-chat-question]").forEach(button => {
        button.addEventListener("click", () => {
            questionInput.value = button.dataset.chatQuestion;
            updateComposer();
            form.requestSubmit();
        });
    });

    function appendInlineFormatted(target, text) {
        const parts = String(text ?? "").split(/(\*\*[^*]+\*\*|\*[^*\s][^*]*\*)/g);
        for (const part of parts) {
            if (part.startsWith("**") && part.endsWith("**") && part.length > 4) {
                const bold = document.createElement("b");
                bold.textContent = part.slice(2, -2);
                target.append(bold);
            } else if (part.startsWith("*") && part.endsWith("*") && part.length > 2) {
                const em = document.createElement("em");
                em.textContent = part.slice(1, -1);
                target.append(em);
            } else if (part.length > 0) {
                target.append(document.createTextNode(part));
            }
        }
    }

    function buildFormattedBubble(text) {
        const bubble = document.createElement("div");
        bubble.className = "ai-chat-bubble";
        bubble.dir = "auto";

        const lines = String(text ?? "").replace(/\r\n/g, "\n").split("\n");
        let listEl = null;
        let paragraphLines = [];

        const flushParagraph = () => {
            if (paragraphLines.length === 0) return;
            const p = document.createElement("p");
            paragraphLines.forEach((line, idx) => {
                if (idx > 0) p.append(document.createElement("br"));
                appendInlineFormatted(p, line);
            });
            bubble.append(p);
            paragraphLines = [];
        };

        const flushList = () => {
            if (!listEl) return;
            bubble.append(listEl);
            listEl = null;
        };

        for (const rawLine of lines) {
            const trimmed = rawLine.trim();
            if (!trimmed) {
                flushParagraph();
                flushList();
                continue;
            }
            const bulletMatch = trimmed.match(/^[-*•]\s+(.*)$/);
            const orderedMatch = trimmed.match(/^\d+[.)]\s+(.*)$/);
            if (bulletMatch || orderedMatch) {
                flushParagraph();
                const desiredTag = orderedMatch ? "OL" : "UL";
                if (!listEl || listEl.tagName !== desiredTag) {
                    flushList();
                    listEl = document.createElement(desiredTag.toLowerCase());
                }
                const li = document.createElement("li");
                appendInlineFormatted(li, (bulletMatch ?? orderedMatch)[1]);
                listEl.append(li);
            } else {
                flushList();
                paragraphLines.push(trimmed);
            }
        }
        flushParagraph();
        flushList();

        if (!bubble.firstChild) {
            const fallback = document.createElement("p");
            fallback.textContent = String(text ?? "");
            bubble.append(fallback);
        }
        return bubble;
    }

    function persistEntries() {
        try {
            sessionStorage.setItem(storageKey, JSON.stringify(storedEntries.slice(-14)));
        } catch {
            /* ignore quota errors */
        }
    }

    function saveEntry(entry) {
        storedEntries.push(entry);
        if (storedEntries.length > 14) storedEntries.splice(0, storedEntries.length - 14);
        persistEntries();
    }

    function renderEntry(entry, save = true) {
        const message = document.createElement("div");
        message.className = "ai-chat-message";
        message.dataset.sender = entry.sender;

        const headerRow = document.createElement("div");
        headerRow.className = "ai-chat-meta";

        const name = document.createElement("strong");
        name.textContent = entry.sender === "you" ? "You" : "Safi";
        headerRow.append(name);

        const safeDownloads = Array.isArray(entry.downloads)
            ? entry.downloads.filter(d => d && typeof d.url === "string" && d.url.startsWith("/AiChat/Download/"))
            : [];

        if (entry.mutated) {
            const badge = document.createElement("span");
            badge.className = "ai-chat-badge";
            badge.textContent = "✓ Workspace updated";
            headerRow.append(badge);
        } else if (safeDownloads.length > 0) {
            const badge = document.createElement("span");
            badge.className = "ai-chat-badge";
            badge.textContent = "✓ File ready";
            headerRow.append(badge);
        } else if (entry.navigated) {
            const badge = document.createElement("span");
            badge.className = "ai-chat-badge";
            badge.textContent = "↗ Page transfer";
            headerRow.append(badge);
        }

        const bubble = entry.sender === "you"
            ? (() => {
                const div = document.createElement("div");
                div.className = "ai-chat-bubble";
                div.dir = "auto";
                const p = document.createElement("p");
                p.textContent = entry.text;
                div.append(p);
                return div;
            })()
            : buildFormattedBubble(entry.text);

        message.append(headerRow, bubble);
        if (Array.isArray(entry.importRows)) {
            const wrap = document.createElement("div");
            wrap.className = "ai-chat-import-preview";
            wrap.tabIndex = 0;
            const table = document.createElement("table");
            const caption = document.createElement("caption");
            caption.textContent = "Account import · " + entry.importRows.length + " rows";
            table.append(caption);
            const head = document.createElement("tr");
            for (const title of ["Row", "Email", "Name", "Role", "Validation"]) {
                const th = document.createElement("th"); th.scope = "col"; th.textContent = title; head.append(th);
            }
            const thead = document.createElement("thead"); thead.append(head); table.append(thead);
            const tbody = document.createElement("tbody");
            for (const row of entry.importRows.slice(0, 100)) {
                const tr = document.createElement("tr");
                for (const text of [row.row, row.email, row.fullName, row.role, row.errors?.length ? row.errors.join(" ") : "✓ Valid"]) {
                    const td = document.createElement("td"); td.textContent = String(text ?? ""); tr.append(td);
                }
                tbody.append(tr);
            }
            table.append(tbody); wrap.append(table); message.append(wrap);
        }

        const hasSafeSource = typeof entry.source === "string" && entry.source.startsWith("/") && !entry.source.startsWith("//");
        if (safeDownloads.length > 0 || hasSafeSource || entry.mutated) {
            const actions = document.createElement("div");
            actions.className = "ai-chat-actions";

            for (const dl of safeDownloads) {
                const dlLink = document.createElement("a");
                dlLink.className = "ai-chat-download-link";
                dlLink.dataset.format = dl.format || "file";
                dlLink.href = dl.url;
                dlLink.setAttribute("download", dl.fileName || "Safi-Export");
                dlLink.textContent = `⬇ ${dl.label || dl.fileName || "Download file"}`;
                actions.append(dlLink);
            }

            if (hasSafeSource) {
                const link = document.createElement("a");
                link.className = "ai-chat-action-link";
                link.href = entry.source;
                link.textContent = `${entry.sourceLabel || "Open record"} →`;
                actions.append(link);
            }

            if (entry.mutated) {
                const refreshBtn = document.createElement("button");
                refreshBtn.type = "button";
                refreshBtn.className = "ai-chat-refresh-btn";
                refreshBtn.textContent = "↻ Refresh page";
                refreshBtn.addEventListener("click", () => window.location.reload());
                actions.append(refreshBtn);
            }

            message.append(actions);
        }

        messages.append(message);
        start.hidden = true;
        if (clearButton) clearButton.hidden = false;
        messages.tabIndex = 0;
        scrollToLatest();

        if (save) saveEntry(entry);
    }

    // Restore session conversation for this user + workspace if present
    try {
        const rawSaved = sessionStorage.getItem(storageKey);
        if (rawSaved) {
            const parsed = JSON.parse(rawSaved);
            if (Array.isArray(parsed)) {
                for (const item of parsed.slice(-14)) {
                    if (item && (item.sender === "you" || item.sender === "assistant") && typeof item.text === "string") {
                        storedEntries.push(item);
                        renderEntry(item, false);
                    }
                }
            }
        }
        if (sessionStorage.getItem(voiceFlagKey) === "1") {
            if (sessionStorage.getItem(muteFlagKey) === "1") setMicMuted(true);
            openVoice(false);
        }
        if (sessionStorage.getItem(openFlagKey) === "1") openChat(false);
    } catch {
        /* ignore corrupted storage */
    }

    form.addEventListener("submit", async event => {
        event.preventDefault();
        const attachedFile = attachmentInput.files[0];
        const question = questionInput.value.trim() || (attachedFile ? (/\.xlsx$/i.test(attachedFile.name)
            ? (importReady ? "تأكيد إنشاء الحسابات من الملف" : excelMode?.value === "accounts" ? "راجع ملف الحسابات قبل إنشائها" : "حلّل ملف الإكسل واشرح أهم النتائج والملاحظات.") : /\.pdf$/i.test(attachedFile.name) ? "لخّص هذا الملف واشرح أهم المعلومات فيه." : "اشرح الصورة وساعدني أفهم المشكلة الظاهرة.") : "");
        if (pending || !question) return;
        const useVoice = voiceEnabled && !attachedFile;
        window.clearTimeout(listenTimer);
        listenTimer = undefined;
        stopSpeech?.();

        const historyPayload = storedEntries
            .filter(item => !item.failed && !item.pending)
            .slice(-historyLimit)
            .map(item => ({
                role: item.sender === "assistant" ? "assistant" : "user",
                content: item.text
            }));

        if (!useVoice) questionInput.focus({ preventScroll: true });
        let currentUserEntry = null;
        if (question === failedQuestion) {
            currentUserEntry = [...storedEntries].reverse().find(item => item.sender === "you" && item.text === question);
            if (currentUserEntry) {
                currentUserEntry.pending = true;
                delete currentUserEntry.failed;
            }
        }
        if (!currentUserEntry) {
            currentUserEntry = { sender: "you", text: question, pending: true };
            renderEntry(currentUserEntry, true);
        }
        scrollToLatest(true);

        pending = true;
        setThinkingState(true);
        questionInput.readOnly = true;
        questionInput.value = "";
        updateComposer();
        status.dataset.state = "pending";
        status.textContent = "Safi is preparing a reply…";
        if (useVoice) setVoiceState("thinking", "Preparing a reply…");
        let waitingForVoice = false;

        try {
            const upload = attachedFile ? new FormData() : null;
            if (upload) {
                upload.append("workspace", chat.dataset.workspace);
                upload.append("question", question);
                upload.append("file", attachedFile);
                upload.append("defaultRole", importRole?.value || "SourcingOfficer");
                upload.append("confirmImport", String(importReady));
                upload.append("intent", importReady ? "accounts" : excelMode?.value || "analyze");
            }
            const response = await fetch(attachedFile ? "/AiChat/Attachment" : form.action, {
                method: "POST",
                headers: {
                    ...(!attachedFile ? { "Content-Type": "application/json" } : {}),
                    "RequestVerificationToken": form.elements.__RequestVerificationToken.value
                },
                body: upload || JSON.stringify({
                    workspace: chat.dataset.workspace,
                    page: chat.dataset.page || null,
                    recordId: Number(chat.dataset.recordId) || null,
                    history: historyPayload,
                    question
                })
            });
            if (response.redirected || response.status === 401) {
                throw new Error("Session access changed. Reload the page to continue.");
            }
            if (response.status === 429) throw new Error("Give Safi a quick breath — try again in a few seconds.");
            const result = await response.json().catch(() => null);
            if (!response.ok || !result?.answer) {
                throw new Error(result?.error ?? "Safi could not complete that right now. Try again.");
            }
            if (attachedFile) {
                if (result.previewReady) {
                    importReady = true;
                    if (importOptions) importOptions.hidden = false;
                    attachmentHelp.textContent = "Preview is ready. Click Confirm account creation to save all accounts, or × to cancel.";
                } else resetAttachment();
            }

            if (result.workspaceOnly) {
                renderEntry({
                    sender: "assistant",
                    text: result.answer,
                    source: result.source,
                    sourceLabel: result.sourceLabel
                }, false);
                throw new Error("");
            }

            delete currentUserEntry.pending;
            delete currentUserEntry.failed;

            const safeNavigateUrl = typeof result.navigateUrl === "string" &&
                result.navigateUrl.startsWith("/") && !result.navigateUrl.startsWith("//")
                ? result.navigateUrl
                : null;

            const replyEntry = {
                sender: "assistant",
                text: result.answer,
                mutated: Boolean(result.mutated),
                navigated: Boolean(safeNavigateUrl),
                source: result.source || null,
                sourceLabel: result.sourceLabel || null,
                downloads: Array.isArray(result.downloads) ? result.downloads : [],
                importRows: Array.isArray(result.importRows) ? result.importRows : null
            };
            saveEntry(replyEntry);
            delete status.dataset.state;
            status.textContent = safeNavigateUrl ? "Safi is transferring your page…" : "";
            failedQuestion = "";

            let preview;
            let previewText;
            function showSpokenWords(text) {
                if (!preview) {
                    waitingForVoice = false;
                    setThinkingState(false);
                    preview = document.createElement("div");
                    preview.className = "ai-chat-message";
                    preview.setAttribute("aria-hidden", "true");
                    const name = document.createElement("strong");
                    name.textContent = "Safi";
                    previewText = document.createElement("div");
                    previewText.className = "ai-chat-bubble";
                    previewText.dir = "auto";
                    preview.append(name, previewText);
                    messages.append(preview);
                }
                if (text.length <= previewText.textContent.length) return;
                previewText.textContent = text;
                scrollToLatest();
            }

            const finishTurn = () => {
                waitingForVoice = false;
                setThinkingState(false);
                preview?.remove();
                renderEntry(replyEntry, false);
                if (!safeNavigateUrl) {
                    listenAgain();
                    return;
                }
                const currentPathAndQuery = window.location.pathname + window.location.search;
                if (currentPathAndQuery.toLowerCase() !== safeNavigateUrl.toLowerCase()) {
                    if (!voiceEnabled) {
                        try { sessionStorage.setItem(openFlagKey, "1"); } catch { /* ignore */ }
                    }
                    window.setTimeout(() => window.location.assign(safeNavigateUrl), 450);
                } else {
                    status.textContent = "";
                    listenAgain();
                }
            };
            waitingForVoice = useVoice;
            if (!useVoice || !speakReply(result.answer, showSpokenWords, finishTurn)) finishTurn();
        } catch (error) {
            delete currentUserEntry.pending;
            currentUserEntry.failed = true;
            persistEntries();
            status.dataset.state = "error";
            status.textContent = error instanceof TypeError ? "Connection lost. Try again." : error.message;
            questionInput.value = question;
            failedQuestion = question;
        } finally {
            pending = false;
            if (!waitingForVoice) setThinkingState(false);
            questionInput.readOnly = false;
            if (voiceEnabled && !stopSpeech && voicePanel.dataset.state === "thinking")
                setVoiceState("ready", failedQuestion ? "Review the message below to retry." : "Tap to speak");
            updateComposer();
        }
    });
})();
