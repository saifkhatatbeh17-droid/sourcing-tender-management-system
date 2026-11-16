(() => {
    const chat = document.querySelector("[data-ai-chat]");
    if (!chat) return;

    const dialog = chat.querySelector("dialog");
    const launcher = chat.querySelector(".ai-chat-launcher");
    const form = chat.querySelector("form");
    const questionInput = form.elements.question;
    const send = form.querySelector("button[type='submit']");
    const sendLabel = send.querySelector("span");
    const messages = chat.querySelector("[role='log']");
    const status = chat.querySelector("[role='status']");
    const start = chat.querySelector("[data-chat-start]");
    const earlierQuestions = [];
    let pending = false;
    let failedQuestion = "";

    function sizeForKeyboard() {
        const viewport = window.visualViewport;
        if (!viewport || !dialog.open) return;
        const keyboard = Math.max(0, window.innerHeight - viewport.height - viewport.offsetTop);
        dialog.style.setProperty("--ai-chat-keyboard", `${keyboard}px`);
        dialog.style.setProperty("--ai-chat-viewport", `${viewport.height}px`);
    }

    launcher.addEventListener("click", () => {
        dialog.showModal();
        launcher.setAttribute("aria-expanded", "true");
        sizeForKeyboard();
        messages.scrollTop = messages.scrollHeight;
        if (window.matchMedia("(pointer: fine)").matches) questionInput.focus();
    });
    chat.querySelector(".ai-chat-close").addEventListener("click", () => dialog.close());
    dialog.addEventListener("close", () => {
        launcher.setAttribute("aria-expanded", "false");
        launcher.focus();
    });
    window.visualViewport?.addEventListener("resize", sizeForKeyboard);
    window.visualViewport?.addEventListener("scroll", sizeForKeyboard);

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

    function addMessage(sender, text, source, sourceLabel) {
        const message = document.createElement("div");
        message.className = "ai-chat-message";
        message.dataset.sender = sender;
        const name = document.createElement("strong");
        name.textContent = sender === "you" ? "You" : "Tiryaq Assistant";
        const body = document.createElement("p");
        body.dir = "auto";
        body.textContent = text;
        message.append(name, body);
        if (source?.startsWith("/") && !source.startsWith("//")) {
            const link = document.createElement("a");
            link.href = source;
            link.textContent = sourceLabel;
            message.append(link);
        }
        messages.append(message);
        messages.tabIndex = 0;
        messages.scrollTop = messages.scrollHeight;
    }

    form.addEventListener("submit", async event => {
        event.preventDefault();
        const question = questionInput.value.trim();
        if (pending || !question) return;

        questionInput.focus({ preventScroll: true });
        if (question !== failedQuestion) addMessage("you", question);
        start.hidden = true;
        pending = true;
        questionInput.readOnly = true;
        questionInput.value = "";
        updateComposer();
        status.dataset.state = "pending";
        status.textContent = "Waiting for a reply…";

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
                    earlierQuestions,
                    question
                })
            });
            if (response.redirected || response.status === 401 || response.status === 403) {
                throw new Error("Chat access changed. Reload the page to check your session.");
            }
            if (response.status === 429) throw new Error("Too many messages. Wait a minute and try again.");
            const result = await response.json().catch(() => null);
            if (!response.ok || !result?.answer) {
                throw new Error(result?.error ?? "The assistant could not reply. Try again.");
            }
            addMessage("assistant", result.answer, result.source, result.sourceLabel);
            earlierQuestions.push(question);
            if (earlierQuestions.length > 3) earlierQuestions.shift();
            status.textContent = "";
            failedQuestion = "";
        } catch (error) {
            status.dataset.state = "error";
            status.textContent = error instanceof TypeError ? "Connection lost. Try again." : error.message;
            questionInput.value = question;
            failedQuestion = question;
        } finally {
            pending = false;
            questionInput.readOnly = false;
            updateComposer();
        }
    });
})();
