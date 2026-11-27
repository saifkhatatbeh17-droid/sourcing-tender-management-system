// Login page: the show/hide password button.
const passwordToggle = document.querySelector(".login-password-toggle");
const loginPassword = document.getElementById("Password");
const loginIdentity = document.querySelector(".login-identity");
const logoStateLabel = document.getElementById("logo-state-label");
const logoEnergyLabel = document.getElementById("logo-energy-label");
const panelReactionBadgeText = document.getElementById("panel-reaction-text");

if (passwordToggle && loginPassword) {
    passwordToggle.hidden = false;
    passwordToggle.addEventListener("click", () => {
        const reveal = loginPassword.type === "password";
        loginPassword.type = reveal ? "text" : "password";
        passwordToggle.textContent = reveal ? "Hide" : "Show";
        passwordToggle.setAttribute("aria-pressed", String(reveal));

        if (loginIdentity) {
            if (reveal) {
                loginIdentity.dataset.passwordShown = "true";
                if (logoStateLabel) logoStateLabel.textContent = "Password Visible (Peeking)";
                if (logoEnergyLabel) logoEnergyLabel.textContent = "Optical visor open while password shown";
                if (panelReactionBadgeText) panelReactionBadgeText.textContent = "Visor Open";
            } else {
                delete loginIdentity.dataset.passwordShown;
                if (logoStateLabel) logoStateLabel.textContent = "Privacy Shield Active";
                if (logoEnergyLabel) logoEnergyLabel.textContent = "Tiryaq core locked for password privacy";
                if (panelReactionBadgeText) panelReactionBadgeText.textContent = "Privacy Shield";
            }
        }
    });
}
