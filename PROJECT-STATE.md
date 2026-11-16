# Main local project — latest checkpoint16 November 2026

## Demo data refresh — 16 November 2026
User authorized clearing and replacing business data while keeping accounts. Local SQL database `SourcingTenderManagementCodeFirst` now has 24 fresh purchase requests, 14 tenders, 39 offers with sample PDF attachments, 84 invitations, 90 evaluations, 9 reports, 33 approvals, 2 clarifications, 3 contact messages and 18 notifications. Workflow stages span draft requests through awarded tenders. All 19 existing users, password hashes, roles/assignments, passkeys, face credentials, supplier profiles/registration documents and departments were compared exactly inside the refresh transaction and preserved. No new accounts or password resets.
Verified COPY_ONLY/CHECKSUM backup: `C:\Program Files\Microsoft SQL Server\MSSQL17.MSSQLSERVER\MSSQL\Backup\Tiryaq-before-refresh-20261116.bak`. DBCC CHECKCONSTRAINTS passed; all 39 offer tax amounts match 4% of their item subtotals. All 13 retained demo accounts authenticated and 66 authenticated role pages/detail views returned HTTP 200. Evidence/scripts: `output/qa/refresh-demo-20261116*` and `output/qa/verify-refresh-20261116.py`. AI analyses, legacy criterion score records, extension rounds and tie votes were cleared; new evaluations use the current direct financial/technical marks. App remains running from this directory on 7130/5072. No push or merge performed.

Canonical directory: `C:\Users\user\Documents\ChatGPT\MVC Project-main`.
Branch: `ghaith`. Run localhost:7130 only from this project. The other similarly named checkout is stale; do not edit/synchronize it unless asked.

## Current stopping point
Completed the user's full local QA and remaining-role UI/UX rework. Read `QA-REPORT-2026-11-16.md` for the detailed coverage/results/limits. This checkpoint supersedes the earlier role coverage that listed five roles as awaiting a full rework.

### UI/UX coverage
- Department: request form already reworked; request history/details navigation, filters, progress and contrast reviewed/refined.
- Sourcing Officer: existing request/tender/offer/report/dashboard work retained and checked; report popup decision paths, confirmations and colors verified.
- Evaluation Committee: evaluations, weights/member forms, tender/offer/report selection, reporting, dashboard and clarification entry reviewed/reworked.
- Sourcing Manager: submissions, chosen offers, reports, clarifications and dashboard reviewed/reworked.
- CEO & Chiefs: decisions, tender detail/voting/comparison, history, reports, clarifications, notifications and dashboard reviewed/reworked. Optional filters disclose progressively.
- Supplier: invitations/details, offer form, offers/details, profile, notifications, clarifications/history and dashboard reviewed/reworked.
- Admin: users, departments, suppliers/applications/review, Contact Us inbox/details and dashboard reviewed/refined.
- All seven dashboard charts and short previews preserved. No role-wide rework remains queued from this request; next step is user feedback or separate production/hardware verification.

## Preserve agreed behavior
- Keep CEO & Chiefs wording; supplier My Offers replaces the History sidebar link.
- No tender draft stage, no tender edit button/popup, and no automatic offer expansion.
- Approved suppliers only in supplier workspaces.
- Normal handoff: three distinct compliant suppliers. Up to four offer rounds. Final one/two-offer exception requires written reason. Zero compliant offers blocked.
- Confirmed compliance decisions are final and require explicit confirmation.
- Four committee members score every eligible offer. Agreed financial/technical weights lock. Highest average total leads; tied leaders require unanimous resolution.
- Officer Review report opens a report popup on Evaluation reports, with recommendation/quote/full report/approvals/history and separate confirmations; decisions return to the report list.
- Supplier quote entry includes tax and extra costs; quantities/prices use numeric inputs. Nonnegative charges and inclusive total persist and round-trip.
- Contact Us/admin inbox, expired passkey401, committee redirect cleanup, current scoring/report/round/rejection integrations remain included.

## Verification
- Build: 0 warnings/errors; no EF model changes missing a migration.
- 93 isolated entry/Admin/contact/strict-round HTTP checks.
- 137 isolated full scoring/referral HTTP checks.
- 13 supplementary backend suites including 47 rejection/replacement checks.
- 331 CEO & Chiefs foundation/phase/history/navigation checks.
- 58 authorization/anonymous/expired-passkey checks.
- 86 authenticated page checks across 13 demo accounts, desktop+390px mobile/dark, no JS/resource/overflow failures or final automated accessibility violations.
-Real browser supplier draft/save/submit, tax/extra total, upload boundary, native quantity validity; officer/manager/top confirmation cancellation and focus behavior.
-Session-owned fixtures removed. Live workflow decisions were not mutated in browser checks.

## Saved state and shutdown
New work saved locally on ghaith. The current request did not authorize publishing another push or merging into develop. Preserve this distinction when reporting.
Full report and local raw evidence are saved before the user-requested non-forced shutdown. A non-forced shutdown may be blocked by unsaved applications; do not claim powered-off status without observation.
Credentials remain outside Git in the existing encrypted DPAPI file; follow the user's AGENTS instructions. Raw browser screenshots/logs are local under output/qa and excluded from Git.

## Latest UI follow-up — 16 November 2026
Implemented the user-requested three-stage registration and supplier offer entry, two-stage department request form, Admin review stages, corrected authentication/confirmation actions,10-row request pagination/search/Clear/voice, row QR dialogs, offer detail popups, JD display and percentage-tax contract. Removed At a glance/open-round copy/empty tender-documents UI; transient notices dismiss after about3seconds; replacement indicators cover all applicable owned tenders. See UI-FOLLOWUP-2026-11-16.md for exact scope and verification.
Tax is now entered as TaxPercentage (0–100); server derives stored Bid.TaxAmount from item subtotal. Raw TaxAmount posts are not trusted. ISO storage stays JOD, displayed as JD.
Current app is running on7130 from this canonical project. No shutdown should be carried over from the earlier overnight task. No new push/merge is authorized by this follow-up.

## Shared public navbar follow-up — 16 November 2026
Pulled origin/develop (088cb38) into ghaith with a safety stash retaining all pre-pull work. Ported the existing shared public navbar fix from the other checkout: one consistent header on Home and public Account pages, active links, responsive wrapping, correct homepage canvas/scroll offset; retained Inside Tiryaq. Canonical page content and local changes retained. Build passed with 0 warnings/errors. Browser checks: eight routes at 1440/390 widths, all HTTP 200, one shared header, no horizontal overflow, homepage stage aligned below header. Sourcing/Home link clicks passed. App remains running at https://localhost:7130. No push performed.

## Final integration — 16 November 2026
User explicitly authorized integrating missing changes from the sourcing-tender-management-system-develop checkout and pushing a new branch named `Final`. Current branch: `Final`, based on the pulled develop commit 088cb38, retaining canonical local changes.

Imported the source checkout's public sourcing campus/Contact styling, AI advisory narrative recovery with verified quote facts and disclosure, AI result redirect, and extra passkey regression checks. Completed its saved officer tender-list design: explicit selection, searchable rows, Back to tenders clearing ambient bid IDs, direct links and ownership checks retained. Preserved canonical registration animation, seven-row pagination, sourcing video/transcript, Inside Tiryaq city and team animation. Existing shared public navbar remains consistent on all public pages.

Verification: normal build 0 warnings/errors; mocked AI recovery and report safeguards; 140 isolated scoring MVC checks; 97 isolated procurement entry MVC checks; 33 isolated authentication/passkey checks; 13 backend suites including 47 rejection checks. Public browser checks at 1440/390 widths: HTTP 200, one shared navbar, one h1, no horizontal overflow, sourcing stage controls and retained video, no page errors or failed local resources. Tender-list browser checks: search/no results, workspace links, direct bid return, desktop/mobile with no horizontal overflow after scoped grid specificity fix. Updated one stale report-popup check to match its existing compact recommendation/PDF design; protected decision and approval assertions retained. All task-created SQL databases and upload fixtures removed.

See FINAL-INTEGRATION-2026-11-16.md. Canonical app remains running on https://localhost:7130. No push to develop, no shutdown and no new migrations. Generated output, private uploads, appsettings.Local.json and encrypted credentials remain outside the published changes.
