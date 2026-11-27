using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using SourcingTenderManagement.Contracts.TopCommittee;
using SourcingTenderManagement.Controllers;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Data.Migrations;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Models.TopCommittee;
using SourcingTenderManagement.Services.TopCommittee;

namespace SourcingTenderManagement.Checks;

// No SQL fixtures or persisted votes. Both real command services share one isolated memory transaction
// store for race tests. SQL checks below execute SELECTs only; Phase 3 checks the real SQL lock itself.
public static class TopCommitteePhase4Checks
{
    public static async Task RunAsync(IServiceProvider services)
    {
        var passed = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); passed++; }
        async Task Denied<T>(Func<Task> action, string message) where T : Exception
        { try { await action(); } catch (T) { passed++; return; } throw new InvalidOperationException(message); }
        static CommitteeVoteInput Input(string decision = "Approved", string? notes = null, int tenderId = 1) => new()
        { TenderId = tenderId, ExpectedNominatedBidId = 10, Decision = decision, Notes = notes, Confirmed = true };
        static TopCommitteeVotingService Voting(MemoryStore data) => new(data);
        static async Task Approve(MemoryStore data, params int[] positions)
        { foreach (var position in positions) await Voting(data).SubmitAsync(Actor(position), Input()); }
        static CommitteeTenderPage Page(MemoryStore data, int member = 1, bool databaseReady = true) =>
            new(TopCommitteeQueryService.MapDetails(data.Tender, data.Members, data.Managers, [],
                TopCommitteeClarificationScope.OpenCount(data.Access($"member-{member}")), $"member-{member}"), null, Input(), databaseReady);

        var data = new MemoryStore(); var service = Voting(data);
        await service.SubmitAsync(Actor(1), Input(notes: "  Reviewed offer.  "));
        var first = data.Votes.Single();
        Check(first.Stage == "Top" && first.Decision == "Approved" && first.UserId == "member-1" && first.CommitteeAssignmentId == 1 &&
            first.TenderId == 1 && first.Notes == "Reviewed offer." && first.DecidedAt != default, "Approval must use authenticated member and trusted assignment with timestamp/notes.");
        Check(data.Tender.Status == "AwaitingCommitteeDecision"
            && data.PauseChecks == 1, "One approval must not award and must pass the locked pause gate.");
        await Denied<CommitteeStateConflictException>(() => service.SubmitAsync(Actor(1), Input()), "Duplicate approval must fail.");
        await Denied<CommitteeStateConflictException>(() => service.SubmitAsync(Actor(1), Input("Rejected", "Changed mind")), "An approval must not be changed into rejection.");
        Check(data.Votes.Single() == first && first.Decision == "Approved", "Original vote must remain unchanged.");
        await Denied<CommitteeAccessDeniedException>(() => service.SubmitAsync(new ClaimsPrincipal(), Input()), "Anonymous vote must fail.");
        await Denied<CommitteeAccessDeniedException>(() => service.SubmitAsync(Actor(1, RoleNames.Supplier), Input()), "Wrong claim role must fail.");
        await Denied<CommitteeTenderNotFoundException>(() => service.SubmitAsync(Actor(9), Input()), "Unassigned member must fail.");
        await Denied<CommitteeTenderNotFoundException>(() => service.SubmitAsync(Actor(2), Input(tenderId: 999)), "Cross-tender vote must fail.");
        data.Active["member-2"] = false;
        await Denied<CommitteeAccessDeniedException>(() => service.SubmitAsync(Actor(2), Input()), "Disabled account must fail under lock.");
        data.Active["member-2"] = true; data.Roles["member-2"] = [];
        await Denied<CommitteeAccessDeniedException>(() => service.SubmitAsync(Actor(2), Input()), "Revoked database role must fail despite cookie.");
        data.Roles["member-2"] = [RoleNames.TopCommittee];
        foreach (var notes in new string?[] { null, "", "   ", new string('R', 4001) })
            await Denied<ValidationException>(() => service.SubmitAsync(Actor(2), Input("Rejected", notes)), "Missing/blank/oversized rejection must fail.");
        await Denied<ValidationException>(() => service.SubmitAsync(Actor(2), Input("ReturnedForReview", "Invalid")), "Unsupported decisions cannot be repurposed.");
        var unconfirmed = Input(); unconfirmed.Confirmed = false;
        await Denied<ValidationException>(() => service.SubmitAsync(Actor(2), unconfirmed), "Final vote confirmation is required.");
        var stale = Input(); stale.ExpectedNominatedBidId = 99;
        await Denied<CommitteeStateConflictException>(() => service.SubmitAsync(Actor(2), stale), "Stale/client nomination must never select the awarded bid.");
        foreach (var status in new[] { "Draft", "Open", "UnderEvaluation", "AwaitingManagerReview", "Awarded", "Cancelled" })
        {
            var invalid = new MemoryStore(); invalid.Tender.Status = status;
            await Denied<CommitteeStateConflictException>(() => Voting(invalid).SubmitAsync(Actor(1), Input()), $"{status} must block voting.");
        }
        foreach (var mutate in new Action<MemoryStore>[] {
            d => d.Members.RemoveAt(3), d => d.Members[3] = d.Members[3] with { IsActive = false },
            d => d.Members[3] = d.Members[3] with { HasCommitteeRole = false },
            d => d.Members[3] = d.Members[3] with { Position = 1 },
            d => d.Members[3] = d.Members[3] with { UserId = "member-1" },
            d => d.Nomination.TenderId = 999, d => d.Nomination.SubmittedAt = null,
            d => d.Nomination.Status = "Withdrawn", d => d.Nomination.ComplianceStatus = "NonCompliant",
            d => d.Tender.EvaluationReport!.RecommendedBidId = null,
            d => d.Tender.EvaluationReport!.ManagerSummary = " ", d => d.Managers.Clear(),
            d => d.Managers.Add(d.Managers[0] with { UserId = "another-manager" }),
            d => d.Managers[0] = d.Managers[0] with { IsActive = false },
            d => d.Managers[0] = d.Managers[0] with { HasManagerRole = false },
            d => d.Tender.AwardedBidId = 10, d => d.Tender.AwardedAt = DateTime.UtcNow })
        {
            var invalid = new MemoryStore(); mutate(invalid);
            await Denied<CommitteeStateConflictException>(() => Voting(invalid).SubmitAsync(Actor(1), Input()), "Invalid referral/roster/nomination/award state must fail.");
            Check(!invalid.Votes.Any(), "A rejected precondition cannot leave a vote behind.");
        }
        var history = new MemoryStore(); await Approve(history, 1);
        history.Votes.Single().CommitteeAssignmentId = 4;
        await Denied<CommitteeStateConflictException>(() => Voting(history).SubmitAsync(Actor(2), Input()), "Malformed historical assignment cannot count towards award.");
        Check(history.Votes.Count() == 1, "Invalid history must be preserved for explicit repair, not overwritten.");
        foreach (var status in new[] { "Pending", "Answered" })
        {
            var paused = new MemoryStore(); await Approve(paused, 1); paused.Question(status);
            await Denied<CommitteeStateConflictException>(() => Voting(paused).SubmitAsync(Actor(3), Input()), "Open clarification must block every member's approval.");
            await Denied<CommitteeStateConflictException>(() => Voting(paused).SubmitAsync(Actor(3), Input("Rejected", "Reason")), "Paused voting also blocks rejection.");
            Check(paused.Votes.Count() == 1, "Pause cannot remove an earlier approval.");
            paused.Tender.Clarifications.Single().Status = "Resolved";
            await Voting(paused).SubmitAsync(Actor(3), Input());
            Check(paused.Votes.Count() == 2, "Voting resumes after all relevant questions are resolved.");
        }
        foreach (var decision in new[] { "Approved" })
        {
            var blocked = new MemoryStore { DatabaseReady = false };
            await Denied<CommitteeStateConflictException>(() => Voting(blocked).SubmitAsync(Actor(1), Input(decision, "Reason")), "Both decisions must fail closed before the constraint is ready.");
            Check(!blocked.Votes.Any(), "Schema readiness denial must not persist a vote.");
        }

        var awardData = new MemoryStore(); await Approve(awardData, 1, 2, 3);
        Check(awardData.Tender.Status == "AwaitingCommitteeDecision"
            && awardData.Tender.AwardedAt == null, "Three approvals cannot award.");
        var award = await Voting(awardData).SubmitAsync(Actor(4), Input());
        Check(award.IsAwarded && awardData.Votes.Count() == 4 && awardData.Tender.Status == "Awarded" && awardData.Tender.AwardedBidId == 10 &&
            awardData.Tender.AwardedAt == awardData.Votes.Last().DecidedAt && !string.IsNullOrWhiteSpace(awardData.Tender.AwardNotes), "Fourth approval and nominated-bid award must be atomic.");
        await Denied<CommitteeStateConflictException>(() => Voting(awardData).SubmitAsync(Actor(4), Input()), "Award cannot be repeated.");
        var rejected = new MemoryStore(); await Approve(rejected, 1, 2);
        await Voting(rejected).SubmitAsync(Actor(3), Input("Rejected", "Specification mismatch."));
        Check(rejected.Tender.Status == "AwaitingManagerReview" && rejected.Votes.Count() == 3 && rejected.Tender.AwardedBidId == null,
            "Rejection returns the tender and preserves previous approvals.");

        var requestEdit = new MemoryStore(); await Approve(requestEdit, 1, 2);
        await Denied<ValidationException>(() => Voting(requestEdit).SubmitAsync(Actor(3), Input("RequestEdit", "  ")), "RequestEdit must require notes.");
        var editRes = await Voting(requestEdit).SubmitAsync(Actor(3), Input("RequestEdit", "Please verify cold-chain SLA or check second offer."));
        Check(editRes.Decision == "RequestEdit" && !editRes.IsAwarded && requestEdit.Tender.Status == "AwaitingManagerReview" &&
            requestEdit.Tender.TopEditState == "WithManager"
                && requestEdit.Tender.TopEditRequestedByUserId == "member-3"
                &&
            requestEdit.Tender.TopEditRequestNotes == "Please verify cold-chain SLA or check second offer." &&
            !requestEdit.Votes.Any(), "RequestEdit returns tender to Manager with TopEditState=WithManager and clears prior round " +
                    "approvals so voting can reopen.");

        async Task<CommitteeVoteResult?> TryVote(MemoryStore d, int member, string decision = "Approved")
        { try { return await Voting(d).SubmitAsync(Actor(member), Input(decision, "Reason")); } catch (Exception ex) when (ex is CommitteeStateConflictException or ValidationException) { return null; } }
        var duplicate = new MemoryStore();
        var duplicateResults = await Task.WhenAll(TryVote(duplicate, 1), TryVote(duplicate, 1));
        Check(duplicateResults.Count(r => r != null) == 1
            && duplicate.Votes
            .Count() == 1, "Concurrent duplicate votes must have one winner.");
        var finalRace = new MemoryStore(); await Approve(finalRace, 1, 2, 3);
        var finalResults = await Task.WhenAll(TryVote(finalRace, 4), TryVote(finalRace, 4));
        Check(finalResults.Count(r => r?.IsAwarded == true) == 1
            && finalRace.Votes
            .Count() == 4, "Concurrent fourth approvals cannot create duplicate awards.");
        var lastTwo = new MemoryStore(); await Approve(lastTwo, 1, 2);
        var lastTwoResults = await Task.WhenAll(TryVote(lastTwo, 3), TryVote(lastTwo, 4));
        Check(lastTwoResults.All(r => r != null) && lastTwoResults.Count(r => r!.IsAwarded) == 1 && lastTwo.Votes.Count() == 4,
            "Two remaining members must serialize with one final award.");
        foreach (var rejectionFirst in new[] { false, true })
        {
            var competing = new MemoryStore(); await Approve(competing, 1, 2, 3);
            var results = await Task.WhenAll(TryVote(competing,
                4,
                rejectionFirst ? "Rejected" : "Approved"), TryVote(competing,
                4,
                rejectionFirst ? "Approved" : "Rejected"));
            Check(results.Count(r => r != null) == 1 && competing.Votes.Count() == 4 &&
                competing.Tender.Status == (rejectionFirst ? "AwaitingManagerReview" : "Awarded"), "Concurrent opposite final decisions must not overwrite one another.");
        }
        foreach (var clarificationFirst in new[] { true, false })
        {
            var racing = new MemoryStore(); await Approve(racing, 1, 2, 3);
            async Task<bool> Question()
            {
                try { await new TopCommitteeClarificationService(racing).CreateAsync(Actor(4), new() { TenderId = 1,
                    Subject = "Check delivery",
                    Description = "Please confirm." }); return true; }
                catch (CommitteeStateConflictException) { return false; }
            }
            Task<bool> questionTask; Task<CommitteeVoteResult?> voteTask;
            if (clarificationFirst) { questionTask = Question(); voteTask = TryVote(racing, 4); }
            else { voteTask = TryVote(racing, 4); questionTask = Question(); }
            await Task.WhenAll(questionTask, voteTask);
            Check(questionTask.Result != (voteTask.Result != null) &&
                (clarificationFirst ? racing.Tender.Status == "AwaitingCommitteeDecision"
                    && racing.Votes.Count() == 3 : racing.Tender.Status == "Awarded"),
                "Actual clarification and voting commands sharing a lock cannot both win the final-state race.");
        }
        foreach (var failAfterSave in new[] { false, true })
        {
            var failing = new MemoryStore(); await Approve(failing, 1, 2, 3); failing.FailAfterSave = failAfterSave; failing.FailSave = !failAfterSave;
            await Denied<IOException>(() => Voting(failing).SubmitAsync(Actor(4), Input()), "Failure before transaction commit must propagate.");
            Check(failing.Votes.Count() == 3 && failing.Tender.Status == "AwaitingCommitteeDecision" && failing.Tender.AwardedBidId == null &&
                failing.Tender.AwardedAt == null && failing.Tender.AwardNotes == null, "Failed final vote/award must roll back all changes together.");
            failing.FailSave = false; failing.FailAfterSave = false;
            Check((await Voting(failing).SubmitAsync(Actor(4), Input())).IsAwarded, "Rollback must release the lock for a legitimate retry.");
        }

        var eligiblePage = Page(new MemoryStore());
        var html = await TopCommitteePhase2Checks.RenderAsync(services, Actor(1), "TenderDetails", eligiblePage);
        Check(html.Contains("tc-member-approval-form") && html.Contains("name=\"Vote.Confirmed\"") && html.Contains("__RequestVerificationToken") &&
            html.Contains("value=\"Approved\"") && html.Contains("value=\"RequestEdit\"") && html.Contains("value=\"Rejected\""), "Voting form, edit/reject modals, choices and CSRF token must render.");
        Check(html.Contains("name=\"Vote.ExpectedNominatedBidId\"")
            && !html
            .Contains("name=\"UserId\"")
            && !html
            .Contains("name=\"CommitteeAssignmentId\""), "Browser cannot select actor/assignment.");
        var pausedUi = new MemoryStore(); pausedUi.Question("Pending");
        var pausedHtml = await TopCommitteePhase2Checks.RenderAsync(services, Actor(1), "TenderDetails", Page(pausedUi));
        Check(pausedHtml.Contains("Voting paused")
            && !pausedHtml
            .Contains("tc-member-approval-form"), "Paused UI cannot offer submission.");
        var votedHtml = await TopCommitteePhase2Checks.RenderAsync(services, Actor(1), "TenderDetails", Page(data));
        Check(votedHtml.Contains("decision is recorded and cannot be changed")
            && votedHtml
            .Contains("Reviewed offer.")
            && !votedHtml
            .Contains("tc-member-approval-form"), "Recorded votes/notes are visible and immutable.");
        var awardedHtml = await TopCommitteePhase2Checks.RenderAsync(services, Actor(1), "TenderDetails", Page(awardData));
        Check(awardedHtml.Contains("4 / 4 recorded")
            && awardedHtml
            .Contains("Winning bid")
            && !awardedHtml
            .Contains("tc-member-approval-form"), "Final award must be read-only.");
        var schemaHtml = await TopCommitteePhase2Checks.RenderAsync(services, Actor(1), "TenderDetails", eligiblePage with { VotingDatabaseReady = false });
        Check(schemaHtml.Contains("Voting is temporarily unavailable")
            && !schemaHtml
            .Contains("tc-member-approval-form"), "Pending database application must fail closed in UI.");
        var validation = new ModelStateDictionary(); validation.AddModelError("Vote.Notes", "A rejection justification is required.");
        var errorHtml = await TopCommitteePhase2Checks.RenderAsync(services,
            Actor(1),
            "TenderDetails",
            eligiblePage with { Vote = Input("Rejected") },
            validation);
        Check(errorHtml.Contains("A rejection justification is required.")
            && errorHtml
            .Contains("field-validation-error"), "Server-side vote validation must render.");
        var action = typeof(TopCommitteeController).GetMethod(nameof(TopCommitteeController.SubmitVote))!;
        Check(action.GetCustomAttribute<HttpPostAttribute>() != null
            && action
            .GetCustomAttribute<ValidateAntiForgeryTokenAttribute>() != null, "Vote action must require POST and anti-forgery.");

        TopCommitteeController Controller(MemoryStore state, ClaimsPrincipal actor)
        {
            var read = new ReadModels(state);
            return new TopCommitteeController(read,
                new TopCommitteeClarificationService(state),
                services.GetRequiredService<ITopCommitteeClarificationQueries>(),
                Voting(state),
                read)
            { ControllerContext = new ControllerContext(TopCommitteePhase2Checks.Context(services, actor, "SubmitVote")) };
        }
        var mvc = new MemoryStore();
        var redirect = await Controller(mvc, Actor(1)).SubmitVote(Input(), default) as RedirectToActionResult;
        Check(redirect?.ActionName == "TenderDetails"
            && (int?)redirect.RouteValues?["id"] == 1, "Successful POST must redirect using trusted tender ID.");
        Check(await Controller(mvc, Actor(9)).SubmitVote(Input(), default) is NotFoundResult, "MVC must conceal inaccessible tenders.");
        Check(await Controller(mvc, Actor(1, RoleNames.Supplier)).SubmitVote(Input(), default) is ForbidResult, "MVC must map role failure to forbidden.");
        var invalidController = Controller(new MemoryStore(), Actor(1));
        var invalidResult = await invalidController.SubmitVote(Input("Rejected"), default);
        Check(invalidResult is ViewResult
            && invalidController.Response.StatusCode == 400
            && !invalidController.ModelState.IsValid, "Invalid rejection must return useful 400 validation.");

        var migration = new AllowTopCommitteeRejection();
        Check(migration.UpOperations.Count == 2 && migration.UpOperations[0] is DropCheckConstraintOperation { Name: "CK_TenderApprovals_3", Table: "TenderApprovals" } &&
            migration.UpOperations[1] is AddCheckConstraintOperation { Name: "CK_TenderApprovals_3", Table: "TenderApprovals", Sql: TopCommitteeVotingDatabase.NewConstraint }, "Up must contain only the authorized constraint replacement.");
        Check(migration.DownOperations.Count == 2 && migration.DownOperations[0] is DropCheckConstraintOperation { Name: "CK_TenderApprovals_3", Table: "TenderApprovals" } &&
            migration.DownOperations[1] is AddCheckConstraintOperation { Name: "CK_TenderApprovals_3", Table: "TenderApprovals", Sql: TopCommitteeVotingDatabase.OldConstraint }, "Down must restore exactly the original constraint, without deleting history.");
        Check(TopCommitteeVotingDatabase.AcceptsDefinition(TopCommitteeVotingDatabase.NewConstraint) &&
            TopCommitteeVotingDatabase.AcceptsDefinition("([Stage]='Manager' OR [Decision]='Approved' OR [Stage]='Top' AND [Decision]='Rejected')")
                &&
            TopCommitteeVotingDatabase.AcceptsDefinition(TopCommitteeVotingDatabase.OldConstraint) &&
            !TopCommitteeVotingDatabase.AcceptsDefinition("(([Stage]='Manager' OR [Decision]='Approved' OR [Stage]='Top') AND [Decision]='Rejected')"), "Schema gate must recognize SQL normalization without accepting changed boolean grouping.");
        var db = services.GetRequiredService<ApplicationDbContext>();
        var cases = await db.Database.SqlQueryRaw<ConstraintCase>($"SELECT [Stage], [Decision], CAST(CASE WHEN {TopCommitteeVotingDatabase.OldConstraint} THEN 1 ELSE 0 END AS bit) AS OldAccepted, CAST(CASE WHEN {TopCommitteeVotingDatabase.NewConstraint} THEN 1 ELSE 0 END AS bit) AS NewAccepted FROM (VALUES ('Manager'), ('Evaluation'), ('Top')) AS stages([Stage]) CROSS JOIN (VALUES ('Approved'), ('Rejected'), ('ReturnedForReview')) AS decisions([Decision])")
            .ToListAsync();
        Check(cases.Count == 9
            && cases.All(c => c.NewAccepted == (c.Stage == "Manager" || c.Decision == "Approved" || c.Stage == "Top" && c.Decision == "Rejected")), "SQL truth table must preserve other-stage rules.");
        Check(cases.Where(c => c.NewAccepted != c.OldAccepted)
            .Single() is { Stage: "Top", Decision: "Rejected", NewAccepted: true }, "Only Top rejection may change acceptance.");
        var liveReady = await TopCommitteeVotingDatabase.IsReadyAsync(db, default);
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        Console.WriteLine($"NOTE: Live SQL voting readiness = {liveReady}; pending Phase 4 migration = {pending.Contains("20261113064343_AllowTopCommitteeRejection")}. " +
                $"No migration applied by checks.");
        await Denied<CommitteeAccessDeniedException>(() => services.GetRequiredService<ITopCommitteeVotingReadiness>()
            .IsDatabaseReadyAsync(new ClaimsPrincipal(), 1), "Readiness must not disclose tender access to anonymous callers.");
        Check(!db.ChangeTracker.HasChanges(), "Phase 4 checks must leave the SQL context unchanged.");
        var output = Environment.GetEnvironmentVariable("TOP_COMMITTEE_PREVIEW_DIR");
        if (!string.IsNullOrWhiteSpace(output))
        {
            // Phase 2 validates the absolute outside-repository destination and copies the shared assets.
            foreach (var (name, content) in new[] { ("voting.html", html), ("voting-paused.html", pausedHtml), ("voting-recorded.html", votedHtml),
                ("voting-awarded.html", awardedHtml), ("voting-unavailable.html", schemaHtml), ("voting-error.html", errorHtml) })
                await File.WriteAllTextAsync(Path.Combine(output, name), content);
            Directory.CreateDirectory(Path.Combine(output, "js"));
            File.Copy(Path.Combine(services.GetRequiredService<IWebHostEnvironment>().WebRootPath, "js", "top-committee-voting.js"), Path
                .Combine(output, "js", "top-committee-voting.js"), true);
        }
        Console.WriteLine($"PASS: {passed} Phase 4 voting, rejection, award, concurrency, rollback, MVC/Razor and constraint " +
                $"checks. Mutation tests are isolated in memory; SQL checks are SELECT-only, not SQL persistence tests.");
    }

    private static ClaimsPrincipal Actor(int member, string role = RoleNames.TopCommittee) => new(new ClaimsIdentity(new[] {
        new Claim(ClaimTypes.NameIdentifier, $"member-{member}"), new Claim(ClaimTypes.Role, role) }, "Phase4Checks"));
    private sealed class ConstraintCase
    { public string Stage { get; set; } = ""; public string Decision { get; set; } = ""; public bool OldAccepted { get; set; } public bool NewAccepted { get; set; } }

    private sealed class ReadModels(MemoryStore data) : ITopCommitteeQueries, ITopCommitteeVotingReadiness
    {
        public Task<bool> IsDatabaseReadyAsync(ClaimsPrincipal member,
            int tenderId,
            CancellationToken cancellationToken = default) => Task.FromResult(data.DatabaseReady);
        public Task<CommitteeTenderDetails> GetTenderDetailsAsync(ClaimsPrincipal actor, int tenderId, CancellationToken cancellationToken = default)
        {
            if (tenderId != 1) throw new CommitteeTenderNotFoundException();
            var id = actor.FindFirstValue(ClaimTypes.NameIdentifier)!;
            return Task.FromResult(TopCommitteeQueryService.MapDetails(data.Tender,
                data.Members,
                data.Managers,
                [],
                TopCommitteeClarificationScope.OpenCount(data.Access(id)),
                id));
        }
        public Task<CommitteeDashboard> GetDashboardAsync(ClaimsPrincipal actor, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Not used by Phase 4 checks.");
        public Task<CommitteePage<CommitteeTenderRow>> GetTendersAsync(ClaimsPrincipal actor,
            CommitteeTenderFilter filter,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("Not used by Phase 4 checks.");
    }

    private sealed class MemoryStore : ITopCommitteeVotingStore, ITopCommitteeClarificationStore
    {
        private readonly SemaphoreSlim mutex = new(1, 1);
        public Tender Tender { get; } = new() { TenderId = 1, Title = "Test equipment tender", Description = "In-memory voting test", Status = "AwaitingCommitteeDecision",
            OpensAt = DateTime.UtcNow.AddDays(-3), ClosesAt = DateTime.UtcNow.AddDays(-1),
            PurchaseRequest = new PurchaseRequest { PurchaseRequestId = 1,
                Title = "Test equipment",
                Reason = "Test request",
                Priority = "Normal",
                TargetDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)),
                Department = new Department { Name = "Test department" } },
            EvaluationReport = new EvaluationReport { EvaluationReportId = 1, TenderId = 1, Status = "ManagerApproved", RecommendedBidId = 10, ManagerSummary = "Test Manager recommendation" } };
        public Bid Nomination { get; } = new() { BidId = 10,
            TenderId = 1,
            Status = "Compliant",
            ComplianceStatus = "Compliant",
            SubmittedAt = DateTime.UtcNow,
            Currency = "USD" };
        public List<CommitteeMember> Members { get; } = Enumerable.Range(1, 4)
            .Select(i => new CommitteeMember(i, $"member-{i}", $"Test member {i}", i, true, true))
            .ToList();
        public List<CommitteeManager> Managers { get; } = [new("manager", "Test manager", true, true, DateTime.UtcNow)];
        public Dictionary<string, bool> Active { get; } = new();
        public Dictionary<string, IReadOnlyList<string>> Roles { get; } = new();
        public bool DatabaseReady { get; set; } = true;
        public bool FailSave { get; set; }
        public bool FailAfterSave { get; set; }
        public int PauseChecks { get; private set; }
        public IEnumerable<TenderApproval> Votes => Tender.TenderApprovals.Where(v => v.Stage == "Top");
        public MemoryStore()
        {
            foreach (var member in Enumerable.Range(1, 9)) { Active[$"member-{member}"] = true; Roles[$"member-{member}"] = [RoleNames.TopCommittee]; }
            var invitation = new Invitation { SupplierProfile = new SupplierProfile { CompanyName = "Test nominated supplier" } };
            invitation.Bids.Add(Nomination); Tender.Invitations.Add(invitation);
            Tender.TenderApprovals.Add(new TenderApproval { TenderId = 1,
                Stage = "Manager",
                Decision = "Approved",
                UserId = "manager",
                Notes = "Existing Manager approval" });
        }
        public CommitteeClarificationContext Access(string actor, int tenderId = 1) => new(tenderId == 1 ? Tender : null,
            Active.GetValueOrDefault(actor), Roles.GetValueOrDefault(actor) ?? [], tenderId == 1 ? Members : [], tenderId == 1 ? Managers : []);
        public void Question(string status) => Tender.Clarifications.Add(new Clarification { ClarificationId = 1,
            TenderId = 1,
            SenderUserId = "member-2",
            RecipientUserId = "manager",
            Status = status });
        Task<T> ITopCommitteeVotingStore.ExecuteAsync<T>(string memberId,
            int tenderId,
            Func<ICommitteeVotingSession, CancellationToken, Task<T>> action,
            CancellationToken ct) =>
            Locked(memberId, tenderId, (session, token) => action(session, token), ct);
        Task<T> ITopCommitteeClarificationStore.ExecuteAsync<T>(string actorId,
            int tenderId,
            Func<ICommitteeClarificationSession, CancellationToken, Task<T>> action,
            CancellationToken ct) =>
            Locked(actorId, tenderId, (session, token) => action(session, token), ct);
        public Task<int?> FindTenderAsync(string actorId, string role, int clarificationId, CancellationToken ct) =>
            Task.FromResult(Tender.Clarifications.SingleOrDefault(c => c.ClarificationId == clarificationId
                && c.SenderUserId == actorId)?.TenderId);
        private async Task<T> Locked<T>(string actor, int tenderId, Func<Session, CancellationToken, Task<T>> action, CancellationToken ct)
        {
            await mutex.WaitAsync(ct);
            var status = Tender.Status; var awardId = Tender.AwardedBidId; var awardAt = Tender.AwardedAt; var awardNotes = Tender.AwardNotes;
            var votes = Tender.TenderApprovals.ToList(); var questions = Tender.Clarifications.ToList();
            try { await Task.Yield(); var result = await action(new Session(this, Access(actor, tenderId)), ct); if (FailAfterSave) throw new IOException("Simulated commit failure."); return result; }
            catch
            {
                Tender.Status = status; Tender.AwardedBidId = awardId; Tender.AwardedAt = awardAt; Tender.AwardNotes = awardNotes;
                Tender.TenderApprovals.Clear(); foreach (var vote in votes) Tender.TenderApprovals.Add(vote);
                Tender.Clarifications.Clear(); foreach (var question in questions) Tender.Clarifications.Add(question);
                throw;
            }
            finally { mutex.Release(); }
        }
        private sealed class Session(MemoryStore data, CommitteeClarificationContext access) : ICommitteeVotingSession, ICommitteeClarificationSession
        {
            CommitteeVotingContext ICommitteeVotingSession.Context => new(access, data.Nomination);
            CommitteeClarificationContext ICommitteeClarificationSession.Context => access;
            public Task AssertNotPausedAsync(CancellationToken ct)
            { data.PauseChecks++; if (TopCommitteeClarificationScope.OpenCount(access) > 0) throw new CommitteeStateConflictException("Paused"); return Task.CompletedTask; }
            public Task<bool> IsDatabaseReadyAsync(CancellationToken ct) => Task.FromResult(data.DatabaseReady);
            public void Add(TenderApproval vote) { vote.TenderApprovalId = data.Tender.TenderApprovals.Count + 1; vote.User = new ApplicationUser { FullName = data.Members.Single(m => m.UserId == vote.UserId).FullName }; data.Tender.TenderApprovals.Add(vote); }
            public void ClearTopVotes() { foreach (var v in data.Tender.TenderApprovals.Where(v => v.Stage == "Top" && v.Decision != "Rejected")
                .ToList()) data.Tender.TenderApprovals.Remove(v); }
            public void Add(Clarification question) { question.ClarificationId = data.Tender.Clarifications.Count + 1; data.Tender.Clarifications.Add(question); }
            public void Add(ClarificationResponse response) => throw new InvalidOperationException("Not used by Phase 4 checks.");
            public async Task SaveAsync(CancellationToken ct) { if (data.FailSave) throw new IOException("Simulated persistence failure."); ct.ThrowIfCancellationRequested(); await Task
                .Yield(); }
        }
    }
}
