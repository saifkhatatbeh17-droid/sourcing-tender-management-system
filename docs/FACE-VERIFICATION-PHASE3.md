# Phase 3 — Informational face verification

## Scope

Authenticated users open `/FaceVerification/Index`. Users without enrollment receive a link to
`/FaceEnrollment/Index`. Enrolled users can capture one fresh 128-dimensional descriptor and submit
it for server-side comparison. No login, navigation, role-specific page, enrollment implementation,
database entity, configuration, snapshot, migration, or business workflow was changed in this phase.

New files:

- `Controllers/FaceVerificationController.cs`
- `Models/FaceVerification/FaceVerificationModels.cs`
- `Services/FaceVerification/FaceVerificationService.cs`
- `Services/FaceVerification/FaceVerificationExtensions.cs`
- `Views/FaceVerification/Index.cshtml`
- `wwwroot/js/face-verification.js`
- `Checks/FaceVerificationChecks.cs`
- `Checks/FaceVerificationHttpChecks.cs`
- `Checks/face-verification-client.cjs`
- `docs/FACE-VERIFICATION-PHASE3.md`

Only modified existing file in this phase: `Program.cs` adds the namespace and
`AddFaceVerification(builder.Configuration)` call. Existing rate-limiter middleware is reused.
Previous uncommitted database and enrollment work remains intact.

## Reuse and workflow

Uses the same local face-api library/models, existing enrollment CSS, user-bound FaceTemplateProtector,
UserFaceCredentials table, and stored-descriptor consistency validator. Enrollment files are unchanged.
Camera acquisition is opt-in and requires HTTPS/localhost and browser permission. Exactly one face is
required, with basic bounding-box quality feedback. The page stops tracks after capture, failure,
cancellation, visibility loss or page exit. Cancellation aborts an in-flight request and ignores late results.

The server derives the owner from authenticated claims, rechecks active/non-locked account status,
loads only that owner's credential with AsNoTracking, checks stored model/format compatibility,
decrypts with the existing user-bound protector and validates sample structure/count/consistency.
Invalid/corrupt/missing enrollment receives a generic Verification Failed result.

## Algorithm and configuration

For each stored template t and input descriptor d, distance is:

`sqrt(sum((t[i] - d[i])^2)), i = 0..127`.

All stored samples are compared. Result is Matched when the minimum distance is <= MatchThreshold;
otherwise Not Matched. Default threshold is 0.5, chosen consistently with the reference ZIP approach.
This is a similarity heuristic, not a calibrated error rate or a security assurance.

Override using configuration `FaceVerification:MatchThreshold`, or environment variable
`FaceVerification__MatchThreshold`. Configuration must be finite, > 0 and <= 1; invalid configuration
fails startup validation. No tracked connection/configuration file changes are needed.

Input descriptors require exactly 128 finite values with absolute value <= 3. Requests are limited to
12,000 bytes. Stored payloads above 64,000 bytes are rejected safely; supported format is 1 and model
identifier is the existing `FaceEnrollmentService.ModelIdentifier`. No model choice supplied by the
browser can select another user's data or bypass stored compatibility checks.

## Security boundary

This feature is an informational similarity check for an already authenticated user. Browser-generated
descriptors can be forged or replayed. No trusted camera provenance, server-verified liveness, PAD or
proof of identity is claimed. No session, token, claims, roles, lockout counters, approval rights or
business permissions are created/changed by verification. No success state is persisted for other
operations to consume. It is unsuitable as an authorization gate or a passwordless login mechanism.

All endpoints require authentication; the comparison endpoint is POST-only with anti-forgery validation.
Rate limit: 10 attempts per minute per authenticated user, fixed window and process-local, independent
of the enrollment limiter. A multi-instance deployment needs separately approved distributed controls.
Pages/results are no-store. API returns only status and an optional safe error, never descriptors,
distances, templates or encryption material. The module adds no biometric/request-body logging;
operators must keep such logging disabled in proxies/APM. Existing Data Protection key storage is reused.

## Tests actually executed

Phase 3: 68 checks passed:

- 29 service/SQL checks: missing enrollment, matching/nonmatching fixtures, matching the last stored
  sample, configurable threshold, dimensions/nonfinite input, request/stored model and format mismatch,
  corrupt ciphertext/JSON, foreign-user ciphertext, invalid stored samples/counts, inactive/locked account,
  cross-user isolation and unchanged enrollment/EF change tracker.
- 21 real loopback MVC/Identity/HTTP checks: anonymous access rejection, actual password login and cookies,
  missing-enrollment view, authenticated Razor view, no-store, CSRF, matching/nonmatching POSTs, model/
  dimensions/nonfinite JSON rejection, minimal results, absence of Set-Cookie on matching, unchanged
  enrollment/account/roles, unchanged Admin denial, POST-only routing, JS delivery and rate limiting.
- 18 Node DOM/camera simulations: match/nonmatch/failure messages, 128D payload, no owner ID or client
  match decision, permissions/devices/HTTPS errors, progress, no-face/multi-face frames, invalid shape,
  camera shutdown, cancellation during permission, aborting requests and ignoring late responses.

Phase 2 regression: 76 checks passed (47 service/SQL/crypto/Identity + 19 HTTP + 10 JS simulations).
Combined total: 144 passed. Full build: 0 warnings/errors. JavaScript syntax and git diff --check passed.
Scoped UI mechanical detector: no findings.

SQL target for test writes was ONLY `localhost / SourcingTender_FaceMigration_20261113_180829_008465`.
No original database connection was opened in this phase. Service fixtures were rolled back. HTTP fixtures
were unique synthetic users/credential rows and were explicitly removed after cross-request tests.
The temporary external check runner invokes these public test classes without running Program.cs or
startup seeding. Tests use synthetic descriptors and random in-memory passwords, never real biometrics.

## Remaining manual verification

No real browser/camera or desktop/mobile screenshot review was performed for Phase 3. HTTP Razor checks
and DOM simulations are not browser evidence. Confirm camera permission, one-face feedback, cancellation,
match/nonmatch usability and responsive layout on supported real devices. A mismatch does not diagnose
identity: lighting, camera/model limitations and threshold all affect the result. The entire seven-role
business workflow suite was not executed; those files remain untouched and prior Identity checks pass.

No migration operations, original biometric updates, new packages, Git staging/commit/push/merge or branch
switch. Stop after Phase 3; Face Login and security-sensitive verification usage require explicit approval.
