# Phase 2: Face enrollment only

## Scope and access

Branch: `feature/face-id-database`. Open `/FaceEnrollment/Index` after normal password login.
There is deliberately no automatic enrollment redirect, login link, dashboard edit, face login,
face-verification endpoint, authentication cookie issuance, or passkey functionality.
All seven existing primary roles can manage their own enrollment while authenticated and active.
Supplier registration and approval routing are unchanged.

## Implementation

- New controller: `Controllers/FaceEnrollmentController.cs` (Index, Save, Delete).
- New request/page models: `Models/FaceEnrollment/FaceEnrollmentModels.cs`.
- New services: `Services/FaceEnrollment/FaceEnrollmentService.cs`, `FaceTemplateProtector.cs`, `FaceEnrollmentExtensions.cs`.
- New UI: `Views/FaceEnrollment/Index.cshtml`, `wwwroot/js/face-enrollment.js`, `wwwroot/css/face-enrollment.css`.
- New checks: `Checks/FaceEnrollmentChecks.cs`, `FaceEnrollmentHttpChecks.cs`, `face-enrollment-client.cjs`.
- Only existing source file changed in this phase: `Program.cs` (namespace, service registration, rate-limiter middleware after authentication).
- Database entity/configuration/migration/snapshot changes from the previous approved phase remain intact; no new schema changes in this phase.

The owner ID comes exclusively from the authenticated principal. Every mutation requires
the current password; `CheckPasswordSignInAsync` checks Identity sign-in eligibility and lockout,
counts wrong passwords, and never creates an authentication cookie. Consent is required for
enrollment and replacement. Passwords are not retained in server state, returned, or logged.
Client password fields are cleared after operations and when the page is hidden.

Save/Delete are authenticated POSTs with anti-forgery validation, 48,000-byte request limits,
and a shared six-attempt/minute fixed-window policy per authenticated user (process-local).
GET views are no-store. Do not enable request-body logging for these endpoints in reverse proxies/APM.

Three to five samples are accepted; the UI captures five. Each is 128 finite floats, bounded
to absolute value <= 3. Consistency distance must not exceed 0.5 and identical captures are rejected.
These checks validate shape/consistency, not camera authenticity or liveness. Input can be forged.
No photographs, video, landmarks or raw frames are transmitted or stored by this module.

The server protects a serialized array of descriptors using purpose
`SourcingTender.FaceEnrollment / v1 / authenticated UserId` before storage. The encrypted payload
and metadata are saved atomically into the existing UserFaceCredentials row. Replacement requires
explicit intent and the displayed RowVersion. Concurrent writes fail without overwriting changes;
first-enrollment races are also protected by the existing PK. Deletion clears all template/consent
metadata and sets SampleCount to zero; EnrollmentPromptedAt and the row remain.

## Key storage

The face module uses a separate Data Protection provider, leaving Identity's provider/key ring unchanged.
Windows-only configuration persists keys under the running account's LocalApplicationData:
`SourcingTenderManagement/FaceEnrollmentKeys`, encrypted at rest using current-user DPAPI.
The directory is outside the repository and web root. Keep the same service identity/profile across
restarts; provision its profile and restrict directory ACLs. Back up keys with the appropriate
DPAPI recovery context. Losing the keys requires re-enrollment. Non-Windows hosting fails closed
until a suitable protected key store is explicitly approved and configured.
Database backups may retain older encrypted templates; deletion is not backup erasure.

## Reference ZIP and browser dependency

The ZIP was available and inspected without execution. Its pinned `@vladmandic/face-api@1.7.13`
approach informed detection, descriptor capture, multiple samples, consistency checks and template
protection. ZIP automatic schema creation, direct sign-in, raw-SQL template store, passkey code,
supplier-specific assumptions and biometric audit schema were not copied.

Local browser assets in `wwwroot/lib/face-api/`:

- `face-api.js`, `LICENSE`, `package.json`.
- `model/tiny_face_detector_model-weights_manifest.json`, `model/tiny_face_detector_model.bin`.
- `model/face_landmark_68_model-weights_manifest.json`, `model/face_landmark_68_model.bin`.
- `model/face_recognition_model-weights_manifest.json`, `model/face_recognition_model.bin`.

Downloaded from the exact npm-version URLs on jsDelivr; runtime uses local assets only.
No NuGet or npm installation or project package changes. The package declares MIT and its LICENSE
is included. Upstream references: https://github.com/vladmandic/face-api and
https://cdn.jsdelivr.net/npm/@vladmandic/face-api@1.7.13/package.json .
Dataset provenance, biometric consent wording, deployment threat model and privacy retention policy
still require team review; MIT package licensing is not a privacy/security certification.

## Verification actually executed

- Full build: 0 errors, 0 warnings; outputs in Windows Temp.
- 47 service/validation/crypto/Identity/SQL checks. Includes DPAPI key persistence across provider
  recreation (not a full application restart), actual SQL RowVersion conflicts, active/locked accounts,
  password failure count and membership in all seven roles. Metadata checks are identified as such.
- 19 real loopback MVC/HTTP checks with actual Identity password login, cookies, anti-forgery,
  Razor rendering, enrollment persistence, deletion persistence, local model downloads and rate limiting.
- 10 Node DOM/camera simulations: consent, HTTPS, permission denial, delayed-camera cancellation,
  capture, owner-ID omission, password clearing and stream shutdown.
- JavaScript syntax check and scoped UI mechanical detector passed; detector returned no findings.
- `git diff --check` passed.

The external temporary test runner references the project and calls the two public check classes.
It does not invoke normal Program.cs, role seeding, or the original connection configuration.
SQL target: `localhost / SourcingTender_FaceMigration_20261113_180829_008465` only.
Service fixtures run inside a rolled-back transaction. HTTP fixtures are unique synthetic accounts
and their own face row, committed for cross-request visibility and explicitly removed afterward.
Existing fixtures were not reused. Descriptors are entirely synthetic; generated test passwords
exist only in memory and are not printed. Some key-persistence fixtures remain in Windows Temp,
containing DPAPI-protected test keys, never face descriptors/passwords.

Two initial test-harness defects (tracking a detached Identity instance and assuming a relative
login redirect) were corrected before the successful final run; no unresolved application test failure.

## Limits / next gate

No real-camera/browser interaction, desktop/mobile visual review, or full seven-role end-to-end
workflow regression was performed. HTTP Razor rendering and Node simulations are not browser tests.
The production application was not launched; tests used a dedicated no-seeding MVC host.
No original database connection or test writes, migration operations, changes to existing users,
role-specific files, existing login/register, shared layouts or business workflows in this phase.
No Git staging, commit, push or branch switch.

Before production deployment: verify real camera behavior on supported devices, service-account
key storage/backup, privacy/legal consent and retention, third-party model provenance, and hosting
configuration (HTTPS, payload-log exclusions). This feature only enrolls data; it establishes no
biometric authentication assurance. Stop here pending approval for any later phase.
