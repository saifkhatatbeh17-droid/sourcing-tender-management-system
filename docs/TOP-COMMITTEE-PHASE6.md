# Top Committee Phase 6 — Notifications

Completed locally on `Mohammad`, 2026-11-13, after the Phase 5 build/test gate passed.

## Existing infrastructure and implementation

The existing `Notification` entity/table supplies `NotificationId`, `UserId`, `Title`, `Message`, `RelativeUrl`, `IsRead` and `CreatedAt`. The existing shared notification bell and layout remain unchanged. No table, entity, mapping, constraint or migration was changed.

The Top Committee-specific page provides:

- Own-recipient list, persisted unread count and 20-row pagination.
- Visible read/unread distinction, created timestamp and empty state.
- Authenticated, anti-forgery-protected POST actions for mark-one and mark-all; repeated requests are safe.
- Database role/active-account revalidation and recipient ownership checks on every read/update.
- Tender links only for the exact internal Top Committee details route and a current assignment. Arbitrary stored URLs are not followed.
- Encoded notification titles/messages. Generated event messages contain no private clarification text.

`GET` does not mutate notifications. Mark-all affects unread rows present when the update runs; later events may correctly remain unread. Updates appear on refresh. There is no background worker, polling, automatic historical backfill or shared bell redesign.

## Event generation

| Event | Implementation | Recipient |
| --- | --- | --- |
| New assignment | Public integration hook; Manager team must call it | Each eligible assigned member |
| Manager answers clarification | Existing Top Committee Manager-answer service save | Original clarification creator only |
| Last open clarification resolved | Existing clarification save; only when relevant open count reaches zero | Eligible assigned members |
| Automatic award | Existing voting store save on final decision | Eligible assigned members |
| Rejection/return to Manager | Existing voting store save on terminal rejection | Eligible assigned members |

Partial approvals do not generate final-decision notifications. Existing approvals, rejection permanence, award and clarification transition rules were not changed.

Notification rows are enqueued and saved in the same existing tender transaction as the relevant state transition. The event helper requires the existing exclusive tender application lock. Duplicate checks cover both tracked and persisted recipient/title/tender-link event keys. No new unique index was introduced. This provides deduplication for cooperating writers using that lock and stable event titles; arbitrary external writers bypassing the contract are outside that guarantee.

## Manager integration contract

The assignment hook is `TopCommitteeNotificationEvents.PublishAssignmentsAsync(ITopCommitteeTenderTransaction transaction, ClaimsPrincipal manager, int tenderId, CancellationToken ct)` in `Services/TopCommittee/TopCommitteeNotificationEvents.cs`.

The Manager team should call it after persisting the valid referral and committee assignments, using the real authenticated principal and injected existing tender transaction coordinator. It obtains the shared tender lock, revalidates the active Manager role, requires the unique responsible referring Manager to match, requires `AwaitingCommitteeDecision` with no Top votes, derives recipients from the database and saves notices. Repeated calls for unchanged assignment records do not duplicate notices. The assignment ID is part of the event key. The hook is a separate transaction after the referring team's persistence; if it fails, that team must handle/retry explicitly rather than claim atomicity with its earlier referral save.

Do not call it from a GET or trust a browser-supplied recipient ID. Do not bypass the shared coordinator from another writer. It is not wired into teammate-owned Controllers or Views in this delivery.

For Manager answers, integrate the existing `ITopCommitteeManagerAnswers` contract from `Contracts/TopCommittee/ITopCommitteeClarifications.cs`; its existing authorization/response transition is reused. The Top Committee clarification store now creates the notification inside that operation. The Manager response UI has not been modified or implemented in this phase. Direct table writes bypassing the service will not emit these events.

## Files

Created:

- `Controllers/TopCommitteeController.Notifications.cs`
- `Models/TopCommittee/CommitteeNotificationModels.cs`
- `Services/TopCommittee/TopCommitteeNotifications.cs`
- `Services/TopCommittee/TopCommitteeNotificationEvents.cs`
- `Views/TopCommittee/Notifications.cshtml`
- `Checks/TopCommitteePhase6Checks.cs`
- This document.

Updated: `Services/TopCommittee/TopCommitteeServiceCollectionExtensions.cs`, `Services/TopCommittee/TopCommitteeVotingStore.cs`, `Services/TopCommittee/TopCommitteeClarificationStore.cs`, `Views/TopCommittee/Dashboard.cshtml`, `Views/TopCommittee/Reports.cshtml`, `Checks/TopCommitteeFoundationChecks.cs`, `Checks/TopCommitteePhase2Checks.cs` and `Checks/TopCommitteePhase3Checks.cs`. The last two scope checks now explicitly allow the two approved notification POST actions; existing checks were retained.

## Validation

Phase 6 has seven non-SQL checks and 17 optional isolated SQL checks (24 total). Checks cover recipient ownership, CSRF attributes, encoded output, empty states, assignment hook authorization/idempotency, four-approval award notices, duplicate voting, rejection notices, private creator-only answer notification, all-members resolution notification, mark-one repeat safety, cross-user rejection, mark-all persistence across contexts and unchanged EF model.

Write checks used only the freshly created `SourcingTender_Phase46_e83cfb1a37e943a38ab0037677daabba` database. Its synthetic accounts were created through Identity; no original account password hashes, authentication secrets or personal data were copied. The database is retained, not automatically deleted. Optional SQL checks consume a fresh three-tender fixture; rerunning them requires a new isolated fixture, not reusing the consumed database or resetting the original.

Real browser checks observed four unread notices for member 2, one read becoming three unread, persistence after logout/login, mark-all becoming zero after refresh, member 3 retaining four unread and an unassigned test user seeing an empty list. Desktop/mobile, unread and empty states were inspected. Real HTTP tests independently checked missing anti-forgery rejection, cross-recipient update rejection and repeat-safe own updates.

All original database integrity fingerprints matched after testing. See the final combined report for totals and remaining integration boundaries.
