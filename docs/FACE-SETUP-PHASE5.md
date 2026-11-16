# Phase 5 — Voluntary first-login enrollment modal

## Behavior

After normal successful password login the existing GoToDashboard dispatcher checks whether the
authenticated active/non-locked user has a primary role, no enrolled templates, and has not declined
setup in this authenticated session. Pending suppliers retain their original RegistrationStatus
redirect before this check. Approved suppliers and all six employee roles are eligible.

Eligible users see a protected welcome surface with an automatically opened Set Up Face ID modal,
not the standalone enrollment page. Already enrolled users keep their existing dashboard routing.
The inline Face ID option still stays on the original Login document: after password success its
protected modal endpoint now chooses enrollment setup for eligible unenrolled users, or the existing
face similarity modal otherwise. No enrollment state is revealed by the public password response.

Setup starts no camera automatically. Register My Face requires explicit consent and the current
password before capture. Existing Phase 2 JS collects five samples and the existing Save endpoint
reauthenticates the password, validates descriptors, encrypts with user-bound Data Protection,
sets trusted metadata and uses the current RowVersion/PK safeguards. No new biometric implementation.
The popup shows sample progress, short feedback and a green animated Face ID is ready state; after
850 ms it uses the existing GoToDashboard routing. Existing standalone enrollment behavior is retained.

Use password only and modal dismissal send an authenticated anti-forgery POST. A dedicated HttpOnly,
SameSite=Lax session cookie suppresses this optional prompt until a new successful password login or
logout clears it. The cookie contains only `1`, no UserId, biometric data, match result or authority.
It is deliberately a UI preference, not an authentication token: forging it only suppresses an optional
prompt that users are allowed to decline anyway. Identity cookie issuance/validation is unchanged.
The preference also survives normal Identity cookie sliding renewal. Browser session restore may
retain session cookies, but every actual new password login clears the preference.

Camera cleanup runs on cancel, dismissal, navigation, success and failures. Late permission streams
are stopped when they arrive. An already accepted enrollment save cannot be uncommitted by closing
the browser; consent/password validation remains required for that request. Decline never stores a
biometric row. No schema changes, migrations or verification-attempt persistence.

Later enrollment is available through one Manage Face ID link in Views/Shared/_Layout.cshtml.
TopCommittee's thin role layout inherits this shared layout, so seven separate dashboards are not edited.

## Files created

- `Services/FaceEnrollment/FaceSetupFlow.cs`
- `Controllers/FaceSetupController.cs`
- `Views/FaceSetup/Index.cshtml`
- `Views/FaceSetup/_SetupModal.cshtml`
- `wwwroot/css/face-setup.css`
- `wwwroot/js/face-setup-modal.js`
- `Checks/face-setup-client.cjs`
- `docs/FACE-SETUP-PHASE5.md`

## Files modified

- `Controllers/AccountController.cs`: clear the UI preference after successful password sign-in/logout,
  check eligibility after existing supplier gating, and use the authenticated setup-or-verify modal URL.
- `Views/Shared/_Layout.cshtml`: one authenticated Manage Face ID link.
- `Views/Account/Login.cshtml`: setup stylesheet and lazy script URLs only.
- `wwwroot/js/face-login-inline.js`: load/reopen setup without leaving the Login document; preserve retries.
- `wwwroot/js/face-enrollment.js`: optional modal success state/navigation, cancellation hook and readiness.
- `Checks/FaceLoginChecks.cs`: new setup/decline/session/role eligibility tests and updated modal URL contract.
- `Checks/face-enrollment-client.cjs`: DOM doubles expose dataset for the real success-state API.
- `Checks/face-login-inline-client.cjs`: current modal URL and inline setup dispatch case.

No Program.cs, Identity model/options, registration, role-specific dashboards/controllers/views,
entity/configuration/snapshot/migration, FaceVerificationService or enrollment storage/security service
changes. Existing uncommitted work remains preserved on feature/face-id-database.

## Executed verification

269 checks passed:

- Enrollment service/crypto/Identity/SQL: 47.
- Enrollment real HTTP/MVC: 19.
- Verification service/SQL: 29.
- Verification real HTTP/MVC: 21.
- Login/setup HTTP and role eligibility: 83 (75 HTTP checks + 8 eligibility checks).
- Enrollment client simulations: 10.
- Verification client simulations: 18.
- Login camera modal simulations: 17.
- Inline Login simulations: 13.
- Setup modal simulations: 12.

New coverage includes primary-role eligibility, approved/pending supplier gating, unenrolled employee
setup routing, enrolled bypass, consent, five-sample Save, password-only POST, CSRF, no repeated prompt,
no redirect loop, new-login preference reset, manual enrollment availability, camera shutdown, success
navigation, failure/retry and same-document inline setup. Prior Face Login/Enrollment/Verification tests
were rerun, with the modal URL expectation updated to the authenticated dispatcher.

The no-seeding test hosts use only `localhost / SourcingTender_FaceMigration_20261113_180829_008465`.
No original database connection or real biometric records are used. Tests create only synthetic users,
face rows, supplier profiles/roles/memberships; their own HTTP fixtures are removed and service fixtures
rolled back. The test database is retained, with its existing three migrations. No database recreation,
migration execution, project startup seeding, Git commit/push/pull/merge/rebase or branch switch.

Full build succeeded with zero warnings/errors. JS syntax, git diff --check and scoped UI mechanical
checks passed. No real-browser/camera/modal keyboard or responsive visual review was executed in Phase 5;
HTTP and DOM simulations are not browser evidence. Manually verify setup on first login, decline and
later return, camera permissions, five samples, password confirmation, success animation, mobile scrolling,
Escape/close cleanup, reduced motion, and later Manage Face ID access before deployment.

Face enrollment and similarity remain optional and do not replace password authentication or grant
permissions. A user without a face can always continue using the existing password workflow.
