# Main local project

Use this directory: `C:\Users\user\Documents\ChatGPT\MVC Project-main`.
It is the sole project to edit and run for localhost:7130.
The other checkout is an older integration source, not the running application.

## Integrated functionality
- Approved suppliers only for supplier workspaces.
- Committee navigation clears stale offer references.
- Expired passkey assertions return a sign-in error rather than HTTP500.
- Contact form persists messages, notifies administrators and exposes an admin inbox/read state.
- Four committee members review every eligible offer; ranking uses average recorded financial and technical marks. All required reviews must be complete. Highest score is recommended; tied leaders require unanimous tie resolution.
- Report exports and CEO/Chiefs views use the recorded score limits and include offer rounds and exception reasons.
- Three distinct compliant suppliers for normal handoff, up to four offer rounds, a reason required for the final-round shortfall exception, zero compliant offers always blocked.
- Confirmed compliance decisions are final; confirmation dialogs save per-offer decisions.
- No tender draft stage, no tender edit button, no automatic offer expansion.

## Local operation
Create a tender through Publish & invite suppliers. Do not run a second checkout against the same local application. Preserve local work. On 16 November 2026 the user authorized committing and pushing this checkpoint to `ghaith`; merging into `develop` remains unauthorized.

## Verification
Committee scoring suite:134 HTTP/MVC checks, isolated disposable database.
Strict offer round regression:13 checks including shortage, deadlines, extensions and final exception.
Live Contact Us/admin inbox submission/read flow and expired-passkey401 checked.

# Checkpoint — 16 November 2026, branch ghaith

## Resume here
- Canonical project: `C:\Users\user\Documents\ChatGPT\MVC Project-main`.
- Run only this project on `https://localhost:7130`. The similarly named `sourcing-tender-management-system-develop` folder is an older integration source; do not edit or synchronize it unless asked.
- Current branch: `ghaith`. The user explicitly requested saving and pushing all current source work here. Do not merge into `develop` without a new instruction.
- Last completed work: shared, role-specific status charts on all seven dashboards, plus shorter list previews. No implementation step is currently left half finished. Continue from the user's next requested page or issue.

## Role coverage
| Role | Work completed in this session | Scope still available for a future review |
| --- | --- | --- |
| Department | Purchase request form rework, shared orange primary actions, request history action, request-progress charts; latest requests capped at five. | Remaining department pages have not all had a full UX review. |
| Sourcing officer | Action-first request inbox; tender/offer workspace rework; unread offer dots; immutable compliance decisions and confirmations; strict offer rounds; tender header; report library; report review popup with approve/request edits/reject and confirmations; Evaluation reports sidebar label; dashboard charts. | Continue polish only as requested; no unfinished change is pending. |
| Evaluation committee | Full scoring/ranking and navigation fixes integrated; dashboard review-progress charts; tender preview reduced from all 16 records to five with View all. | Dashboard work is complete; this does not represent a full visual rework of every committee page. |
| Sourcing manager | Latest scoring/referral/report and rejection handoff integration; dashboard charts limited to reports that reached manager review. | Full manager workflow UI polish is not complete across all pages. |
| CEO & Chiefs | Latest decision/report/scoring and offer-round information integrated; assigned-tender charts; previews capped at five pending and three recent decisions. | Full visual review of all decision/history/report screens remains outside the completed dashboard task. |
| Supplier | Approval access enforced; later-round resubmission resets compliance/unread state; own-offer status charts; latest invitations/offers/notifications capped at three each. | Other supplier pages have not all had a full UX review. |
| Admin | Contact Us persistence, notifications and inbox/read handling integrated; supplier-approval charts; pending application preview remains capped at five. | Other admin pages have not all had a full UX review. |

## Preserve these decisions
- Keep the name CEO & Chiefs; supplier My Offers replaces the separate History sidebar link.
- No tender draft stage, no tender edit button/popup, and no automatic offer expansion.
- Confirmed compliance decisions are final, with explicit confirmations; do not reintroduce an editable decision toggle.
- Normal handoff requires three distinct compliant suppliers. Up to four rounds; only final-round one/two-offer shortfalls can proceed with a written reason. Zero compliant offers cannot proceed.
- All four evaluation members must review every eligible offer. Highest average score leads; tied leaders require unanimous resolution.
- Review report opens a popup on Evaluation reports. It shows the complete report/recommendation/quote/approval and round records; decisions return to the report list. Server ownership, version, approval and clarification checks remain enforced.
- Dashboard previews stay short; chart aggregates cover the complete role-scoped dataset, independently of preview limits.

## Latest verification
- Build succeeded with zero warnings/errors.
- 137 real HTTP/MVC scoring/report/access checks passed using an isolated disposable database; local user database fixtures were not changed.
- All seven real role dashboards returned HTTP200, counts summed correctly, no JS errors, no document overflow at 390px; dark theme colors checked.
- Report popup checked on desktop/mobile, three confirmation paths, required reasons, Escape and focus restoration. Live user decisions were not submitted during UI checks.
- Earlier checks: 13 strict-round regressions; Contact Us/admin read flow; expired passkey401; pending supplier route restrictions.
- This checkpoint is not a claim that every page in every role has received a complete UX audit.

## Local assets and accounts
Generated QA screenshots stay local under `output/playwright/` and are excluded from Git. Demo credentials stay outside the repository in the current Windows user's encrypted DPAPI file; follow AGENTS instructions when credentials are requested. Do not commit secrets or local database files.
