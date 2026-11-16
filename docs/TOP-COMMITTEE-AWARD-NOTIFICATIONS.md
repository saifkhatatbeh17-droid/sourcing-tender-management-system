# Related tender award notifications — verification

## Scope and exact files

- Modified: Services/TopCommittee/TopCommitteeNotificationEvents.cs.
- Added: Checks/TopCommitteeAwardNotificationChecks.cs.
- Added: docs/TOP-COMMITTEE-AWARD-NOTIFICATIONS.md (this record).

A SHA-256 comparison against the working tree before this task confirmed that no other existing tracked file changed. Earlier uncommitted reporting/UI changes were retained.

## Verified recipient relationships and destinations

| Recipient | Relationship | Destination | Example message |
| --- | --- | --- | --- |
| Sourcing Manager | Single active referring manager from this tender's approved Manager-stage TenderApproval record, already loaded by GetManagersAsync | /SourcingManager/ChosenOffers | Tender #41 has been awarded. Review the award outcome in Chosen Offers. |
| Sourcing Officer | Tender.CreatedByUserId | /SourcingOfficer/Tenders?id=41 | Tender #41 has been awarded. Review the selected offer in your tender workspace. |
| Requester | Tender.PurchaseRequestId → PurchaseRequest.RequestedByUserId | /Department/Details/20 | Tender #41 for your purchase request #20 has been awarded. View your request for reference. |

ChosenOffers accepts awarded tenders with an evaluation report (the award preflight requires that report). The officer action limits tender access to CreatedByUserId. Department Details limits requests to the current user's DepartmentId.

There is no department owner or notification mailbox relationship. The originating requester is the specific department recipient; other department users are not broadcast recipients. New recipients must have active accounts and the current destination role. A requester must still belong to the request's department. Missing/inactive accounts or revoked roles are skipped, never replaced with other users.

## Persistence and deduplication

The extension runs inside the existing Awarded/winning-bid branch, after the unchanged committee and supplier notifications. New planning additionally requires AwardedAt. It uses the same locked database context and adds Notifications without saving or committing independently. TopCommitteeVotingStore still invokes DecisionAsync before its existing SaveChangesAsync, and TopCommitteeTenderTransaction still commits or rolls back the whole operation.

Candidate IDs originate only from the tender's relationships. The account/role queries are restricted to those IDs. No role-wide recipient search is used.

New candidates are deduplicated by user ID; precedence is manager, officer, requester among eligible destinations. Users already receiving an existing committee or participating-supplier award notification receive no additional new notice. Existing committee/supplier notices are preserved, including their original payloads and behavior. The extension does not retrospectively combine any original dual-role notices.

The existing local/database recipient + title + URL check is retained for new notices. No uniqueness constraint or new notification system was added. All new titles are 'Tender #ID: awarded'; messages disclose only tender/request identifiers and the award result.

## Safe verification

- 31 new database-free checks passed, using the production voting service and production recipient-planning functions with a rollback-capable in-memory transaction/session adapter.
- Covered fourth-approval timing; actual relationship IDs; no unrelated role recipients; correct local destination paths; controller role attributes; active/role/department checks; missing request/account; ambiguous manager avoidance; dual-role deduplication; suppression of extra committee/supplier notices; repeated notice identity; failed save and simulated failed commit.
- 48 existing Phase 3 in-memory clarification checks passed in the temporary harness (SQL/MVC portions excluded).
- 24 comprehensive reporting regression/export checks passed, including SQL translation without a database connection.
- Source comparison confirmed every existing committee/supplier award-handler statement and the clarification/assignment handlers remained unchanged.
- SHA-256 comparison confirmed voting service, voting rules, transaction coordinator, controllers, schema/migrations, comprehensive reports, Face ID and shared UI were not changed by this task.
- Application build passed with zero warnings/errors using isolated output and UseAppHost=false to avoid the executable running in Visual Studio.
- git diff --check passed; line-ending normalization notices are not whitespace failures.

The new checks can be invoked as TopCommitteeAwardNotificationChecks.RunAsync() from a database-free harness compiled alongside the project sources, matching the existing Checks convention. The temporary harness also ran the bounded existing suites. Its two harness-only warnings (unused helper and BuildServiceProvider) are unrelated to the clean application build.

## Limitations

No SQL write test, actual fourth approval, live rollback, browser login or destination click was executed. Failure/rollback assertions describe the memory adapter; production atomicity was verified from the unchanged save/transaction path, not a new live SQL failure injection. Controller authorization metadata and ownership/department query predicates were inspected; browser authorization was not re-tested.

The requester link opens the existing purchase request, which is authorized for that requester. It is a reference destination, not a newly introduced tender-award page. No department UI was changed.

No email/push channel or mark-as-read feature was added. The shared bell continues to display existing notification records. Account/role/department changes after notification creation may make a previously valid link inaccessible, with existing destination checks enforcing access.

No database records were changed and no migrations/schema changes, voting/award/bid-status rule changes, supplier mark-as-read changes, authorization-policy changes or Git history/branch operations were performed.
