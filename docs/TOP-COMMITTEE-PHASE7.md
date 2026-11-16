# Top Committee Phase 7 — Decision History

Completed locally on `Mohammad`, 2026-11-13. No Manager integration or subsequent phase was started.

## Definition and implemented functionality

History is the chronological record of the authenticated member's persisted `TenderApprovals` where `Stage = Top`, `UserId` is the authenticated member, and `Decision` is `Approved` or `Rejected`. A current `Top` assignment on the same tender is required. Other members' votes do not create entries in this member's history.

The page shows the member's own decision and `DecidedAt` timestamp in UTC, tender identification/title, current status, committee approval count, current nominated bid and awarded bid when present. Every row links to the existing independently authorized Tender Details action. There are no edit/delete or vote POST endpoints in this phase.

Collective outcome is separate from the member's vote:

- Any persisted Top rejection means voting permanently ended, even if someone resets the tender status.
- `Awarded` is shown only with the persisted Awarded status, awarded bid and awarded timestamp. The existing voting command remains responsible for four approvals and the atomic award; History does not infer an award from a count.
- An unrejected tender in `AwaitingCommitteeDecision` is labeled `Pending collective decision`, including after the current member has approved.
- Other states are labeled `No final committee outcome recorded`; no final result is invented.

Clarifications are not decisions and do not generate History rows. Their private questions/responses are excluded. Their pause/resume behavior remains on the existing Clarifications/Tender Details pages. History is not a duplicate of Reports: it starts from this member's completed votes, sorts by vote time and does not include unvoted assignments or export/statistics sections.

## Filters, pagination and performance

- Tender-title search, maximum 200 characters.
- Existing tender status and own Approved/Rejected decision filters, validated server-side.
- Default 20 rows; supported page size 1–100 (UI offers 10/20/50/100), page 1–1,000,000.
- Stable newest-first ordering by `DecidedAt`, then unique `TenderApprovalId` descending.
- Invalid bound/filter values return HTTP 400 and a validation message; missing/inaccessible data does not broaden scope.
- Empty/no-match/out-of-range pages show explanatory text and a reset link.
- Three SQL queries per successful request: account/role validation, filtered count, bounded projection. `AsNoTracking`, correlated counts/exists and projection avoid entity graph loading and per-row database requests.

The existing shared layout and `_ReadStyles` remain unchanged. The table uses the established responsive internal scrolling region and labeled filters. No package was added.

## Authorization and read-only behavior

The partial Controller retains the existing class-level `Authorize(TopCommittee)` and no-store response policy. The service independently rechecks the authenticated Identity ID, active database account and current database TopCommittee role. Both own-vote and current-assignment conditions are applied in SQL before filtering/pagination. Client `UserId`/`TenderId` query parameters cannot replace that scope. Inaccessible Details continue to return 404 through existing authorization.

Titles and filter values use ordinary Razor encoding. No other member's vote text, private conversation content, account secrets or storage paths are projected. Committee-wide approval/rejection aggregates are displayed only for an already-authorized tender. The feature contains no SaveChanges, data mutation, migrations or schema operations.

## Exact repository file manifest

Created:

- `Controllers/TopCommitteeController.History.cs`: GET History, filter validation and existing forbidden/not-found handling.
- `Models/TopCommittee/CommitteeHistoryModels.cs`: filter, own-vote row, collective-outcome display and page DTOs; not EF entities.
- `Services/TopCommittee/TopCommitteeHistory.cs`: authorized, filtered and paginated read-only projection.
- `Checks/TopCommitteePhase7Checks.cs`: focused rules, security, Razor and optional isolated SQL checks.
- `docs/TOP-COMMITTEE-PHASE7.md`: this report.

Modified:

- `Controllers/TopCommitteeController.cs`: remove the old History placeholder action; constructor/other actions unchanged.
- `Views/TopCommittee/History.cshtml`: replace placeholder with the functional page.
- `Services/TopCommittee/TopCommitteeServiceCollectionExtensions.cs`: register the scoped History query service.
- `Checks/TopCommitteeFoundationChecks.cs`: invoke Phase 7 checks after Phase 6.

All other baseline files are preserved, including teammate Controllers/Views/services, entities, DbContext, migrations/snapshot, packages, shared layout/styles and existing voting/clarification commands. No commit, push, merge, reset, clean or branch switch was performed. Pre-existing modified/untracked work remains present.

Final SHA-256 comparison against the 234-file starting baseline found exactly the four modifications listed above, 230 unchanged files and zero missing files; exactly the five listed files were added. The final branch check returned `Mohammad`.

## Build and automated test results

Full project build passed with zero warnings and zero errors.

The combined run passed **336 C# checks**:

| Suite | Passed |
| --- | ---: |
| Phase 1 foundation | 42 |
| Phase 2 | 33 |
| Phase 3 | 65 |
| Phase 4 | 114 |
| Phase 5 including isolated SQL | 22 |
| Phase 6 including fresh isolated SQL writes | 24 |
| Phase 7 including read-only isolated SQL | 36 |

Phase 7 comprises 23 base checks and 13 optional SQL checks. Coverage includes empty history, individual approval versus collective outcome, award, terminal rejection/status reset, unrecorded outcomes, invalid/incomplete award, anonymous/wrong-role/stale identity, invalid search/status/decision/page/page size, SQL own/assignment scope, another member, unassigned access, search/status/decision filters, repeatable pagination, chronological order, no tracked entities, Razor encoding, Details URLs and absent vote mutation controls. Prior Phase 1–6 checks remain intact.

The normal check command without optional SQL variables runs **298 checks**: 275 previous base checks plus 23 Phase 7 base checks. The 336 total includes 38 optional SQL checks across Phases 5–7. Phase 6 optional write checks require a fresh fixture and must not be rerun against their consumed database.

The normal 298-check run was repeated after implementation/documentation and passed; the final build again reported zero warnings/errors.

Two test assertions were corrected during development: a whole-layout POST search incorrectly counted the legitimate shared Logout form, and an external HTTP pagination assertion incorrectly counted authorized links in the shared notification bell. The corrected assertions target vote mutation controls and the History main region respectively. No application authentication or shared-layout behavior was changed to satisfy them.

## Real MVC/Identity and browser evidence

An additional **27 real HTTP checks passed**, counted separately from the C# suite. They used actual Identity login/cookies and logout CSRF forms against the running MVC app. Coverage: anonymous redirect, own three-record history, pending/rejected/awarded outcomes, newest-first page 1/page 2, search/status/decision filters, six invalid filter cases returning 400, ignored client identity/tender tampering, another member's one-record history, unassigned empty history, unassigned Details 404 and wrong-role AccessDenied.

Actual browser interactions executed:

1. Anonymous History navigation redirected to Login; signed in as synthetic member 1.
2. Opened History through existing workspace navigation and saw persisted Awarded and Rejected decisions.
3. Opened tender 3, approved through the existing confirmation modal and real anti-forgery POST, then returned to History. The new own vote appeared first, while the collective outcome remained Pending with 1/4 approvals and no award.
4. Applied Rejected filter and saw one matching own decision; reset the filters.
5. Searched for an unmatched title and saw the empty state.
6. Signed out/in as member 2; saw only that account's one award-related approval, without member 1's rejection or pending vote.
7. Signed out/in as an unassigned Top member; saw zero decisions and no tender rows.
8. Signed out, closed the test tab, cleared the viewport override, hid the temporary browser and discarded the in-memory credential. The owned test server was stopped.

Desktop 1280×900 and mobile 390×844 were captured and visually inspected. Mobile DOM measured document width 390 and table scroll width 672: horizontal scrolling stays inside the table. Evidence outside the repository:

- `C:\Users\USER\.codex\phase56-preview\history-desktop.png`
- `C:\Users\USER\.codex\phase56-preview\history-mobile.png`
- `C:\Users\USER\.codex\phase56-preview\history-empty-mobile.png`
- `C:\Users\USER\.codex\phase56-preview\history-unassigned-empty.png`
- `C:\Users\USER\.codex\phase56-preview\history.html` and `history-empty.html`: separate synthetic Razor previews, not browser SQL evidence.
- `C:\Users\USER\.codex\phase56-audit\Phase7Http.cs` and `Phase7Http.csproj`: external HTTP harness, no plaintext credential stored.

These are the scenarios actually executed; this phase does not claim a repeat of every previous manual browser test.

## SQL isolation and original database integrity

Original database: `DESKTOP-4CBPOU8 / SourcingTenderManagementCodeFirst`. It was used only for read-only checks and integrity comparison, with no normal app server pointed at it during browser tests.

Read-only Phase 5/7 SQL fixture: `SourcingTender_Phase46_b4e8d015226040b8a16507d33fda0bcc`.

Fresh Phase 6 regression/browser database: `SourcingTender_Phase46_d4bb19c0671041a1bb940b6a8f1a6772`. The existing isolated setup harness created its current migrated schema and synthetic accounts through Identity; original account password hashes, personal information and secrets were not copied. All write tests and the browser approval were restricted to this test database. No original account passwords were changed. The database was retained, not deleted.

Final SQL verification showed: tender 1 Awarded to bid 1 with four distinct approvals; tender 2 AwaitingManagerReview with one justified rejection; tender 3 AwaitingCommitteeDecision with member 1's approval, no award, and its earlier Resolved clarification/one response preserved. The integrity harness returned **`ORIGINAL_INTEGRITY: True`** after all browser/SQL actions, comparing original application/Identity data, migration history and schema fingerprints.

`git diff --check` passed. Git emitted its ordinary LF-to-CRLF normalization notice for the History view; no whitespace error was reported.

## Limits

- History stores no new snapshots. Own vote/time are persisted historical values; title, status, nomination, assignment scope and collective outcome reflect current records. A removed assignment removes access, rather than exposing old tender data.
- Missing historical nomination versions/referral timestamps are not invented. Existing data inconsistencies are displayed conservatively without repairs.
- The count and page are separate read queries, so concurrent changes may be visible on refresh; this is not a transactionally immutable audit export.
- Clarification content/activity is intentionally outside this vote chronology. Existing private Clarifications remains the appropriate screen.
- No Manager referral, Manager clarification UI, teammate integration, AI or biometric work was implemented.

Stop after Phase 7 for user review.
