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

    const title = section.querySelector("[data-spec-title]");
    const reason = section.querySelector("[data-spec-reason]");
    const evidence = section.querySelector("[data-spec-evidence]");
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
