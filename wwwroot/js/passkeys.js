// Passkey (fingerprint / device) sign-in and registration in the browser.
document.querySelectorAll("[data-passkey-form]").forEach(form => {
    const button = form.querySelector("button");
    const status = form.querySelector("[role='status']");
    const enrollment = form.dataset.passkeyForm === "enroll";
    const parseOptions = enrollment
        ? window.PublicKeyCredential?.parseCreationOptionsFromJSON
        : window.PublicKeyCredential?.parseRequestOptionsFromJSON;

    function showAccess(state) {
        if (!enrollment) document.dispatchEvent(new CustomEvent("login-access", { detail: state }));
    }

    async function post(url, data) {
        const response = await fetch(url, {
            method: "POST",
            body: data,
            headers: { Accept: "application/json" }
        });

        if (!response.ok) {
            const error = await response.json().catch(() => null);
            throw new Error(error?.message || "Could not complete the passkey request. Please try again.");
        }
        return response;
    }

    form.addEventListener("submit", async event => {
        event.preventDefault();
        if (!window.isSecureContext || !navigator.credentials || !parseOptions) {
            status.textContent = "Use a current browser on HTTPS or localhost to use a passkey.";
            return;
        }

        button.disabled = true;
        button.setAttribute("aria-busy", "true");
        showAccess("pending");
        status.textContent = "Follow the verification prompt on your device.";
        try {
            const data = new FormData(form);
            const optionsData = new FormData();
            optionsData.set("__RequestVerificationToken", data.get("__RequestVerificationToken"));
            const response = await post(form.dataset.optionsUrl, optionsData);
            const publicKey = parseOptions.call(PublicKeyCredential, await response.json());
            const credential = enrollment
                ? await navigator.credentials.create({ publicKey })
                : await navigator.credentials.get({ publicKey });
            if (!credential) {
                showAccess("idle");
                status.textContent = "No passkey was selected.";
                return;
            }

            data.set("credential", JSON.stringify(credential));
            const result = await post(form.action, data);
            if (enrollment) {
                form.reset();
                status.textContent = "Passkey added. You can use it next time you sign in.";
            } else {
                const { redirect } = await result.json();
                showAccess("verified");
                await (window.tiryaqBiometricWelcome?.("fingerprint", button) ?? Promise.resolve());
                window.location.assign(redirect);
            }
        } catch (error) {
            showAccess(error.name === "NotAllowedError" ? "idle" : "failed");
            status.textContent = error.name === "NotAllowedError"
                ? "Verification was cancelled, timed out, or no matching passkey was available."
                : error.message;
        } finally {
            button.disabled = false;
            button.removeAttribute("aria-busy");
        }
    });
});
