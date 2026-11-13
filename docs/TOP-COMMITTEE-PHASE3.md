# Phase 3: Top Committee clarifications

## Implemented boundary

This phase adds private Top Committee-to-Sourcing Manager clarification commands and a Top Committee page. It uses the existing `Clarifications` and `ClarificationResponses` entities and their current SQL Server constraints. No schema, entity, EF mapping, migration, Manager controller/view, voting endpoint, notification, report or export changes are included.

Creation requires an authenticated, active database user with the TopCommittee role and a current Top assignment to the tender. The tender must be `AwaitingCommitteeDecision`, have no award and no recorded Top rejection. The creator must not have a recorded Top vote. The recipient is derived from exactly one `TenderApprovals` row with `Stage = Manager` and `Decision = Approved`; that user must be active and retain the SourcingManager role. Ambiguous/missing managers and self-addressed questions are rejected rather than guessing or violating the sender/recipient constraint.

The saved association is `TenderId` only, with `Pending` status. Subject and question text are limited to 200 and 4000 characters. A second submission with the same trimmed subject and description by the same member for the same tender is rejected, including after resolution. This is content-based duplicate protection; no durable request-key column has been added. It does not merge or delete previous questions.

Only the responsible Manager may add one nonblank response (maximum 4000 characters), atomically changing `Pending` to `Answered`. Only the original, still-authorized creator can change `Answered` to `Resolved`, and only if a nonblank response by the responsible Manager already exists. The response and existing approvals are preserved. Awarded, rejected or otherwise closed tenders do not accept these transitions. Existing inconsistent records are not repaired automatically.

## Manager-team integration contract

The DI-registered `ITopCommitteeManagerAnswers` in `Contracts/TopCommittee/ITopCommitteeClarifications.cs` exposes `AnswerAsync(manager, clarificationId, answer, cancellationToken)` and returns the trusted tender/clarification IDs. No Manager-side action or view is added in Phase 3.

The Manager team should inject this contract in its own controller or application service, supply the server-authenticated `User` principal, the selected clarification ID and response text, and call it from an authorized, anti-forgery-protected POST. Do not accept a Manager ID or tender ID as proof of authorization. The contract derives the tender and recipient, then revalidates identity, current database role/activity, responsible Manager, status and absence of previous responses under the tender lock.

Expected outcomes are `CommitteeAccessDeniedException` (forbidden identity/role), `CommitteeTenderNotFoundException` (missing or inaccessible conversation; avoid disclosing private content), `ValidationException` (invalid answer) and `CommitteeStateConflictException` (stale/duplicate/closed state or lock timeout). The caller should display appropriate validation or reload messages. It should not directly update clarification status or insert another response outside this contract. Manager-side listing/display and its HTTP exception handling remain the Manager team's responsibility; this interface does not supply a Manager inbox query.

## Privacy and relevance

The Top Committee page only projects questions whose sender is the authenticated member, together with their responses. It never projects another member's subject, message or responses. An aggregate unresolved count for the tender is visible to its assigned members so the shared voting pause can be explained without disclosing the conversations. Tender selection is checked against the user's assignments server-side.

The shared relevance predicate requires a direct tender clarification, a sender assigned to that tender's Top Committee and a recipient recorded as an approving Manager for that tender. Purchase Request/Bid conversations and conversations outside those relationships are not changed or counted. The current schema has no immutable workflow-origin/assignment-history marker: team code must preserve the referring Manager approval and committee roster while the committee workflow is active. Reassigning/removing those relationships can remove an old conversation from the relevance predicate; a roster/referral change requires a separately agreed policy. Phase 3 does not alter or infer that policy.

## Required Phase 4 concurrency contract

`ITopCommitteeVotingPause.GetAsync` supplies an authorized read-only pause summary for display. It is **not** an authorization to insert a vote later.

All Phase 3 commands use `ITopCommitteeTenderTransaction.ExecuteAsync`, which opens a fresh context and SQL transaction, acquires the transaction-owned exclusive `sp_getapplock` resource `TopCommittee:Tender:{tenderId}`, and takes an update lock on the existing tender row. Permissions and transitions are checked after acquiring the lock, and changes are saved before the transaction commits. No new database object or persisted lock row is created. SQL Server's `sp_getapplock`/`APPLOCK_MODE` permissions are required; lock acquisition failure returns a state conflict, not an unsafe fallback.

Phase 4 must execute the following inside the **same** `ExecuteAsync` callback, using its supplied context:

1. Recheck the actual member's active status, role, assignment, tender state, nomination, previous vote, rejection and award state.
2. Call `ITopCommitteeVotingPause.AssertNotPausedAsync` with that context and tender ID. It requires the matching exclusive transaction lock and rejects any relevant `Pending` or `Answered` clarification.
3. Insert the vote and perform any permitted automatic award using the same context, save, and allow the coordinator to commit.

An outside-transaction pause read followed by a later vote write is unsafe. The coordinator serializes clarification creation, answers, resolution and future voting for one tender, so a creation cannot race past the final eligibility check. Existing approvals are never reset. Every other writer to this workflow must use this contract; direct SQL or legacy code that ignores it is outside its guarantee. Manager handoff and roster changes also need coordination if introduced. No voting command is implemented here. The existing `CK_TenderApprovals_3` rejection limitation is unchanged and still requires a separate Phase 4 decision.

## Verification and practical limits

Run `dotnet build --no-restore`, then `dotnet run --no-build --no-launch-profile -- --top-committee-foundation-check`. This dedicated check mode runs before startup role seeding. Do not substitute the unrelated `--self-test` path, which can write database fixtures.

Phase 3 checks cover command authorization, valid creation/answer/resolution, invalid tender states, own-vote blocking, self-recipient and ambiguous Manager rejection, wrong Manager responses, duplicate requests, creator-only resolution, cross-tender access, remaining-open clarification pause, approval preservation, Razor encoding, POST/anti-forgery declarations, empty/blocked UI and concurrent transitions. Mutation cases use an in-memory session store. A real SQL Server check uses two independent transactions and an absent tender ID to verify serialization, commit/rollback release and rejection of a missing/wrong lock; it saves no SQL records.

No assigned tender exists in the review database at verification time. SQL query execution and locking are exercised, but a successful persisted clarification/response round trip and authenticated browser POSTs have not been tested against a seeded database. Those should be checked in an agreed test environment with a real referral and assignments before release. Optional Razor preview fixtures are rendered to an explicitly supplied directory outside the repository and are never seeded into SQL or production.

The interface uses the existing shared layout and scoped Top Committee styles, with refresh-based updates. It adds no realtime updates and no notification delivery. Stop after Phase 3; Phase 4 requires separate approval.

## Phase 3 delivery file manifest

Paths below are relative to the project root. New means introduced in Phase 3; modified includes Phase 1/2 files that remain uncommitted. Existing unrelated working-tree changes were preserved.

| Change | File | Purpose |
| --- | --- | --- |
| New | `Contracts/TopCommittee/ITopCommitteeClarifications.cs` | Member commands, page query, Manager response, pause and transaction contracts |
| New | `Models/TopCommittee/TopCommitteeClarificationViewModels.cs` | Input validation and private page/thread DTOs |
| New | `Services/TopCommittee/TopCommitteeClarificationQueries.cs` | Authorized private conversations and create/resolve availability |
| New | `Services/TopCommittee/TopCommitteeClarificationRules.cs` | Identity, assignment, Manager and state transition rules |
| New | `Services/TopCommittee/TopCommitteeClarificationScope.cs` | Shared relevance predicate and open-count rule |
| New | `Services/TopCommittee/TopCommitteeClarificationService.cs` | Atomic create, answer and resolve commands |
| New | `Services/TopCommittee/TopCommitteeClarificationStore.cs` | Fresh-context SQL persistence session and test seam |
| New | `Services/TopCommittee/TopCommitteeTenderTransaction.cs` | Per-tender SQL transaction/application-lock coordinator |
| New | `Services/TopCommittee/TopCommitteeVotingPause.cs` | Authorized pause read and locked Phase 4 backend assertion |
| New | `Views/TopCommittee/_ClarificationThread.cshtml` | Encoded conversation/status/response and creator resolve form |
| New | `Checks/TopCommitteePhase3Checks.cs` | Focused Phase 3 command, race, SQL lock and Razor checks |
| New | `docs/TOP-COMMITTEE-PHASE3.md` | Manager handoff, Phase 4 contract, limits and delivery manifest |
| Modified | `Controllers/TopCommitteeController.cs` | Authorized clarification GET and two CSRF-protected clarification POSTs |
| Modified | `Views/TopCommittee/Clarifications.cshtml` | Private list, tender selection, create form and messages |
| Modified | `Views/TopCommittee/TenderDetails.cshtml` | Link to the member's clarification page for that tender |
| Modified | `Views/TopCommittee/_ReadStyles.cshtml` | Preserve clarification message line breaks |
| Modified | `Services/TopCommittee/TopCommitteeQueryService.cs` | Reuse the centralized clarification scope in existing read models |
| Modified | `Services/TopCommittee/TopCommitteeServiceCollectionExtensions.cs` | Register Phase 3 services/contracts |
| Modified | `Checks/TopCommitteeFoundationChecks.cs` | Invoke Phase 3 checks from the existing explicit check mode |
| Modified | `Checks/TopCommitteePhase2Checks.cs` | Updated constructor and POST scope guard; reuse Razor check helpers |

Verification result: build succeeded with 0 warnings and 0 errors; 42 foundation + 33 Phase 2 + 65 Phase 3 checks passed (140 total). The changed UI detector returned no findings. Desktop (1280 × 900) and mobile (390 × 844) rendered previews were inspected, including empty, blocked, validation-error and maximum-text states. No shared design changes were made.
