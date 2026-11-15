# Required face match for the Face ID sign-in option

The `Account/LoginWithFaceId` option now requires a valid password and a matching pre-enrolled face before issuing the application authentication cookie. This replaces the optional post-password behavior described in the earlier FACE-LOGIN phase documents.

The login page uses the existing email to identify the account. Selecting Face ID opens the camera dialog and automatically starts capture after it becomes visible, without a second Start button. Capture runs locally, then one anti-forgery-protected POST submits the credentials, model identifier and 128-value descriptor. A password-only POST, missing enrollment, malformed sample, non-match, inactive account or locked account cannot authenticate. There is no pre-authenticated dashboard continuation. Cancel/error stops the camera and clears the password; a delayed camera-permission response is stopped after cancellation as well.

Password failures and face mismatches count toward Identity lockout. A correct password alone does not reset face failures. A successful match proceeds through the existing Identity password sign-in checks and role dispatcher. The ordinary Sign in and passkey options remain independent. Users must register their face from an authenticated account before choosing this option.

No schema migration or new package is required. Existing enrollment protection and similarity threshold are reused. New descriptors are not persisted during login.

## Security boundary

This change enforces the match result on the server before cookie issuance. The existing browser-based descriptor capture still has no liveness/anti-spoofing or hardware attestation, and the server cannot prove a supplied descriptor came from a live camera. It is not equivalent to a hardware-backed passkey or a production-grade independent biometric authentication factor. Regular password login remains available by design.

## Verification

- Build: passed, zero warnings/errors.
- `FaceLoginChecks.RunAsync`: 28 real HTTP/session checks against the isolated database `SourcingTender_FaceMigration_20261113_180829_008465`; synthetic users/templates removed in finally.
- Covered absent/invalid descriptor, missing CSRF, wrong password, wrong face, absent enrollment, protected-page denial and absence of authentication cookies on failure, successful cookie issuance, safe return URL, seven role dispatchers, accumulated face failures/lockout, unchanged enrollment and normal password login.
- `node Checks/face-login-inline-client.cjs`: passed normal login, invalid fields, no premature authentication request, combined request, matching redirect, mismatch, permission denial, invalid descriptor and cancellation including delayed camera permission.
- Browser: inspected the actual login modal and cancellation at a narrow viewport; password cleared and no navigation after cancel.
- Live camera recognition with the user's face was not performed; synthetic descriptors exercise the HTTP gate, not biometric accuracy or spoof resistance.
