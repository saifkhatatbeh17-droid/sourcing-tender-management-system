// Sourcing Officer offer popup: runs the AI document review on request.
// Findings are advisory and every one shown quotes evidence that was found in the offer.
(() => {
    const labels = { issue: "Problem", warning: "Check", info: "Note" };

    const el = (tag, className, text) => {
        const node = document.createElement(tag);
        if (className) node.className = className;
        if (text != null) node.textContent = text;
        return node;
    };

    for (const box of document.querySelectorAll("[data-offer-ai-review]")) {
        const button = box.querySelector("[data-ai-review-run]");
        const output = box.querySelector("[data-ai-review-result]");
        const token = box.querySelector("input[name='__RequestVerificationToken']")?.value || "";
        const idleLabel = button.textContent;

        button.addEventListener("click", async () => {
            button.disabled = true;
            button.setAttribute("aria-busy", "true");
            button.textContent = "Reviewing documents…";
            output.replaceChildren(el("p", "ow-precheck-status", "Reading the offer and its documents. This can take up to a minute…"));
            try {
                const response = await fetch(box.dataset.url, {
                    method: "POST",
                    headers: { "RequestVerificationToken": token, "X-Requested-With": "XMLHttpRequest" },
                    body: new URLSearchParams({ id: box.dataset.bidId })
                });
                const data = await response.json().catch(() => null);
                if (!response.ok || !data) throw new Error(data?.error || "The AI review could not be completed. Try again.");
                render(data);
                button.textContent = "Run again";
            } catch (err) {
                output.replaceChildren(el("p", "ow-precheck-status is-error", err.message));
                button.textContent = idleLabel;
            } finally {
                button.disabled = false;
                button.removeAttribute("aria-busy");
            }
        });

        function render(data) {
            const parts = [];
            parts.push(el("h4", null, "AI document review"));
            if (data.summary) parts.push(el("p", "ow-precheck-summary", data.summary));
            const findings = Array.isArray(data.findings) ? data.findings : [];
            if (findings.length === 0) {
                parts.push(el("p", "ow-precheck-ok", "The AI found nothing to flag in the offer text and readable documents."));
            } else {
                const list = el("ul", "ow-precheck-list");
                const order = { issue: 0, warning: 1, info: 2 };
                for (const f of [...findings].sort((a, b) => (order[a.severity] ?? 3) - (order[b.severity] ?? 3))) {
                    const li = el("li");
                    li.dataset.severity = f.severity;
                    li.append(el("span", "ow-precheck-badge", labels[f.severity] || "Note"));
                    const body = el("span");
                    body.append(el("span", null, f.message));
                    if (f.evidence) {
                        const quote = el("q", "ow-precheck-evidence", f.evidence);
                        quote.dir = "auto";
                        body.append(el("br"), el("small", null, `${f.source ? f.source + ": " : ""}`), quote);
                    }
                    li.append(body);
                    list.append(li);
                }
                parts.push(list);
            }
            if (data.hiddenUnverifiable > 0)
                parts.push(el("p", "ow-precheck-note", `${data.hiddenUnverifiable} AI ${data.hiddenUnverifiable === 1 ? "finding was" : "findings were"} hidden because the quoted evidence could not be found in the offer.`));
            for (const note of data.documentNotes || []) parts.push(el("p", "ow-precheck-note", note));
            output.replaceChildren(...parts);
        }
    }
})();
