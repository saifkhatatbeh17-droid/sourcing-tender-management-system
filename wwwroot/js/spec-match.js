document.querySelectorAll("[data-spec-match]").forEach(section => {
    const form = section.querySelector("[data-spec-form]");
    const run = form?.querySelector("button");
    const progress = form?.querySelector("[data-spec-progress]");
    form?.addEventListener("submit", () => {
        run.disabled = true;
        run.textContent = "Matching…";
        progress.textContent = "Matching specifications. This may take a moment.";
    });
    if (form) window.addEventListener("pageshow", event => {
        if (!event.persisted) return;
        run.disabled = false;
        run.textContent = run.dataset.idleLabel;
        progress.textContent = "";
    });
    if (run) run.dataset.idleLabel = run.textContent;

    // The comparison stays folded away until asked for, so the page is not crowded. It opens right after an analysis.
    const body = section.querySelector("[data-spec-body]");
    const toggle = section.querySelector("[data-spec-toggle]");
    function setOpen(open) {
        if (!body || !toggle) return;
        body.hidden = !open;
        toggle.setAttribute("aria-expanded", String(open));
        toggle.textContent = open ? "Hide comparison" : "Show comparison";
    }
    toggle?.addEventListener("click", () => setOpen(body.hidden));
    if (section.id && location.hash === "#" + section.id && body) { setOpen(true); section.scrollIntoView(); }

    const title = section.querySelector("[data-spec-title]");
    const reason = section.querySelector("[data-spec-reason]");
    const evidence = section.querySelector("[data-spec-evidence]");
    const warranty = section.querySelector("[data-spec-warranty]");
    let selected;

    section.querySelectorAll(".spec-match-cell").forEach(cell => {
        cell.addEventListener("click", () => {
            selected?.setAttribute("aria-pressed", "false");
            cell.setAttribute("aria-pressed", "true");
            selected = cell;
            title.textContent = cell.dataset.title;
            reason.textContent = cell.dataset.reason || "";
            reason.hidden = !cell.dataset.reason;
            evidence.textContent = cell.dataset.evidence || "";
            evidence.hidden = !cell.dataset.evidence;
            if (warranty) {
                warranty.textContent = cell.dataset.warranty || "";
                warranty.hidden = !cell.dataset.warranty;
                if (cell.dataset.warrantyVerdict) warranty.dataset.verdict = cell.dataset.warrantyVerdict;
                else delete warranty.dataset.verdict;
            }
        });

        cell.addEventListener("keydown", event => {
            const offsets = { ArrowLeft: [0, -1], ArrowRight: [0, 1], ArrowUp: [-1, 0], ArrowDown: [1, 0] };
            const offset = offsets[event.key];
            if (!offset) return;
            event.preventDefault();
            const row = Number(cell.dataset.row) + offset[0];
            const column = Number(cell.dataset.column) + offset[1];
            section.querySelector(`[data-row="${row}"][data-column="${column}"]`)?.focus();
        });
    });
});
