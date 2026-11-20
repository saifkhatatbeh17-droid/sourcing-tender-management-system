// Admin Users page: reset an employee's password to a temporary one.
// The password is shown once in the dialog and cleared when the dialog closes; it is never stored.
(() => {
    for (const section of document.querySelectorAll("[data-password-reset]")) {
        const form = section.closest("form");
        const modal = section.closest(".modal");
        const start = section.querySelector("[data-reset-start]");
        const confirmBox = section.querySelector("[data-reset-confirm]");
        const go = section.querySelector("[data-reset-go]");
        const cancel = section.querySelector("[data-reset-cancel]");
        const result = section.querySelector("[data-reset-result]");
        const output = section.querySelector("[data-reset-password]");
        const copy = section.querySelector("[data-reset-copy]");
        const error = section.querySelector("[data-reset-error]");

        const showError = (message) => {
            error.textContent = message;
            error.hidden = !message;
        };

        function reset() {
            output.value = "";
            result.hidden = true;
            confirmBox.hidden = true;
            start.hidden = false;
            start.disabled = false;
            go.disabled = false;
            copy.textContent = "Copy";
            showError("");
        }

        start.addEventListener("click", () => {
            showError("");
            start.hidden = true;
            confirmBox.hidden = false;
            go.focus();
        });

        cancel.addEventListener("click", () => {
            reset();
            start.focus();
        });

        go.addEventListener("click", async () => {
            go.disabled = true;
            go.textContent = "Resetting…";
            try {
                const response = await fetch(section.dataset.resetUrl, {
                    method: "POST",
                    headers: {
                        "RequestVerificationToken": form?.querySelector("input[name='__RequestVerificationToken']")?.value || "",
                        "X-Requested-With": "XMLHttpRequest"
                    },
                    body: new URLSearchParams({ id: section.dataset.userId })
                });
                const data = await response.json().catch(() => null);
                if (!response.ok || !data?.password) throw new Error(data?.error || "The password could not be reset. Try again.");
                confirmBox.hidden = true;
                output.value = data.password;
                result.hidden = false;
                output.focus();
                output.select();
            } catch (err) {
                confirmBox.hidden = true;
                start.hidden = false;
                showError(err.message);
            } finally {
                go.disabled = false;
                go.textContent = "Reset password";
            }
        });

        copy.addEventListener("click", async () => {
            try {
                await navigator.clipboard.writeText(output.value);
                copy.textContent = "Copied";
            } catch {
                output.select();
                copy.textContent = "Press Ctrl+C";
            }
        });

        // Clear the password from the page as soon as the dialog closes.
        modal?.addEventListener("hidden.bs.modal", reset);
    }
})();
