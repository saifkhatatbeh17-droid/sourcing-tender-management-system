// Admin only: create employee accounts from an Excel sheet inside Safi's panel.
// The sheet is checked on the server; nothing here is sent to the AI, and these messages are never
// saved to the chat history that Safi sends with later questions.
(() => {
    const chat = document.querySelector("[data-ai-chat]");
    const button = chat?.querySelector("[data-account-import]");
    const fileInput = chat?.querySelector("[data-account-import-file]");
    if (!chat || !button || !fileInput) return;

    const form = chat.querySelector("form.ai-chat-form");
    const messages = chat.querySelector("[role='log']");
    const start = chat.querySelector("[data-chat-start]");
    const token = () => form?.querySelector("input[name='__RequestVerificationToken']")?.value || "";
    const MAX_BYTES = 1024 * 1024;
    let busy = false;

    const el = (tag, className, text) => {
        const node = document.createElement(tag);
        if (className) node.className = className;
        if (text != null) node.textContent = text;
        return node;
    };

    function scrollDown() {
        messages.scrollTop = messages.scrollHeight;
    }

    // Builds a message in the same shape as Safi's own, marked so it is never read back as history.
    function message(sender, ...content) {
        if (start) start.hidden = true;
        const wrap = el("div", "ai-chat-message ai-import-message");
        wrap.dataset.sender = sender;
        wrap.dataset.localOnly = "true";
        const meta = el("div", "ai-chat-meta");
        meta.append(el("strong", null, sender === "you" ? "You" : "Safi"));
        const bubble = el("div", "ai-chat-bubble ai-import-bubble");
        bubble.dir = "auto";
        bubble.append(...content);
        wrap.append(meta, bubble);
        messages.append(wrap);
        scrollDown();
        return bubble;
    }

    const paragraph = (text) => el("p", null, text);

    function templateLink() {
        const link = el("a", "ai-import-template", "Download the template");
        link.href = button.dataset.templateUrl;
        link.setAttribute("download", "tiryaq-account-import-template.xlsx");
        return link;
    }

    function problemTable(problems) {
        const table = el("table", "ai-import-table");
        const caption = el("caption", null, `${problems.length} ${problems.length === 1 ? "row needs" : "rows need"} fixing`);
        const head = el("thead");
        const headRow = el("tr");
        for (const label of ["Row", "Email", "Problem"]) headRow.append(Object.assign(el("th", null, label), { scope: "col" }));
        head.append(headRow);
        const body = el("tbody");
        for (const p of problems) {
            const row = el("tr");
            row.append(el("td", null, String(p.rowNumber)), el("td", null, p.email || p.name || "–"));
            const cell = el("td");
            for (const issue of p.problems || []) cell.append(el("div", null, issue));
            row.append(cell);
            body.append(row);
        }
        table.append(caption, head, body);
        const scroller = el("div", "ai-import-table-wrap");
        scroller.append(table);
        return scroller;
    }

    function readyList(ready) {
        const details = el("details", "ai-import-ready");
        details.append(el("summary", null, `Show the ${ready.length} ${ready.length === 1 ? "account" : "accounts"} to create`));
        const list = el("ul");
        for (const r of ready) list.append(el("li", null, `${r.name} · ${r.email} · ${r.role}`));
        details.append(list);
        return details;
    }

    async function post(url, body) {
        const response = await fetch(url, {
            method: "POST",
            headers: { "RequestVerificationToken": token(), "X-Requested-With": "XMLHttpRequest" },
            body
        });
        const data = await response.json().catch(() => null);
        if (!response.ok || !data) throw new Error(data?.error || "Something went wrong. Try again.");
        return data;
    }

    function setBusy(state) {
        busy = state;
        button.disabled = state;
        button.setAttribute("aria-busy", String(state));
    }

    button.addEventListener("click", () => {
        if (!busy) fileInput.click();
    });

    fileInput.addEventListener("change", async () => {
        const file = fileInput.files?.[0];
        fileInput.value = "";
        if (!file) return;

        message("you", paragraph(`Create accounts from ${file.name}`));
        if (!file.name.toLowerCase().endsWith(".xlsx")) {
            message("safi", paragraph("Only Excel .xlsx files can be imported. Save the sheet as .xlsx and try again."), templateLink());
            return;
        }
        if (file.size > MAX_BYTES) {
            message("safi", paragraph("This file is larger than 1 MB. Split it into smaller files and upload them one at a time."));
            return;
        }

        setBusy(true);
        const working = message("safi", paragraph("Checking the file…"));
        working.setAttribute("aria-busy", "true");
        try {
            const body = new FormData();
            body.append("file", file);
            const preview = await post(button.dataset.previewUrl, body);
            working.closest(".ai-chat-message").remove();
            showPreview(preview);
        } catch (err) {
            working.removeAttribute("aria-busy");
            working.replaceChildren(paragraph(err.message), templateLink());
        } finally {
            setBusy(false);
        }
    });

    function showPreview(preview) {
        const ready = preview.readyCount || 0;
        const problems = preview.problems || [];
        const parts = [];

        parts.push(paragraph(ready > 0
            ? `${ready} ${ready === 1 ? "account is" : "accounts are"} ready to create${problems.length ? `, and ${problems.length} ${problems.length === 1 ? "row needs" : "rows need"} fixing first` : ""}.`
            : "No accounts can be created from this file yet. Fix the rows below and upload it again."));
        for (const notice of preview.notices || []) parts.push(el("p", "ai-import-notice", notice));
        if (ready > 0) parts.push(readyList(preview.ready || []));
        if (problems.length > 0) parts.push(problemTable(problems));
        if (ready > 0 && problems.length > 0)
            parts.push(paragraph("Rows that need fixing are skipped. You can fix them later and upload just those rows."));

        const actions = el("div", "ai-import-actions");
        let confirm;
        if (ready > 0 && preview.token) {
            confirm = el("button", "btn btn-primary", `Create ${ready} ${ready === 1 ? "account" : "accounts"}`);
            confirm.type = "button";
            const cancel = el("button", "btn btn-outline-secondary", "Cancel");
            cancel.type = "button";
            confirm.addEventListener("click", () => createAccounts(preview.token, actions));
            cancel.addEventListener("click", () => {
                actions.replaceChildren(el("p", "ai-import-done", "Import cancelled. No accounts were created."));
            });
            actions.append(confirm, cancel);
        }
        actions.append(templateLink());
        parts.push(actions);

        message("safi", ...parts);
        confirm?.focus({ preventScroll: true });
    }

    async function createAccounts(previewToken, actions) {
        actions.querySelectorAll("button").forEach(b => { b.disabled = true; });
        const working = el("p", "ai-import-done", "Creating accounts…");
        actions.append(working);
        setBusy(true);
        try {
            const body = new URLSearchParams({ token: previewToken });
            const result = await post(button.dataset.confirmUrl, body);
            actions.replaceChildren(el("p", "ai-import-done", "Confirmed."));
            showResult(result);
        } catch (err) {
            working.textContent = err.message;
        } finally {
            setBusy(false);
        }
    }

    function showResult(result) {
        const created = result.createdCount || 0;
        const failed = result.failed || [];
        const parts = [paragraph(created > 0
            ? `Created ${created} ${created === 1 ? "account" : "accounts"}.`
            : "No accounts were created.")];
        if (failed.length > 0) {
            parts.push(paragraph("These rows changed since the preview and were not created:"));
            parts.push(problemTable(failed));
        }
        const link = result.credentials;
        if (created > 0 && link && typeof link.url === "string" && link.url.startsWith("/AiChat/Download/")) {
            const download = el("a", "ai-chat-download-link", `⬇ ${link.label}`);
            download.dataset.format = "excel";
            download.href = link.url;
            download.setAttribute("download", link.fileName);
            download.addEventListener("click", () => {
                // One-time file: after the first click the link no longer works.
                setTimeout(() => {
                    download.removeAttribute("href");
                    download.setAttribute("aria-disabled", "true");
                    download.textContent = "Password file downloaded";
                }, 0);
            }, { once: true });
            const actions = el("div", "ai-chat-actions");
            actions.append(download);
            parts.push(actions);
            parts.push(el("p", "ai-import-notice",
                "The password file can be downloaded once, within 15 minutes. Give each person only their own password, then delete the file. Everyone must choose a new password at first sign-in."));
        }
        message("safi", ...parts);
    }
})();
