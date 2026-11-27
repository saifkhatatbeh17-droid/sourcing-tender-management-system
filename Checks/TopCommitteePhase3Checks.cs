using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Contracts.TopCommittee;
using SourcingTenderManagement.Controllers;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Models.TopCommittee;
using SourcingTenderManagement.Services.TopCommittee;

namespace SourcingTenderManagement.Checks;

// All mutation scenarios run against an in-memory session store, not SQL rows. The SQL concurrency
// check below acquires transaction locks on an absent tender ID; its callbacks never save any data.
public static class TopCommitteePhase3Checks
{
    public static async Task RunAsync(IServiceProvider services)
    {
        var passed = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); passed++; }
        async Task Denied<T>(Func<Task> action, string message) where T : Exception
        {
            try { await action(); } catch (T) { passed++; return; }
            throw new InvalidOperationException(message);
        }
        static CommitteeCreateClarification Input(string subject = "Delivery clarification", int tenderId = 1) =>
            new() { TenderId = tenderId, Subject = subject, Description = "Please confirm the delivery timeframe." };
        static TopCommitteeClarificationService Service(MemoryStore store) => new(store);
        var member = Actor("member-1", RoleNames.TopCommittee);
        var manager = Actor("manager", RoleNames.SourcingManager);
        var data = new MemoryStore(); var service = Service(data);
        data.Tender.TenderApprovals.Add(new TenderApproval { Stage = "Top", Decision = "Approved", UserId = "member-2" });
        var created = await service.CreateAsync(member, Input());
        var question = data.Question(created.ClarificationId);
        Check(question.Status == "Pending" && question.TenderId == 1 && question.PurchaseRequestId == null && question.BidId == null &&
            question.SenderUserId == "member-1" && question.RecipientUserId == "manager", "Create must use trusted sender/manager and the tender association.");
        Check(TopCommitteeClarificationScope.OpenCount(data.Context("member-1")) == 1, "Pending clarification must pause all members.");
        await Denied<CommitteeStateConflictException>(() => service.CreateAsync(member, Input()), "An exact repeated submission must not insert another question.");
        Check(data.Tender.Clarifications.Count == 1, "Duplicate creation must leave one question.");
        await Denied<CommitteeAccessDeniedException>(() => service.CreateAsync(new ClaimsPrincipal(new ClaimsIdentity()), Input()), "Anonymous creation must fail.");
        await Denied<CommitteeAccessDeniedException>(() => service.CreateAsync(Actor("member-1", RoleNames.Supplier), Input()), "Wrong role must fail.");
        await Denied<CommitteeTenderNotFoundException>(() => service.CreateAsync(Actor("outsider", RoleNames.TopCommittee), Input()), "Unassigned member cannot create.");
        await Denied<CommitteeTenderNotFoundException>(() => service.CreateAsync(member, Input(tenderId: 999)), "Cross-tender creation must fail.");
        await Denied<CommitteeStateConflictException>(() => service.CreateAsync(Actor("member-2", RoleNames.TopCommittee), Input("After vote")), "An existing vote must prevent creation.");
        data.Active["member-1"] = false;
        await Denied<CommitteeAccessDeniedException>(() => service.CreateAsync(member, Input("Disabled")), "Disabled account must fail under the transaction.");
        data.Active["member-1"] = true; data.Roles["member-1"] = [];
        await Denied<CommitteeAccessDeniedException>(() => service.CreateAsync(member, Input("Revoked")), "A stale cookie cannot replace the database role.");
        data.Roles["member-1"] = [RoleNames.TopCommittee];
        await Denied<ValidationException>(() => service.CreateAsync(member, new() { TenderId = 1,
            Subject = " ",
            Description = " " }), "Blank question must fail.");
        foreach (var status in new[] { "Draft", "Open", "Closed", "AwaitingManagerReview", "Awarded", "Cancelled" })
        {
            data.Tender.Status = status;
            await Denied<CommitteeStateConflictException>(() => service.CreateAsync(member, Input("Wrong state")), $"{status} cannot accept a new committee question.");
        }
        data.Tender.Status = "AwaitingCommitteeDecision";
        data.Tender.AwardedBidId = 10;
        await Denied<CommitteeStateConflictException>(() => service.CreateAsync(member, Input("Awarded")), "Recorded award blocks creation even after a status reset.");
        data.Tender.AwardedBidId = null;
        var rejection = new TenderApproval { Stage = "Top", Decision = "Rejected", UserId = "member-4" };
        data.Tender.TenderApprovals.Add(rejection);
        await Denied<CommitteeStateConflictException>(() => service.CreateAsync(member, Input("Rejected")), "Recorded rejection is terminal.");
        data.Tender.TenderApprovals.Remove(rejection);
        data.Managers.Add(data.Managers[0] with { UserId = "other-manager" });
        await Denied<CommitteeStateConflictException>(() => service.CreateAsync(member, Input("Ambiguous manager")), "Never guess a manager from multiple approvals.");
        data.Managers.RemoveAt(1);
        var originalManager = data.Managers[0];
        data.Managers[0] = originalManager with { UserId = "member-1" };
        await Denied<CommitteeStateConflictException>(() => service.CreateAsync(member, Input("Self recipient")), "Dual-role users cannot violate the sender/recipient check constraint.");
        data.Managers[0] = originalManager;
        await Denied<CommitteeAccessDeniedException>(() => service.AnswerAsync(member,
            created.ClarificationId,
            "Answer"), "Member cannot impersonate Manager.");
        await Denied<CommitteeTenderNotFoundException>(() => service.AnswerAsync(Actor("other-manager", RoleNames.SourcingManager),
            created.ClarificationId,
            "Answer"), "Another manager cannot answer.");
        await Denied<ValidationException>(() => service.AnswerAsync(manager, created.ClarificationId, " "), "Blank response must fail.");
        data.Active["manager"] = false;
        await Denied<CommitteeAccessDeniedException>(() => service.AnswerAsync(manager,
            created.ClarificationId,
            "Inactive"), "Disabled Manager cannot answer.");
        data.Active["manager"] = true; data.Roles["manager"] = [];
        await Denied<CommitteeAccessDeniedException>(() => service.AnswerAsync(manager,
            created.ClarificationId,
            "Revoked"), "Revoked Manager role must be checked in the database context.");
        data.Roles["manager"] = [RoleNames.SourcingManager];
        question.TenderId = 999;
        await Denied<CommitteeTenderNotFoundException>(() => service.AnswerAsync(manager,
            created.ClarificationId,
            "Cross tender"), "A clarification from another tender must not be answered in this context.");
        question.TenderId = 1;
        await Denied<CommitteeStateConflictException>(() => service.ResolveAsync(member, created.ClarificationId), "Pending question cannot be resolved.");
        question.Status = "Answered";
        await Denied<CommitteeStateConflictException>(() => service.ResolveAsync(member, created.ClarificationId), "Answered status alone is not evidence of a manager response.");
        question.ClarificationResponses.Add(new ClarificationResponse { ClarificationId = created.ClarificationId,
            RespondedByUserId = "other-manager",
            Message = "Untrusted response" });
        await Denied<CommitteeStateConflictException>(() => service.ResolveAsync(member, created.ClarificationId), "A response from another Manager cannot authorize resolution.");
        question.ClarificationResponses.Clear();
        question.Status = "Pending";
        await service.AnswerAsync(manager, created.ClarificationId, "Seven working days.");
        Check(question.Status == "Answered"
            && question.ClarificationResponses.Single().RespondedByUserId == "manager", "Manager answer must be persisted as a response and set Answered.");
        Check(TopCommitteeClarificationScope.OpenCount(data.Context("member-3")) == 1, "Answered still pauses every member.");
        await Denied<CommitteeStateConflictException>(() => service.AnswerAsync(manager, created.ClarificationId, "Duplicate"), "Duplicate answer must fail.");
        await Denied<CommitteeTenderNotFoundException>(() => service.ResolveAsync(Actor("member-3", RoleNames.TopCommittee), created.ClarificationId), "Only the question creator may resolve.");
        await service.ConfirmResolutionAsync(manager, created.ClarificationId);
        await service.FollowUpAsync(member, created.ClarificationId, "Does that include transit?");
        Check(question.Status == "Pending" && question.ClarificationResponses.Count == 3 &&
            TopCommitteeClarificationScope.OpenCount(data.Context("member-1")) == 1,
            "A follow-up must preserve the first answer and keep voting paused.");
        await Denied<CommitteeStateConflictException>(() => service.ResolveAsync(member, created.ClarificationId), "Wait for the latest answer before resolving.");
        await service.AnswerAsync(manager, created.ClarificationId, "Yes, it includes transit.");
        Check(question.Status == "Answered"
            && question.ClarificationResponses.Count == 4, "The manager can answer again in the same conversation.");
        await service.ResolveAsync(member, created.ClarificationId);
        Check(question.Status == "Answered"
            && TopCommitteeClarificationScope.OpenCount(data.Context("member-1")) == 1, "One confirmation keeps voting paused.");
        await service.ConfirmResolutionAsync(manager, created.ClarificationId);
        Check(question.Status == "Resolved"
            && question.ResolvedAt.HasValue, "Resolve must preserve answer and record its timestamp.");
        await Denied<CommitteeStateConflictException>(() => service.ResolveAsync(member, created.ClarificationId), "Duplicate resolution must fail.");
        Check(TopCommitteeClarificationScope.OpenCount(data.Context("member-1")) == 0 && data.Tender.TenderApprovals.Count == 1,
            "Resume must preserve pre-existing approvals.");

        var multiple = new MemoryStore(); var multipleService = Service(multiple);
        var first = await multipleService.CreateAsync(member, Input("First question"));
        var secondActor = Actor("member-3", RoleNames.TopCommittee);
        var second = await multipleService.CreateAsync(secondActor, Input("Second question"));
        await multipleService.AnswerAsync(manager, first.ClarificationId, "First answer");
        await multipleService.ConfirmResolutionAsync(manager, first.ClarificationId);
        await multipleService.ResolveAsync(member, first.ClarificationId);
        Check(TopCommitteeClarificationScope.OpenCount(multiple.Context("member-4")) == 1, "Resolving one question cannot resume voting while another remains open.");
        await multipleService.AnswerAsync(manager, second.ClarificationId, "Second answer");
        await multipleService.ResolveAsync(secondActor, second.ClarificationId);
        await multipleService.ConfirmResolutionAsync(manager, second.ClarificationId);
        Check(TopCommitteeClarificationScope.OpenCount(multiple.Context("member-4")) == 0, "Voting may resume only when every relevant question is resolved.");
        multiple.Tender.Clarifications.Add(new Clarification { ClarificationId = 99, TenderId = 1,
            SenderUserId = "outsider", RecipientUserId = "manager", Status = "Pending" });
        Check(TopCommitteeClarificationScope.OpenCount(multiple.Context("member-1")) == 0, "Unrelated questions must not be repurposed into the committee workflow.");

        async Task<bool> Try(Func<Task> operation)
        {
            try { await operation(); return true; } catch (CommitteeStateConflictException) { return false; }
        }
        var race = new MemoryStore(); var raceService = Service(race);
        var creates = await Task.WhenAll(Try(() => raceService.CreateAsync(member, Input())), Try(() => raceService
            .CreateAsync(member, Input())));
        Check(creates.Count(v => v) == 1
            && race.Tender.Clarifications.Count == 1, "Concurrent duplicate creations must serialize.");
        var raceId = race.Tender.Clarifications.Single().ClarificationId;
        var answers = await Task.WhenAll(Try(() => raceService.AnswerAsync(manager, raceId, "Answer one")), Try(() => raceService
            .AnswerAsync(manager, raceId, "Answer two")));
        Check(answers.Count(v => v) == 1
            && race
            .Question(raceId).ClarificationResponses.Count == 1, "Concurrent manager replies must produce one answer.");
        var resolves = await Task.WhenAll(Try(() => raceService.ResolveAsync(member, raceId)), Try(() => raceService
            .ResolveAsync(member, raceId)));
        Check(resolves.Count(v => v) == 1, "Concurrent resolve requests must have one winner.");

        // Simulate a future eligibility check inside the SAME tender lock. This is a test callback,
        // not a voting service or endpoint, and never writes SQL votes.
        var gateRace = new MemoryStore(); var gateService = Service(gateRace);
        var createRace = Try(() => gateService.CreateAsync(member, Input()));
        var eligibilityRace = Try(() => gateRace.ExecuteAsync("member-1", 1, (session, ct) =>
        {
            if (TopCommitteeClarificationScope.OpenCount(session.Context) > 0) throw new CommitteeStateConflictException("Paused");
            session.Context.Tender!.TenderApprovals.Add(new TenderApproval { Stage = "Top", UserId = "member-1", Decision = "Approved" });
            return Task.FromResult(0);
        }, default));
        var gates = await Task.WhenAll(createRace, eligibilityRace);
        Check(gates.Count(v => v) == 1, "Creation and future voting eligibility must not both pass competing state checks.");

        foreach (var method in new[] { "CreateClarification", "FollowUpClarification", "ResolveClarification" })
        {
            var action = typeof(TopCommitteeController).GetMethod(method)!;
            Check(action.GetCustomAttribute<HttpPostAttribute>() != null && action.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>() != null,
                $"{method} must be POST and anti-forgery protected.");
        }
        var postNames = typeof(TopCommitteeController).GetMethods()
            .Where(m => m.GetCustomAttribute<HttpPostAttribute>() != null)
            .Select(m => m.Name);
        Check(postNames.All(n => n is "CreateClarification" or "FollowUpClarification" or "ResolveClarification" or "SubmitVote" or "AssessImportance" or "MarkNotificationRead" or "MarkAllNotificationsRead"), "Only approved Top Committee POSTs may be introduced.");

        var queries = services.GetRequiredService<ITopCommitteeClarificationQueries>();
        await Denied<CommitteeAccessDeniedException>(() => queries.GetPageAsync(new ClaimsPrincipal(), null), "Anonymous conversation query must fail.");
        var configured = services.GetRequiredService<ApplicationDbContext>();
        var liveId = await configured.Users.AsNoTracking().Where(u => u.IsActive && configured.UserRoles.Any(ur => ur.UserId == u.Id &&
            configured.Roles.Any(r => r.Id == ur.RoleId && r.Name == RoleNames.TopCommittee))).Select(u => u.Id).FirstAsync();
        var livePage = await queries.GetPageAsync(Actor(liveId, RoleNames.TopCommittee), null);
        await Denied<CommitteeTenderNotFoundException>(() => queries.GetPageAsync(Actor(liveId, RoleNames.TopCommittee), -1), "Cross-tender conversation reads must fail.");
        var emptyHtml = await TopCommitteePhase2Checks.RenderAsync(services, member, "Clarifications", livePage);
        Check(livePage.Tenders.Count > 0
            || emptyHtml.Contains("No tenders assigned yet"), "Live empty state must be useful.");
        var uiPage = PreviewPage();
        var html = await TopCommitteePhase2Checks.RenderAsync(services, member, "Clarifications", uiPage);
        Check(html.Contains("Pending")
            && html
            .Contains("Answered")
            && html
            .Contains("Resolved")
            && html
            .Contains("Follow-up question"), "Render all three states and the manager response.");
        Check(html.Contains("CreateClarification")
            && html
            .Contains("ResolveClarification")
            && html
            .Contains("__RequestVerificationToken"), "Forms must render anti-forgery tokens and the correct actions.");
        Check(html.Contains("name=\"Create.Subject\"")
            && html
            .Contains("name=\"Create.TenderId\"")
            && !html
            .Contains("name=\"ManagerId\""), "Form binding must never choose the responsible manager.");
        Check(!html.Contains("<script>alert(1)</script>")
            && html
            .Contains("&lt;script&gt;"), "Questions and answers must be encoded.");
        var blockedHtml = await TopCommitteePhase2Checks.RenderAsync(services,
            member,
            "Clarifications",
            uiPage with { CreateBlockedReason = "You have already voted." });
        Check(!blockedHtml.Contains("Send clarification</button>")
            && blockedHtml
            .Contains("already voted"), "Ineligible creation must have a meaningful read-only state.");
        var validation = new ModelStateDictionary();
        validation.AddModelError("Create.Subject", "Please enter a subject.");
        var errorHtml = await TopCommitteePhase2Checks.RenderAsync(services, member, "Clarifications", uiPage, validation);
        Check(errorHtml.Contains("Please enter a subject.")
            && errorHtml
            .Contains("field-validation-error"), "Validation errors must be visible beside the field and in the summary.");
        var longHtml = await TopCommitteePhase2Checks.RenderAsync(services, member, "Clarifications", uiPage with {
            Threads = new[] { uiPage.Threads[0] with { Subject = new string('S', 200), Description = new string('Q', 4000) } } });
        Check(longHtml.Contains(new string('Q', 4000)), "Maximum-length text must render without truncating the stored conversation.");

        var sqlTransaction = services.GetRequiredService<ITopCommitteeTenderTransaction>();
        var pause = services.GetRequiredService<ITopCommitteeVotingPause>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var lockId = int.MaxValue - 317;
        while (await configured.Tenders.AsNoTracking().AnyAsync(t => t.TenderId == lockId)) lockId--;
        var acquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstLock = sqlTransaction.ExecuteAsync(lockId, async (db, ct) =>
        {
            await pause.AssertNotPausedAsync(db, lockId, ct);
            acquired.SetResult();
            await release.Task.WaitAsync(ct);
            return 1;
        }, timeout.Token);
        try
        {
            await acquired.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var secondLock = sqlTransaction.ExecuteAsync(lockId, async (db, ct) =>
            {
                secondEntered.SetResult(); await pause.AssertNotPausedAsync(db, lockId, ct); return 2;
            }, timeout.Token);
            var early = await Task.WhenAny(secondEntered.Task, Task.Delay(250, timeout.Token));
            Check(early != secondEntered.Task, "Two independent SQL connections must not enter the same tender transaction concurrently.");
            release.TrySetResult();
            await Task.WhenAll(firstLock, secondLock);
            Check(secondEntered.Task.IsCompletedSuccessfully, "The SQL lock must be released at commit for the waiting operation.");
        }
        finally { release.TrySetResult(); await firstLock; }
        await Denied<CommitteeStateConflictException>(() => sqlTransaction.ExecuteAsync<int>(lockId, (_, _) =>
            throw new CommitteeStateConflictException("Test rollback without data writes")), "Failed callback must roll back its transaction.");
        var afterRollback = await sqlTransaction.ExecuteAsync(lockId, async (db, ct) =>
        {
            await pause.AssertNotPausedAsync(db, lockId, ct);
            await Denied<InvalidOperationException>(() => pause.AssertNotPausedAsync(db,
                lockId - 1,
                ct), "A lock for one tender must not authorize a pause check for another.");
            return !db.ChangeTracker.HasChanges();
        });
        Check(afterRollback, "SQL locks must be released after rollback with no tracked data changes.");
        await Denied<InvalidOperationException>(() => pause.AssertNotPausedAsync(configured, lockId), "Phase 4 gate must require the tender transaction lock.");
        Check(!configured.ChangeTracker.HasChanges(), "Checks must leave the database context unchanged.");

        var output = Environment.GetEnvironmentVariable("TOP_COMMITTEE_PREVIEW_DIR");
        if (!string.IsNullOrWhiteSpace(output))
        {
            // Phase 2 already validates this outside-repository preview directory.
            await File.WriteAllTextAsync(Path.Combine(output, "clarifications.html"), html);
            await File.WriteAllTextAsync(Path.Combine(output, "clarifications-empty.html"), emptyHtml);
            await File.WriteAllTextAsync(Path.Combine(output, "clarifications-blocked.html"), blockedHtml);
            await File.WriteAllTextAsync(Path.Combine(output, "clarifications-error.html"), errorHtml);
            await File.WriteAllTextAsync(Path.Combine(output, "clarifications-long.html"), longHtml);
        }
        Console.WriteLine($"PASS: {passed} Phase 3 clarification checks, in-memory command/race tests and real SQL lock serialization; no SQL fixture or production data writes.");
    }

    private static ClaimsPrincipal Actor(string id, string role) => new(new ClaimsIdentity(new[] {
        new Claim(ClaimTypes.NameIdentifier, id), new Claim(ClaimTypes.Role, role) }, "ClarificationChecks"));

    private static CommitteeClarificationsPage PreviewPage()
    {
        var now = DateTime.UtcNow;
        return new CommitteeClarificationsPage(new[] { new CommitteeClarificationTender(1, "Test equipment tender", "AwaitingCommitteeDecision") },
            new[] {
                new CommitteeClarificationThread(1,
                    1,
                    "Test equipment tender",
                    "Test delivery dates",
                    "Please confirm the lead time. <script>alert(1)</script>",
                    "Test manager",
                    "Pending",
                    now,
                    null,
                    false,
                    []),
                new CommitteeClarificationThread(2, 1, "Test equipment tender", "Test installation scope", "Does the quote include installation?", "Test manager", "Answered", now, null, true,
                    new[] { new CommitteeClarificationResponse("manager", "Test manager", "Installation is included in the submitted quote.", now) }),
                new CommitteeClarificationThread(3, 1, "Test equipment tender", "Test warranty", "Please confirm the warranty.", "Test manager", "Resolved", now, now, false,
                    new[] { new CommitteeClarificationResponse("manager", "Test manager", "The submitted warranty is 24 months.", now) }) },
            new CommitteeCreateClarification { TenderId = 1 }, "Test manager", null, 2);
    }

    private sealed class MemoryStore : ITopCommitteeClarificationStore
    {
        private readonly SemaphoreSlim mutex = new(1, 1);
        private int nextId;
        public Tender Tender { get; } = new() { TenderId = 1, Status = "AwaitingCommitteeDecision" };
        public List<CommitteeMember> Members { get; } = Enumerable.Range(1, 4)
            .Select(i => new CommitteeMember(i, $"member-{i}", $"Member {i}", i, true, true))
            .ToList();
        public List<CommitteeManager> Managers { get; } = [new("manager", "Manager", true, true, DateTime.UtcNow)];
        public Dictionary<string, bool> Active { get; } = new();
        public Dictionary<string, IReadOnlyList<string>> Roles { get; } = new();
        public MemoryStore()
        {
            foreach (var id in new[] { "member-1", "member-2", "member-3", "member-4", "outsider", "manager", "other-manager" })
            { Active[id] = true; Roles[id] = [id.Contains("manager", StringComparison.Ordinal) ? RoleNames.SourcingManager : RoleNames.TopCommittee]; }
        }
        public CommitteeClarificationContext Context(string actorId, int tenderId = 1) => new(tenderId == 1 ? Tender : null,
            Active.GetValueOrDefault(actorId), Roles.GetValueOrDefault(actorId) ?? [], tenderId == 1 ? Members : [], tenderId == 1 ? Managers : []);
        public Clarification Question(int id) => Tender.Clarifications.Single(c => c.ClarificationId == id);
        public Task<int?> FindTenderAsync(string actorId, string role, int clarificationId, CancellationToken cancellationToken) => Task.FromResult(
            Tender.Clarifications.SingleOrDefault(c => c.ClarificationId == clarificationId &&
                (role == RoleNames.TopCommittee ? c.SenderUserId == actorId : c.RecipientUserId == actorId))?.TenderId);
        public async Task<T> ExecuteAsync<T>(string actorId,
            int tenderId,
            Func<ICommitteeClarificationSession, CancellationToken, Task<T>> action,
            CancellationToken cancellationToken)
        {
            await mutex.WaitAsync(cancellationToken);
            try { return await action(new Session(this, Context(actorId, tenderId)), cancellationToken); }
            finally { mutex.Release(); }
        }
        private sealed class Session(MemoryStore owner, CommitteeClarificationContext context) : ICommitteeClarificationSession
        {
            public CommitteeClarificationContext Context => context;
            public void Add(Clarification question) { question.ClarificationId = ++owner.nextId; owner.Tender.Clarifications.Add(question); }
            public void Add(ClarificationResponse response) => owner.Question(response.ClarificationId).ClarificationResponses
                .Add(response);
            public async Task SaveAsync(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); await Task
                .Yield(); }
        }
    }
}
