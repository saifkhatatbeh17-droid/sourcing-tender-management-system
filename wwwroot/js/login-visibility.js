const passwordToggle = document.querySelector(".login-password-toggle");
const loginPassword = document.getElementById("Password");

if (passwordToggle && loginPassword) {
    passwordToggle.hidden = false;
    passwordToggle.addEventListener("click", () => {
        const reveal = loginPassword.type === "password";
        loginPassword.type = reveal ? "text" : "password";
        passwordToggle.textContent = reveal ? "Hide" : "Show";
    });
}
