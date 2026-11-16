# Face ID modal on the actual Login document

This supersedes Phase 4.1's full-page post-password destination for JavaScript-enabled browsers.

## User flow

1. User remains at `/Account/Login` and enters Email/Password.
2. Only the Face ID submit button is intercepted. Normal Sign in and keyboard-default password
   submission retain their original native behavior.
3. The existing password wrapper accepts an AJAX JSON response request. It still calls the same
   AccountController.Login logic and returns a generic failure or, after password success, a modal URL.
   No redirect or full-page replacement occurs in this request.
4. Browser sends the new Identity cookie to authenticated `/FaceLogin/Modal`. The response is only
   the shared modal partial, with a new anti-forgery token bound to the authenticated principal.
5. The partial is inserted into the existing Login document. Same-origin face scripts are loaded
   only after password success, then Bootstrap opens the camera modal on top of the original form.
6. The existing FaceVerificationService handles comparison. On Matched, the green success state
   appears and after 850 ms the browser navigates to server-generated `/Account/GoToDashboard`.
   Existing role routing and Supplier Pending/Approved behavior choose the actual destination.
7. Mismatch/error does not automatically navigate. The check remains informational and optional;
   password authentication already completed. Closing cancels pending success navigation and stops
   camera/request processing. Continue to workspace remains available.

The standalone FaceVerification page does not have a success redirect attribute and retains its
existing behavior. The old protected `/FaceLogin` full view is retained only for non-AJAX compatibility;
it is not visited by the normal inline browser flow. No face-match tokens or new permissions exist.

## Safeguards and reuse

No public camera preview, enrollment lookup or face comparison before password success. JSON failures
do not reveal enrollment. The browser clears Password before loading the modal assets and after failures.
Protected modal GET remains authenticated/no-store and checks account status. Fresh anti-forgery tokens
are fetched after login, rather than reusing the public Login token for biometric POSTs. Comparison still
uses the existing authenticated, bounded, rate-limited Verify POST and user-scoped protected templates.
No descriptors/images are stored by login/verification, and no biometric result issues an authentication
cookie. Password login alone remains the actual authentication mechanism.

Camera/Bootstrap permissions, reduced-motion success/failure styling and cleanup from Phase 4.1 remain.
Native submission is not used for the face button while JavaScript is operating. Script/fragment/network
failures stay on Login with safe feedback; post-password users may continue through normal routing.
Only same-origin fragment/script/destination URLs are accepted by the client. No third-party assets
are loaded at runtime. The modal partial is shared by the inline flow and retained compatibility view.

## Changed files in this correction

Created:
- `Views/FaceLogin/_FaceIdModal.cshtml`
- `wwwroot/js/face-login-inline.js`
- `Checks/face-login-inline-client.cjs`
- `docs/FACE-LOGIN-INLINE.md`

Modified:
- `Controllers/AccountController.cs`: JSON representation for Face ID password wrapper; existing password method unchanged.
- `Controllers/FaceLoginController.cs`: authenticated partial-view action, sharing the existing read/check logic.
- `Views/Account/Login.cshtml`: inline script/configuration, dedicated button ID and safe error/continuation elements.
- `Views/FaceLogin/Index.cshtml`: reuse the extracted partial, retaining compatibility view.
- `wwwroot/js/face-verification.js`: optional, same-origin success navigation; cancellation invalidates its timer.
- `Checks/FaceLoginChecks.cs`: seven inline HTTP assertions and public script-loading assertion adapted for lazy assets.
- `Checks/face-login-modal-client.cjs`: three automatic navigation/cancellation assertions and URL/timer doubles.
- `docs/FACE-LOGIN-PHASE4-1.md`: superseding-flow notice.

## Executed validation

233 checks passed:
- Enrollment: 47 service/SQL/crypto/Identity, 19 HTTP, 10 client simulations.
- Verification: 29 service/SQL, 21 HTTP, 18 client simulations.
- Face Login: 60 real HTTP/MVC/Identity checks.
- Modal: 17 integrated client simulations.
- Inline Login: 12 client simulations.

HTTP tests verify password success returns JSON without Location redirect, wrong password cannot open
the modal, the partial is protected and has no full document/password echo, fresh post-login CSRF works,
and matching adds no authentication cookie. Client simulations verify original URL remains unchanged
while opening, normal Sign in is untouched, scripts wait for authentication, errors clear passwords,
success goes to role routing, mismatch does not auto-navigate, and closing cancels navigation.
These simulations are not real-browser evidence. Real-camera/modal animation and mobile verification
for this inline correction still require a browser test.

All SQL test writes remain in `localhost / SourcingTender_FaceMigration_20261113_180829_008465`.
Service fixtures roll back and HTTP fixtures delete only their own synthetic rows. No original database
connection, schema/migration operations, role business changes, packages or Git operations were required.
