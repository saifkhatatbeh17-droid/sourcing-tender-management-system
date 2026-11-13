# Top Committee Phase 1 foundation

## Scope

Phase 1 adds read models, assignment/role checks, EF queries and a **read-only referral preflight**.
It does not add pages, controller actions, votes, clarification commands, notifications, award processing,
database entities, schema changes or migrations. Existing controllers and views are unchanged.

The existing `CK_TenderApprovals_3` still blocks Top rejection. The authorized constraint-only migration
belongs to Phase 4 and must be reviewed and explicitly approved before it is applied. This foundation
does not apply it or attempt to work around it.

## Registered services

`Program.cs` calls `AddTopCommitteeFoundation()` and registers these scoped services:

| Contract/service | Responsibility |
| --- | --- |
| `ITopCommitteeQueries` / `TopCommitteeQueryService` | Dashboard, paged assigned-tender list, tender details |
| `TopCommitteeAccessService` | Authenticated role, active database account, current database role and tender assignment |
| `ITopCommitteeReferralContract` / `TopCommitteeReferralContract` | Validate persisted referral inputs and return a preparation record; never save anything |

Read models live in `Models/TopCommittee/TopCommitteeViewModels.cs`. They are not EF entities and do not
change the model snapshot. Services receive the controller's trusted `User` principal, never an actor ID
or principal assembled from form fields.

## Query contract for Phase 2

- `GetDashboardAsync(User)` returns assigned-tender counts, up to ten pending tenders, ten recent
  decisions and per-member voting progress for the pending preview. Counts have the same assignment scope.
- `GetTendersAsync(User, filter)` supports title/nominated-supplier search, existing tender statuses,
  inclusive UTC creation dates and bounded paging (1-100 rows). Ordering is deterministic.
- `GetTenderDetailsAsync(User, tenderId)` requires an active Top Committee identity and a Top assignment
  on that tender. Missing and unassigned tenders produce the same `CommitteeTenderNotFoundException`.
- A current Top role in both the principal and database is required. Position roles do not confer
  additional power. An inactive or role-revoked user cannot use the services with a stale role claim.
- Future controllers must map `CommitteeAccessDeniedException` to forbidden/challenge as appropriate,
  `CommitteeTenderNotFoundException` to 404, and validation failures to form errors/400. No HTTP endpoints
  or global exception behavior were added in Phase 1.
- Details include request items, all bids with `SubmittedAt` set (including later lifecycle statuses,
  excluding drafts), prices in each bid's own currency, documents by ID/original name, submitted/approved
  evaluation scores, the current unified report, manager approval, members and recorded Top votes.
- Price totals are item quantity x unit price + tax + additional costs, matching the existing officer
  calculation. There is no currency conversion or invented combined/weighted evaluation score.
- Storage filenames and paths are not exposed. Future download actions must independently authorize
  the member and tender before opening a file. Read models are not authorization tokens.
- Only the current member's clarification conversations are returned. Other members' private text and
  responses are not returned. The aggregate open count still pauses voting for everyone.
- A relevant clarification is linked directly by `TenderId`, sent by an assigned Top member and addressed
  to a user with an existing Approved Manager approval on that tender. Pending and Answered both count
  as open. Future create/answer/resolve services must use this same definition.
- Results are ordinary reads, not historical snapshots or a transactionally consistent voting decision.
  Future writes must revalidate all state under a per-tender transaction lock. Reload retrieves updates;
  there is no polling or SignalR.

`Readiness.CanCurrentMemberVote` is a Phase 1 eligibility result, **not an implemented vote endpoint**.
It is false for rejection, wrong state, existing award, invalid/inactive four-member roster, missing or
ambiguous manager, missing recommendation, invalid nomination, any open clarification, or an existing
member vote. A previous Top rejection remains terminal even if the tender's status is later reset.

## Manager referral integration

The existing schema has no `ReferredByUserId`, referral timestamp, separate manager nomination or snapshot.
The minimal contract uses existing persisted data, without repurposing unrelated fields:

1. The responsible manager is identified by exactly one `TenderApprovals` record with `Stage = Manager`
   and `Decision = Approved`. That account must be active and hold `SourcingManager` in the database.
2. The authenticated caller must be that manager. Do not select a manager supplied by the browser or
   silently pick one when multiple managers have approvals.
3. The existing `EvaluationReports.RecommendedBidId` is agreed as the nominated bid for this integration;
   `ManagerSummary` contains the manager-authored recommendation. Coordinate this meaning with the report
   owner before hooking up manager UI. Committee/Officer summaries are not overwritten by this service.
4. The nominated bid must belong to the tender, have a submission timestamp, and must not currently be
   Draft, Withdrawn, Rejected or NonCompliant, nor have NonCompliant compliance status. This conservative
   nomination eligibility is explicit; displaying submitted non-winning offers does not make them eligible.
5. Exactly four distinct active Top members with distinct assignments/positions 1-4 must already exist
   in `CommitteeAssignments`. This preflight does not create or reassign members.
6. Tender status must be `AwaitingManagerReview` or an already-referred `AwaitingCommitteeDecision`,
   with no award fields. Any existing Top vote prevents a new referral, including after rejection.
7. Call `ITopCommitteeReferralContract.PrepareAsync(User, tenderId)`. It returns the database-derived
   manager, bid/supplier, recommendation, report ID, roster and `IsAlreadyReferred` flag. It does **not**
   mark the tender referred, save an approval, lock the nomination, create notifications or implement
   resubmission. It has no `SaveChanges` call.
8. A later referral command must run this validation again inside the same transaction used for its
   state transition. Freeze the nomination/recommendation and roster after referral. Other writers must
   honor that contract because the current database has no version/snapshot fields to enforce it.

The manager teammate can consume this contract without changes to their controller or views now.
Actual referral persistence and notifications remain later-phase work; no placeholder command throws
`NotImplementedException` or misleadingly reports a saved referral. The Manager clarification-response
command will be defined in Phase 3, not implemented as part of this phase.

## Read-only verification

From the repository directory:

```powershell
dotnet build --no-restore
dotnet run --no-build --no-launch-profile -- --top-committee-foundation-check
```

The dedicated check branch runs **before** startup role seeding and before `app.Run()`. It does not start
the website. It checks rules and successful read-model mapping using synthetic in-memory objects only,
then exercises real SQL authorization, filters, dashboard queries, the empty details query and referral
denial paths. A command interceptor rejects non-SELECT commands for the check context. The EF model must
have no pending changes. Existing users/passwords are not modified and no fixtures are inserted.

At least one existing active Top account is needed for live read checks; otherwise the check fails with
an explanation instead of seeding an account. Extra role-denial checks use existing non-member/manager
accounts when available. If an assigned tender exists, the successful detail read is also exercised.
If none exists, that limitation is printed and the success-path projection is tested only in memory.
This does not claim that persisted referral, voting, concurrency, rejection or award have been tested.
The older `--self-test`, SQL fixture checks and HTTP smoke tests are not read-only and are not invoked.

## Files and ownership

- New: `Contracts/TopCommittee/`, `Models/TopCommittee/`, `Services/TopCommittee/`,
  `Checks/TopCommitteeFoundationChecks.cs`, and this document.
- Shared change: `Program.cs` registers services and adds the explicit read-only check switch before
  the existing role seed. Normal startup, authentication and existing routes retain their behavior.
- Unchanged: all controllers/views, entities, `ApplicationDbContext`, migrations, roles and Identity setup.
- The pre-existing `Microsoft.EntityFrameworkCore.Tools` addition to the project file was preserved.

Phase 2 is the core pages using these services. Phase 3 owns clarification commands. Phase 4 owns voting,
atomic award, concurrency and the separately approved database-constraint application. Stop after Phase 1.
