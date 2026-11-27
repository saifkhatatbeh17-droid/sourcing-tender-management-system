// Reports pages: the Admin report-access switches and saving them.
(() => {
    "use strict";
    const form = document.querySelector("[data-report-access]");
    if (!form) return;

    const inputs = Array.from(form.querySelectorAll("input[data-report-key]:not(:disabled)"));
    const save = form.querySelector("[data-save-permissions]");
    const feedback = form.querySelector("[data-permission-feedback]");
    let busy = false;
    const changed = () => inputs.filter(input => input.checked !== (input.dataset.original === "true"));

    form.addEventListener("change", () => {
        const count = changed().length;
        save.disabled = busy || count === 0;
        feedback.textContent = count === 0 ? "No unsaved changes." : `${count} permission change${count === 1 ? "" : "s"} ready to save.`;
        feedback.classList.remove("rpt-error");
    });

    form.addEventListener("submit", async event => {
        event.preventDefault();
        const pending = changed();
        if (busy || pending.length === 0) return;
        busy = true;
        save.disabled = true;
        inputs.forEach(input => { input.disabled = true; });
        feedback.textContent = "Saving permissions…";
        try {
            const response = await fetch(form.action, {
                method: "POST",
                credentials: "same-origin",
                headers: {
                    "Content-Type": "application/json",
                    "Accept": "application/json",
                    "RequestVerificationToken": form.querySelector('input[name="__RequestVerificationToken"]').value
                },
                body: JSON.stringify(pending.map(input => ({
                    reportKey: input.dataset.reportKey,
                    role: input.dataset.reportRole,
                    allowed: input.checked
                })))
            });
            if (response.redirected || !response.headers.get("content-type")?.includes("application/json"))
                throw new Error("Your changes were not confirmed. Reload the page and sign in again if needed.");
            const result = await response.json();
            if (!response.ok || typeof result.message !== "string")
                throw new Error(typeof result === "string" ? result : result.message || "Permissions could not be saved. Refresh and try again.");
            pending.forEach(input => {
                input.dataset.original = String(input.checked);
                input.closest("label").querySelector(".rpt-permission-origin").textContent = "Custom";
            });
            feedback.textContent = result.message;
            feedback.classList.remove("rpt-error");
        } catch (error) {
            feedback.textContent = error.message || "Permissions could not be saved. Please try again.";
            feedback.classList.add("rpt-error");
        } finally {
            busy = false;
            inputs.forEach(input => { input.disabled = false; });
            save.disabled = changed().length === 0;
        }
    });
})();
