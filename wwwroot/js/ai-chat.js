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
    const status = chat.querySelector("[role='status']");
    const start = chat.querySelector("[data-chat-start]");

    const storageKey = `tiryaq.aiChat.v3.${chat.dataset.user || "anon"}.${chat.dataset.workspace || "default"}`;
    const openFlagKey = `${storageKey}.open`;
    const dismissSpeechKey = `${storageKey}.speechMuted`;
    const storedEntries = [];
    let pending = false;
    let failedQuestion = "";

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
            if (dialog.open || speech?.dataset.hidden === "true" || document.hidden) return;
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
        if (!dialog.open) dialog.showModal();
        chat.dataset.open = "true";
        launcher.setAttribute("aria-expanded", "true");
        try { sessionStorage.setItem(openFlagKey, "1"); } catch { /* ignore */ }
        sizeForKeyboard();
        messages.scrollTop = messages.scrollHeight;
        if (focusInput && window.matchMedia("(pointer: fine)").matches) questionInput.focus();
    }

    launcher.addEventListener("click", () => openChat(true));
    speechOpen?.addEventListener("click", () => openChat(true));
    chat.querySelector(".ai-chat-close").addEventListener("click", () => dialog.close());
    dialog.addEventListener("close", () => {
        delete chat.dataset.open;
        launcher.setAttribute("aria-expanded", "false");
        try { sessionStorage.removeItem(openFlagKey); } catch { /* ignore */ }
        launcher.focus();
    });
    window.visualViewport?.addEventListener("resize", sizeForKeyboard);
    window.visualViewport?.addEventListener("scroll", sizeForKeyboard);

    clearButton?.addEventListener("click", () => {
        if (pending) return;
        storedEntries.length = 0;
        messages.replaceChildren();
        start.hidden = false;
        clearButton.hidden = true;
        status.textContent = "";
        try { sessionStorage.removeItem(storageKey); } catch { /* ignore */ }
        questionInput.focus();
    });

    function setThinkingState(isThinking) {
        if (isThinking) {
            chat.dataset.thinking = "true";
            if (stateBadge) stateBadge.textContent = "Brewing…";
        } else {
            delete chat.dataset.thinking;
            if (stateBadge) stateBadge.textContent = "Ready";
        }
    }

    function updateComposer() {
        const text = questionInput.value.trim();
        send.disabled = pending || text.length === 0;
        sendLabel.textContent = !pending && failedQuestion && text === failedQuestion ? "Retry" : "Send";
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
        messages.scrollTop = messages.scrollHeight;

        if (save) {
            storedEntries.push(entry);
            if (storedEntries.length > 14) storedEntries.splice(0, storedEntries.length - 14);
            persistEntries();
        }
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
        if (sessionStorage.getItem(openFlagKey) === "1") {
            openChat(false);
        }
    } catch {
        /* ignore corrupted storage */
    }

    form.addEventListener("submit", async event => {
        event.preventDefault();
        const question = questionInput.value.trim();
        if (pending || !question) return;

        const historyPayload = storedEntries
            .filter(item => !item.failed && !item.pending)
            .slice(-8)
            .map(item => ({
                role: item.sender === "assistant" ? "assistant" : "user",
                content: item.text
            }));

        questionInput.focus({ preventScroll: true });
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

        pending = true;
        setThinkingState(true);
        questionInput.readOnly = true;
        questionInput.value = "";
        updateComposer();
        status.dataset.state = "pending";
        status.textContent = "Safi is checking your workspace…";

        try {
            const response = await fetch(form.action, {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    "RequestVerificationToken": form.elements.__RequestVerificationToken.value
                },
                body: JSON.stringify({
                    workspace: chat.dataset.workspace,
                    page: chat.dataset.page || null,
                    recordId: Number(chat.dataset.recordId) || null,
                    history: historyPayload,
                    question
                })
            });
            if (response.redirected || response.status === 401 || response.status === 403) {
                throw new Error("Session access changed. Reload the page to continue.");
            }
            if (response.status === 429) throw new Error("Give Safi a quick breath — try again in a few seconds.");
            const result = await response.json().catch(() => null);
            if (!response.ok || !result?.answer) {
                throw new Error(result?.error ?? "Safi could not complete that right now. Try again.");
            }

            delete currentUserEntry.pending;
            delete currentUserEntry.failed;

            const safeNavigateUrl = typeof result.navigateUrl === "string" &&
                result.navigateUrl.startsWith("/") && !result.navigateUrl.startsWith("//")
                ? result.navigateUrl
                : null;

            renderEntry({
                sender: "assistant",
                text: result.answer,
                mutated: Boolean(result.mutated),
                navigated: Boolean(safeNavigateUrl),
                source: result.source || null,
                sourceLabel: result.sourceLabel || null,
                downloads: Array.isArray(result.downloads) ? result.downloads : []
            }, true);
            status.textContent = safeNavigateUrl ? "Safi is transferring your page…" : "";
            failedQuestion = "";

            if (safeNavigateUrl) {
                const currentPathAndQuery = window.location.pathname + window.location.search;
                if (currentPathAndQuery.toLowerCase() !== safeNavigateUrl.toLowerCase()) {
                    try { sessionStorage.setItem(openFlagKey, "1"); } catch { /* ignore */ }
                    window.setTimeout(() => window.location.assign(safeNavigateUrl), 450);
                } else {
                    status.textContent = "";
                }
            }
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
            setThinkingState(false);
            questionInput.readOnly = false;
            updateComposer();
        }
    });
})();
