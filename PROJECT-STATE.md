# Main local project — latest checkpoint16 November 2026

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
