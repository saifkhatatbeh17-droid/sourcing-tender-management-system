# Phase 4 — Password login with optional face similarity

> The UI destination described below was superseded by Phase 4.1's authenticated Login-styled camera modal.
> See [FACE-LOGIN-PHASE4-1.md](FACE-LOGIN-PHASE4-1.md). The password-first security boundary is unchanged.

## Final flow and security decision

The existing Login page retains its original Sign in button and Email/Password form. A second
submit button, **Login with Face ID + Password**, targets `/Account/LoginWithFaceId`.
It requires both Email and Password before any camera interaction. The public page does not load
the face library, query enrollments, compare faces or render a camera.

The new POST action delegates to the CURRENT existing `AccountController.Login(model)` method.
It does not duplicate password validation or replace any teammate code. Only after the existing
Identity password sign-in returns its successful dashboard redirect does the wrapper redirect to
`/FaceVerification/Index?fromLogin=True`. Failure preserves Login's validation, generic errors and
original view. Any other existing login outcome is returned unchanged.

Password login therefore creates the normal Identity session BEFORE optional face comparison.
The authenticated page explains this explicitly. The user clicks Start verification to request the
camera. Existing FaceVerificationController/service/JS perform the Phase 3 read-only similarity check.
**Continue to workspace** calls existing `/Account/GoToDashboard`, preserving all seven role routes
and supplier registration-status routing. Continuation is available with no enrollment or a mismatch;
face similarity is not a second authentication factor and is not an access gate.

This intentionally uses the safer password-first alternative in the request. It avoids publicly
exposing enrollment existence or an email-address-to-face matching oracle. It does not claim to
eliminate all timing/account-enumeration properties of the existing password login. There is no
anonymous biometric endpoint, reusable face-match token, success claim, face-only cookie or
passwordless authentication. Query parameter `fromLogin` only controls explanatory text.

## Files

Existing files modified in Phase 4:

- `Controllers/AccountController.cs`: add only `LoginWithFaceId`, calling the original Login method.
  Existing Login, GoToDashboard, registration and logout implementations remain unchanged.
- `Views/Account/Login.cshtml`: second submit button and explicit password-first/optional-check explanation.
- `Views/FaceVerification/Index.cshtml`: contextual password-success explanation and clearer continuation label.
- `Program.cs`: namespace and `AddFaceLogin()` registration only, retaining all prior phase changes.

New files:

- `Services/FaceLogin/FaceLoginExtensions.cs`
- `Checks/FaceLoginChecks.cs`
- `docs/FACE-LOGIN-PHASE4.md`

No enrollment/verification services, models, JS, templates, keys, packages, layouts, role-specific files,
database entities/configurations or migrations were changed in this phase.

## Protections

- The new action is POST-only, anti-forgery protected and limited to 16,000 request bytes.
- Its own fixed-window limiter allows 30 requests/minute per source IP, process-local.
  It does not apply to the original password Login endpoint. Shared NAT users may share this
  optional-flow limit. Deployment must use trusted forwarding configuration if behind a proxy;
  do not trust arbitrary browser-supplied IP headers.
- Existing PasswordSignInAsync continues to enforce password checks and Identity lockout; IsActive
  and existing role/cookie/supplier rules are not replaced. The wrapper cannot elevate a result
  that the existing Login method did not consider successful. Existing unsupported login outcomes
  are not given any new bypass.
- Face comparison remains authenticated, anti-forgery protected, rate-limited, user-scoped and read-only.
  It returns no templates or distances and does not issue cookies, grant permissions or persist attempts.
- No password, submitted descriptor or protected template is logged by this feature.
  Operators must exclude credential/request bodies from proxy/APM logging.

## Tests executed

44 new real HTTP/MVC/Identity checks passed. Coverage includes original password login, new action/button,
post-login comparison, match/mismatch, missing/corrupt enrollment, invalid credentials, unknown-account
generic error, required fields, inactive and locked accounts, lockout triggered by five wrong passwords,
CSRF, GET rejection, cross-user isolation, no face-only authentication, unchanged synthetic enrollment,
all seven dashboard/status routes, supplier approval policy, limiter and normal-password independence.

Previous checks rerun successfully:

- Enrollment: 47 service/crypto/SQL + 19 HTTP + 10 JS simulations = 76.
- Verification: 29 service/SQL + 21 HTTP + 18 JS simulations = 68.
- Combined: **188 passed**. Full build: 0 errors and 0 warnings.

Real HTTP tests use a separate loopback MVC host with the existing Account/Face controllers and Identity,
without running production Program.cs or startup seeding. Synthetic descriptors and random in-memory
passwords only. Target: `localhost / SourcingTender_FaceMigration_20261113_180829_008465`.
No connection to the original database is opened in Phase 4.

New test writes are limited to two newly generated users, one own encrypted synthetic credential,
one own supplier profile, own role memberships and any missing roles created for these fixtures.
Account activation/lockout counters, profile Pending/Approved and intentional corrupt-payload fixtures
are changed only for those new test users, then all own records/roles are removed. Service regressions
roll back their fixtures. Previous test records and original face enrollments are not reused or altered.

## Existing Supplier behavior discovered

An initial test incorrectly assumed `/Supplier/Dashboard` itself required approval. Actual team code
uses role-only authorization there and renders Pending supplier status. `GoToDashboard()` redirects a
Pending supplier to RegistrationStatus, and ApprovedSupplier policy rejects them. The test was corrected
to verify the current implementation, not to weaken a new face rule. Both Pending/Approved routing and
the approval policy were exercised. The policy test uses a test-host-only protected probe, not a new
production route. SupplierController and all supplier business actions remain untouched.
If the team wants direct Supplier dashboard access blocked for Pending accounts, that is a separate
Supplier-owned change, outside this phase.

## Manual browser checks still required

No Phase 4 real-camera/browser interaction or desktop/mobile screenshot review was performed.
HTTP rendering and the existing Node simulations are not browser evidence. Manually check both Login
buttons, password-first explanation, optional camera permission/cancellation, match/nonmatch display,
and Continue to workspace on desktop/mobile. Existing Phase 2/3 camera success was reported by the user.
The tests validate routing for seven roles, not every business workflow of those roles.

No migrations, schema changes, original database writes, account password resets, branch switches,
fetch/pull/merge/rebase/staging/commit/push, or unrelated fixes. Stop at Phase 4.
