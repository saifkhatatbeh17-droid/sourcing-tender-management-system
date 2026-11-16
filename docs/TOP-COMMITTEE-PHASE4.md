# Phase 4: voting, rejection and automatic award

## Delivery status

Implemented on `Mohammad`, preserving the earlier uncommitted phases. No commit, push or merge was made. No production records, roles, entity classes, teammate controllers/views, shared layout or shared CSS were changed. No Phase 5/6, reports, exports, notifications, biometric authentication, AI or voting rounds were added.

The **migration is prepared, not applied**. Both voting decisions fail closed until the expected enabled, trusted SQL constraint is available. The actual review database still has the old constraint, only `20261112190642_InitialCreate` in migration history, and no tenders. The application does not apply this migration on startup or attempt a schema workaround. The UI gives a temporary-unavailability message instead of offering approval while rejection is unavailable.

## Exact constraint change

Live SQL and original EF mapping:

```sql
([Stage]='Manager' OR [Decision]='Approved')
```

Proposed EF mapping and migration:

```sql
([Stage]='Manager' OR [Decision]='Approved' OR ([Stage]='Top' AND [Decision]='Rejected'))
```

Combined with the unchanged other constraints, Manager still permits Approved/Rejected/ReturnedForReview, Evaluation still permits Approved only, and Top permits Approved/Rejected. Top ReturnedForReview remains forbidden. No index or other constraint changes are included.

EF generated `20261113064343_AllowTopCommitteeRejection` with exactly two Up operations: drop `CK_TenderApprovals_3` on `TenderApprovals`, then add that same named constraint with the proposed definition. Down has exactly two operations: drop that constraint and add its exact original definition. The designer and snapshot reflect only this model change; the existing initial migration is untouched. Tests inspect the actual migration operation lists in both directions and evaluate all nine stage/decision combinations in read-only SQL.

**Rollback limitation:** after Top rejections have been stored, restoring the original constraint would reject those rows. Normal transactional Down application must fail and roll back rather than erase or relabel voting history. No automatic data deletion, decision rewriting, `NOCHECK`, or data-repair step is included. Any future rollback with rejected votes needs a separate approved plan; migration preparation is not approval to run either direction.

## Voting contract and state transitions

`ITopCommitteeVoting.SubmitAsync(User, input, cancellationToken)` accepts only the tender, decision, notes, confirmation and an expected nominated-bid ID. The expected bid ID is a stale-page check, never the source of the award nomination. User/assignment IDs are derived from the authenticated principal and database.

Within `ITopCommitteeTenderTransaction.ExecuteAsync`, the SQL store uses the supplied context to reload active status, database role, Top assignments, existing approvals, referring Manager approval, report/recommendation and nominated bid. The same context is supplied to `ITopCommitteeVotingPause.AssertNotPausedAsync`. No unlocked read or second context authorizes a write. The actual constraint readiness is rechecked inside this transaction as well.

The command requires four distinct active members with the TopCommittee role, distinct assignments and positions 1–4; an eligible submitted nomination from the same tender; exactly one active approving Sourcing Manager; a nonblank Manager recommendation; `AwaitingCommitteeDecision`; no award or historical Top rejection; and no vote by this member. Existing votes must match the roster, assignment, tender and permitted decisions. Invalid historical records block further action without changing those records.

An approval inserts `Stage=Top`, `Decision=Approved`, the trusted member/assignment, UTC timestamp and optional notes. The fourth valid approval also sets `Status=Awarded`, the database-nominated `AwardedBidId`, `AwardedAt` and descriptive `AwardNotes`. A single SaveChanges and the coordinator's transaction cover the new vote and award together. Earlier votes are retained and no Bid or EvaluationReport status is repurposed.

A rejection requires nonblank justification (maximum 4000 characters, stored in Notes). It inserts an actual Top/Rejected decision and changes only the tender's status to `AwaitingManagerReview`. Previous approvals and the rejection remain intact. The tender is not cancelled. Any historical Top rejection permanently blocks voting even if another writer later resets the tender status; the existing referral preflight also refuses a tender with a Top vote. No round/resubmission mechanism is added.

The shared per-tender application lock serializes voting against clarification create/answer/resolve commands. Pending and Answered conversations block both Approve and Reject; only all relevant conversations being Resolved allows voting to resume. The existing unique indexes and assignment foreign keys remain additional database defenses. Duplicate-key/concurrency failures produce reload messages; failed database transactions roll back through the Phase 3 coordinator.

## UI and authorization

Tender Details adds a Top Committee voting partial, progress out of four and refresh-based read-only recorded votes/notes. Eligible users choose Approve/Reject and review a Bootstrap confirmation modal naming the tender and nominated bid before a final POST. Rejection justification is checked in the browser and service. Modal cancellation preserves the draft; the browser suppresses repeated submission, while server-side rules remain authoritative. No polling or SignalR is added.

`SubmitVote` is role-authorized, tender-authorized and anti-forgery protected. Invalid input/state renders server validation with HTTP 400; inaccessible tenders return 404 and invalid identities/roles return forbidden. Successful POSTs redirect to the trusted tender ID. Closed, paused, voted, rejected, awarded and unavailable states hide the form. Notes remain HTML-encoded through the existing Razor rendering. Other assigned members can read recorded votes and notes but cannot edit them.

## Tests and observed limits

Build: `dotnet build --no-restore` succeeded with 0 warnings and 0 errors.

Checks: `dotnet run --no-build --no-launch-profile -- --top-committee-foundation-check` passed 42 foundation + 33 Phase 2 + 65 Phase 3 + 114 Phase 4 checks, **254 total**. This dedicated check mode runs before role seeding and never starts the normal application.

Phase 4 covers authorized approval/rejection, invalid/stale identities, assignment/status/Manager/nomination failures, duplicate/changed votes, rejection validation, incomplete/inactive/duplicate rosters, stale browser nomination, invalid historical assignments, Pending/Answered pause and resume, final award, terminal rejection despite status reset, duplicate/final/opposite-decision races, two remaining approvals, actual Phase 3 clarification creation racing with the fourth vote in both orders, and rollback/retry after simulated save/commit failure. MVC response mapping, Razor forms, CSRF declarations/tokens, state rendering, migration Up/Down and SQL truth tables are also checked.

Mutation and rollback scenarios use an isolated in-memory transaction store executing the real voting and clarification command services. The Phase 3 checks additionally verify the actual SQL application lock with independent connections, commit/rollback release and a missing/wrong lock. Phase 4 SQL checks only SELECT metadata, migration history and expression truth tables. **No real SQL vote persistence, DDL application or persisted final-vote/award rollback test has been run.** No test records were inserted into the team database. These are not EF InMemory-provider or SQL persistence tests.

Static previews render the actual Razor views with clearly named in-memory test fixtures to an outside-repository directory. Desktop (1280×900), mobile (390×844), approve/reject modals, required-reason behavior, paused/recorded/awarded/rejected/unavailable/error states were inspected. Main pages use full-page captures; modal captures show the active viewport. Mobile content stays within the viewport and existing table scrolling remains internal. The rejected preview deliberately uses a status-reset fixture to demonstrate that a historical rejection still ends voting. The UI detector returned no findings.

Before deployment, an explicitly approved isolated SQL test database is still needed to apply Up, verify SQL Server's stored constraint/readiness recognition and exercise authenticated POSTs, persisted voting, fourth-vote award and rollback. No such application is authorized or performed in this phase.

## Team integration

Manager referral persistence/UI and the Phase 3 response UI remain the Manager team's responsibility. They must retain the approving Manager relationship and freeze the nomination, recommendation and committee roster during active review. The browser's expected bid check catches a stale bid change, but the schema has no recommendation/roster version snapshot. Refer to the Phase 1/3 contracts for handoff and privacy scope.

Any future writer that changes votes, referral, nomination or committee membership must coordinate with the same tender transaction mechanism. Direct SQL or code bypassing that mechanism is outside its serialization/immutability guarantee. No database trigger or extra constraint was added to force unrelated code to comply. Old constraints left in place intentionally disable voting until the separate database-application approval.

## Phase 4 file manifest

Changes below are relative to the Phase 3 starting files, not to Git HEAD (earlier phases are still uncommitted).

| Change | File | Purpose |
| --- | --- | --- |
| New | `Contracts/TopCommittee/ITopCommitteeVoting.cs` | Vote command, result and authorized readiness contract |
| New | `Models/TopCommittee/TopCommitteeVoteInput.cs` | Validated decision, notes, confirmation and stale-nomination input |
| New | `Services/TopCommittee/TopCommitteeVotingDatabase.cs` | Read-only, fail-closed constraint readiness check |
| New | `Services/TopCommittee/TopCommitteeVotingStore.cs` | Persistence using the Phase 3 locked context and pause gate |
| New | `Services/TopCommittee/TopCommitteeVotingService.cs` | Immutable voting, terminal rejection and atomic award rules |
| New | `Views/TopCommittee/_Voting.cshtml` | Voting states, form and confirmation modal |
| New | `wwwroot/js/top-committee-voting.js` | Top Committee-only validation/modal/duplicate-submit behavior |
| New | `Checks/TopCommitteePhase4Checks.cs` | Isolated command/race/rollback, MVC/Razor and read-only SQL checks |
| New | `Data/Migrations/20261113064343_AllowTopCommitteeRejection.cs` | Only the approved constraint replacement and inverse |
| New | `Data/Migrations/20261113064343_AllowTopCommitteeRejection.Designer.cs` | Generated migration target-model metadata |
| New | `docs/TOP-COMMITTEE-PHASE4.md` | Delivery, migration review, limitations and ownership |
| Modified | `Controllers/TopCommitteeController.cs` | Voting POST and authorized Details readiness/validation |
| Modified | `Models/TopCommittee/TopCommitteePageViewModels.cs` | Vote form/readiness on the Details page |
| Modified | `Services/TopCommittee/TopCommitteeRules.cs` | Validate recorded votes against the actual roster |
| Modified | `Services/TopCommittee/TopCommitteeServiceCollectionExtensions.cs` | Register voting/readiness/store services |
| Modified | `Views/TopCommittee/TenderDetails.cshtml` | Voting partial, feedback, section link and scoped script |
| Modified | `Data/ApplicationDbContext.cs` | One authorized constraint-definition line |
| Modified | `Data/Migrations/ApplicationDbContextModelSnapshot.cs` | Same single constraint-definition update |
| Modified | `Checks/TopCommitteeFoundationChecks.cs` | Invoke Phase 4 checks |
| Modified | `Checks/TopCommitteePhase2Checks.cs` | Updated constructor and approved-POST scope guard |
| Modified | `Checks/TopCommitteePhase3Checks.cs` | Extend scope guard to allow the approved Phase 4 POST |

Stop after Phase 4. Do not apply the migration or begin Phase 5 without separate user approval.
