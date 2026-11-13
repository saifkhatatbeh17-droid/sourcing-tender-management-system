# Phase 4.1 — Face ID camera modal after password login

> Superseded for the normal browser flow by [FACE-LOGIN-INLINE.md](FACE-LOGIN-INLINE.md): the modal now
> opens on the original Login document through AJAX, and a match automatically continues to role routing.

## Flow

The public `/Account/Login` page and normal password Sign in button are unchanged in this phase.
Email and Password are submitted to the existing Face ID password wrapper. That wrapper still delegates
to the existing Login method, preserving Identity, IsActive, lockout and generic validation errors.
Only its successful redirect changes: it now targets `/FaceLogin`, not `/FaceVerification`.

`FaceLoginController.Index` is authenticated, no-store and checks the current account through the
existing FaceVerificationService. It renders an equivalent Login-styled authenticated view with no
password fields or credential echoes. It never invokes Account.Login or redirects back there for an
already authenticated user, preventing the previous Login GET guard from causing a redirect loop.

Bootstrap 5.3.3 opens a centered, scrollable modal automatically. On its first shown event, the wrapper
requests a capture through the existing verification form handler. Browser permission is mandatory;
an always-present Start camera button supports browsers requiring another user gesture and retries.
The camera does not start on the public Login page. Missing enrollment opens a helpful modal without
loading models or requesting a camera. Closing and reopening never silently starts another capture.

The existing verification JS/model assets and FaceVerificationService perform the comparison.
No native form submission is attempted if the camera script failed to initialize: Start remains disabled
and the modal shows a safe error with continuation still available. The fallback form method is POST,
so an antiforgery token cannot accidentally be submitted in a URL.
No server-side face comparison or authentication logic is duplicated. Results select explicit visual
states: animated green checkmark for Matched, red for Not Matched or Verification Failed. Animation
respects prefers-reduced-motion; result text is an aria-live status, not color-only feedback.
Bootstrap supplies Escape/backdrop dismissal and focus trapping; mobile uses fullscreen-sm-down.

Continue to workspace is available inside and outside the modal and always uses the existing
GoToDashboard routing. A mismatch/cancellation still permits continuation because the password session
already exists and the face check is optional/informational, not a second authentication factor.

## Camera and request cleanup

The shared handler gains a custom cancellation event. Modal hide dispatches that event, immediately
stopping known camera tracks, clearing the result, aborting fetch and invalidating late responses.
If camera permission resolves after closing, the newly returned stream is immediately stopped.
The existing pagehide/visibility cleanup and completion/failure cleanup remain in place.
Browser-owned permission prompts cannot be dismissed programmatically; the user controls them.

## Files created

- `Controllers/FaceLoginController.cs`
- `Views/FaceLogin/Index.cshtml`
- `wwwroot/css/face-login-modal.css`
- `wwwroot/js/face-login-modal.js`
- `Checks/face-login-modal-client.cjs`
- `docs/FACE-LOGIN-PHASE4-1.md`

## Files modified

- `Controllers/AccountController.cs`: only the successful Face ID wrapper destination.
- `wwwroot/js/face-verification.js`: result-state attributes and an explicit cancellation-event hook.
- `Checks/FaceLoginChecks.cs`: retain prior assertions with the new intended redirect and add nine modal HTTP checks.
- `Checks/face-verification-client.cjs`: DOM doubles expose dataset, matching the actual DOM result element.
- `docs/FACE-LOGIN-PHASE4.md`: link to this superseding UI flow; previous milestone evidence retained.

No Program.cs, public Login view, standalone Verification view, services, entities, configurations,
schema, migrations, keys, role-specific files, shared layouts, packages or business workflows changed
in Phase 4.1. Previous uncommitted work remains intact on feature/face-id-database.

## Safeguards

Enrollment state and protected UI remain unavailable to anonymous requests. The existing Verify POST
retains authentication, anti-forgery, input limits, per-user rate limiting and server-derived ownership.
The modal creates no authentication cookie/token, stores no descriptors/images/attempts, sets no claims,
and grants no permissions. Password authentication remains solely the existing Identity pipeline.
Submitted passwords are never passed to or re-rendered on the modal page. No face-match tokens exist.
Normal Login remains independent of the optional Face Login limiter.

## Verification actually executed

211 checks passed:

- 47 enrollment service/crypto/Identity/SQL checks.
- 19 enrollment real HTTP/MVC/Identity checks.
- 29 verification service/SQL checks.
- 21 verification real HTTP/MVC checks.
- 53 Face Login HTTP checks (44 previous checks adapted to the new intended destination + 9 modal checks).
- 10 enrollment client simulations.
- 18 existing verification client simulations.
- 14 integrated modal/camera client simulations, including missing-script failure.

New HTTP coverage checks protected modal access, actual successful Identity login, a 200 landing without
redirect loops, no-store, no additional cookie, responsive Bootstrap markup, explicit Start fallback,
no password echo, assets, missing-enrollment modal and continued standalone verification availability.
Client simulations check automatic modal opening, result states, no early camera, completion cleanup,
closing during permission/capture/request, late-result rejection, no camera for missing enrollment,
reopening behavior and network-error recovery. They use a Bootstrap modal double, not a real browser.

Tests only write synthetic fixtures into:
`localhost / SourcingTender_FaceMigration_20261113_180829_008465`.
Service fixtures are rolled back; HTTP tests delete only their own generated users, face rows, supplier
profile and newly generated roles. Existing fixtures are not reused. No original database connection,
real biometric inputs, production startup or role seeding is involved. Temporary no-seeding test hosts
are stopped after their runs. No passwords or descriptors are printed or persisted in test reports.

Full build, JavaScript syntax, git diff --check and scoped UI mechanical checks were executed.

## Manual browser checks remaining

No actual Phase 4.1 browser, real-camera, animation screenshot or desktop/mobile visual review was run.
HTTP rendering and DOM/Bootstrap doubles do not prove real focus trapping, animation or browser permission
behavior. Before sign-off, verify:

1. Password-only Sign in still routes normally.
2. Face option opens the Login-styled modal after correct password; wrong password stays on Login.
3. Permission prompt/autostart and explicit Start camera fallback on supported browsers.
4. Match animation, nonmatch/failure feedback and retry.
5. Escape, close icon, backdrop, Cancel, navigation and permission-pending cleanup; camera indicator turns off.
6. Keyboard focus, reduced motion and small-screen scroll/footer usability.
7. Continue routes to the appropriate workspace or Pending Supplier registration status.

The existing role-only direct Supplier Dashboard behavior documented in Phase 4 remains unchanged.
No passwordless login, migrations, Git staging/commit/push/pull/merge/rebase or branch switch.
