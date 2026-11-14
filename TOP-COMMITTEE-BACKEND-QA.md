# Top Committee backend QA — 14 November 2026

## Result

65 database-free checks passed. Application build passed with zero warnings/errors. Two findings require follow-up. No production backend, UI, database, authentication or migration files were changed during this QA.

## Findings

### P2 — Report decision date does not correspond to My decision

Location: Services/TopCommittee/TopCommitteeReports.cs, line 21.

The report projects MyDecision from the current user's Top-stage vote, but DecisionAt comes from tender.AwardedAt or the latest Top-stage vote by any member. If member A approves at 09:00 and member B approves at 10:00, A's report shows Approved with 10:00. After award it shows the award timestamp instead. This can misrepresent when the member recorded their vote.

Recommended fix: project the current member's DecidedAt alongside MyDecision; expose committee outcome/award time separately if needed. Verify with two different member timestamps and a later award timestamp. This is source-confirmed; no database fixture was inserted to reproduce it.

### P2 — Existing Phase 4 QA suite conflicts with the current voting contract

Locations: Checks/TopCommitteePhase4Checks.cs, line 45; Models/TopCommittee/TopCommitteeVoteInput.cs.

The test expects CommitteeStateConflictException when changing an existing approval into rejection. The current input model rejects all Rejected decisions during validation, so the actual exception is ValidationException with 'Only approval is supported in this committee workflow.' The isolated run stops at that assertion. Later rejection and voting-modal expectations also describe an older workflow.

Recommended fix: align the suite with the accepted approval-only workflow, retaining rejection tests as validation failures and historical rejection as a terminal-state guard. Rejection support must not be re-enabled merely to satisfy outdated tests.

## Executed checks

- 48 existing Phase 3 in-memory clarification checks: creation, ownership, role/account restrictions, duplicate questions, tender states, manager answers, resolution, privacy scope and simulated competing operations.
- 17 current voting/export checks: one-vote non-award; duplicate, anonymous, wrong-role and unassigned denial; stale nomination; confirmation; rejection validation; inactive/revoked actors; four approvals awarding the trusted nomination; clarification pause; simulated save failure and rollback; concurrent duplicate vote serialization in the memory store; Excel formula-like title kept as text; PDF rendering.
- Application build: dotnet build SourcingTenderManagement.CodeFirst.csproj --no-restore -p:UseAppHost=false -o %TEMP%/tender-backend-qa-build. Zero warnings/errors.
- git diff --check passed.

## Method and boundaries

The existing test sources and production C# sources were copied to %TEMP%/top-committee-backend-qa. Only those temporary copies were adapted to exclude SQL/MVC sections and run current approval-only cases against the existing in-memory session store. No database services were registered. The 48/17 results are for these explicitly bounded runs, not a claim that the complete Phase 3/4 integration suites passed. The temporary harness emitted two harness-only warnings (unused local function and BuildServiceProvider); the application build emitted none.

Source review covered the controller and partial controllers, database role rechecks, assigned-tender query scope, voting/readiness rules, transaction coordinator, clarification rules/scope/queries, notification ownership updates, history, reports and exports. POST actions have anti-forgery attributes. The voting path rechecks active database roles and nomination under a tender transaction and checks unresolved clarifications before saving.

No database commands, live approvals, notification updates, authentication changes, migrations or Git history/branch operations were run. Earlier Top Committee UI changes and restored Face ID presentation were preserved.

## Still unverified

- SQL query translation and actual database constraints/readiness.
- Real SQL application-lock serialization, transaction rollback and races with manager/supplier updates.
- Award-side supplier bid status changes and notification persistence in the same transaction.
- End-to-end HTTP authentication, CSRF middleware and live database-backed ownership checks.
- Large-result report performance and export limits with actual SQL data.

These require a separately approved disposable database test run; no live database write testing was performed.
