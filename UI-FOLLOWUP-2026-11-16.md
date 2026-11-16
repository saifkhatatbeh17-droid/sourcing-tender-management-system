# UI follow-up — 16 November 2026

Project: MVC Project-main · Branch: ghaith · Local URL: https://localhost:7130

## Delivered
- Supplier registration: three real stages (Company, Contact & sign-in, Documents), current-step validation, Back/Continue, final summary and explicit confirmation. Files remain selected while moving between stages. Server validation remains authoritative.
- Sign-in, registration submit and confirmation actions: readable shared orange styling and proper focus/disabled/validation behavior.
- Admin supplier review: Company → Documents → Decision; clear progression and action labels; existing review reason/audit/concurrency fields preserved. Read-only supplier information ends after Documents.
- Department Create/Edit: two real stages (Request details, Requested items); values and item management preserved; confirmation appears only after valid final step.
- Officer request inbox: ten rows per page, Next/Previous, whole-result server search/status filtering, Clear reset and existing voice-search code reused with English/Arabic selection.
- Officer requests and supplier invitations: compact row-level QR buttons next to actions. QR opens a keyboard-accessible dialog; the old inside-detail placements were removed.
- Removed request 'At a glance' card and the persistent open-round explanatory banner. Round, deadline, supplier minimum and exception rules remain enforced on the server.
- Supplier offer entry: three stages (Items/prices, Terms/documents, Review/submit); numeric fields, tax percentage0–100, extra costs, inclusive total and document validation. Server calculates and stores the tax amount; forged posted TaxAmount is ignored.
- Display JD for ISO JOD on the affected supplier/officer offer screens. Stored ISO currency remains JOD.
- Supplier/officer offer details open in popups rather than inline dropdown content. Ownership, unread tracking, compliance confirmation and download permissions remain intact.
- Empty tender-documents content removed. Real document records, where present, and mandatory upload guards remain supported.
- Transient TempData notices of all types dismiss after about3seconds, with hover/focus pause. Inline validation and ongoing workflow warnings remain visible.
- Replacement-needed indicators are derived for every applicable owned forwarded-rejection tender without an existing replacement, including the tender chooser; prepared replacements have a distinct state. Healthy tenders do not display a misleading rejection message.

## Verification
- Final build:0 warnings/errors.
-137 scoring/report/access HTTP regressions passed in an isolated database.
-97 entry/Admin/contact/strict-round/percentage-tax HTTP regressions passed in an isolated database; test database/uploads removed.
- Supplier browser: three steps, hidden-invalid recovery, Back retention, draft/GET/submit round trip, percentage limits, upload limits, JD display, popup/QR/focus. Example verified:37 +4%tax1.48 +3.52extras =42.00JD. No browser errors/mobile overflow/review axe violations.
- Registration/Admin:36 targeted browser checks passed. Valid final registration POST intercepted rather than creating an account. Three server-shaped field-error cases revealed the correct stages. No pending applications existed, so pending-row-specific inspection skipped; existing supplier review/decision partial and read-only modal checked without saving decisions.
- Department: stage gating, retained values, add/remove items, confirmation cancellation, timed notice dismissal, desktop/mobile and axe passed. No request submitted.
- Officer:10-row pagination across29 current requests, search/status/clamping, QR/Escape/focus, modal details outside handoff form, unread only when shown, compliance cancellation, replacement marker and round text removal all passed.
- Reused voice code was tested with a simulated browser speech result to verify server query submission and Clear reset. Actual microphone permission/browser speech-service availability depends on the user's browser/device.
- Public Login/Register light/dark axe checks and officer Requests axe checks: no violations; no page errors or mobile page overflow.

Raw local evidence: output/qa/*followup*, department-steps.json and followup-public-voice.json. No new push or merge into develop. The project is left running locally; no new shutdown is scheduled.

Additional final checks: the reused voice component requires a language control; English/Arabic selection was added and simulated recognition successfully submitted a server-side request search, with Clear resetting it. The shared orange/ink authentication actions passed light/dark axe checks. Supplier cancellation now uses the consistent accessible confirmation dialog instead of a native browser confirm; cancellation test posted nothing. Independent source review found no must-fix issues in the root wizard, pagination, QR, notification or confirmation integration.
