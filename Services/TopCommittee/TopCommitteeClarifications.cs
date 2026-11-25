using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using SourcingTenderManagement.Contracts.TopCommittee;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Models.TopCommittee;
using Microsoft.EntityFrameworkCore;

namespace SourcingTenderManagement.Services.TopCommittee;

internal sealed class TopCommitteeClarificationService(ITopCommitteeClarificationStore store)
    : ITopCommitteeClarifications, ITopCommitteeManagerAnswers
{
    private static string Actor(ClaimsPrincipal principal, string role)
    {
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (principal.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(id) || !principal.IsInRole(role))
            throw new CommitteeAccessDeniedException();
        return id;
    }
    private static string Text(string? value, int maxLength, string label)
    {
        var text = (value ?? "").Replace("\r\n", "\n").Trim();
        if (text.Length == 0 || text.Length > maxLength) throw new ValidationException($"{label} must contain 1–{maxLength} characters.");
        return text;
    }

    public Task<CommitteeClarificationResult> CreateAsync(ClaimsPrincipal actor, CommitteeCreateClarification input,
        CancellationToken cancellationToken = default)
    {
        var memberId = Actor(actor, RoleNames.TopCommittee);
        ArgumentNullException.ThrowIfNull(input);
        Validator.ValidateObject(input, new ValidationContext(input), validateAllProperties: true);
        var subject = Text(input.Subject, 200, "Subject");
        var description = Text(input.Description, 4000, "Description");
        return store.ExecuteAsync(memberId, input.TenderId, async (session, ct) =>
        {
            var context = session.Context;
            var manager = TopCommitteeClarificationRules.RequireCreate(context, memberId);
            if (context.Tender!.Clarifications.Any(c => TopCommitteeClarificationScope.Contains(c, context) &&
                c.SenderUserId == memberId && c.Subject == subject && c.Description == description))
                throw new CommitteeStateConflictException("You have already submitted this question for this tender. Review the existing conversation instead.");
            var question = new Clarification { TenderId = input.TenderId, SenderUserId = memberId,
                RecipientUserId = manager.UserId, Subject = subject, Description = description,
                Status = "Pending", CreatedAt = DateTime.UtcNow };
            session.Add(question);
            await session.SaveAsync(ct);
            return new CommitteeClarificationResult(input.TenderId, question.ClarificationId);
        }, cancellationToken);
    }

    public async Task<CommitteeClarificationResult> AnswerAsync(ClaimsPrincipal manager, int clarificationId, string answer,
        CancellationToken cancellationToken = default)
    {
        var managerId = Actor(manager, RoleNames.SourcingManager);
        var message = Text(answer, 4000, "Answer");
        if (message == ClarificationResolution.Confirmation) throw new ValidationException("Enter an answer.");
        var tenderId = await store.FindTenderAsync(managerId, RoleNames.SourcingManager, clarificationId, cancellationToken)
            ?? throw new CommitteeTenderNotFoundException();
        return await store.ExecuteAsync(managerId, tenderId, async (session, ct) =>
        {
            var question = TopCommitteeClarificationRules.RequireAnswer(session.Context, managerId, clarificationId);
            session.Add(new ClarificationResponse { ClarificationId = clarificationId, RespondedByUserId = managerId,
                Message = message, CreatedAt = DateTime.UtcNow });
            question.Status = "Answered";
            await session.SaveAsync(ct);
            return new CommitteeClarificationResult(tenderId, clarificationId);
        }, cancellationToken);
    }

    public async Task<CommitteeClarificationResult> FollowUpAsync(ClaimsPrincipal actor, int clarificationId, string message,
        CancellationToken cancellationToken = default)
    {
        var memberId = Actor(actor, RoleNames.TopCommittee);
        var reply = Text(message, 4000, "Reply");
        if (reply == ClarificationResolution.Confirmation) throw new ValidationException("Enter a reply.");
        var tenderId = await store.FindTenderAsync(memberId, RoleNames.TopCommittee, clarificationId, cancellationToken)
            ?? throw new CommitteeTenderNotFoundException();
        return await store.ExecuteAsync(memberId, tenderId, async (session, ct) =>
        {
            var question = TopCommitteeClarificationRules.RequireFollowUp(session.Context, memberId, clarificationId);
            session.Add(new ClarificationResponse { ClarificationId = clarificationId, RespondedByUserId = memberId,
                Message = reply, CreatedAt = DateTime.UtcNow });
            question.Status = "Pending";
            await session.SaveAsync(ct);
            return new CommitteeClarificationResult(tenderId, clarificationId);
        }, cancellationToken);
    }

    public async Task<CommitteeClarificationResult> ConfirmResolutionAsync(ClaimsPrincipal manager, int clarificationId,
        CancellationToken cancellationToken = default)
    {
        var managerId = Actor(manager, RoleNames.SourcingManager);
        var tenderId = await store.FindTenderAsync(managerId, RoleNames.SourcingManager, clarificationId, cancellationToken)
            ?? throw new CommitteeTenderNotFoundException();
        return await store.ExecuteAsync(managerId, tenderId, async (session, ct) =>
        {
            var question = TopCommitteeClarificationRules.RequireManagerConfirmation(session.Context, managerId, clarificationId);
            Confirm(session, question, managerId, question.SenderUserId);
            await session.SaveAsync(ct);
            return new CommitteeClarificationResult(tenderId, clarificationId);
        }, cancellationToken);
    }

    private static void Confirm(ICommitteeClarificationSession session, Clarification question, string actor, string other)
    {
        var confirmations = ClarificationResolution.ConfirmedUsers(question.ClarificationResponses);
        if (confirmations.Contains(actor))
            throw new CommitteeStateConflictException("You already marked this clarification as resolved.");
        session.Add(ClarificationResolution.CreateConfirmation(question.ClarificationId, actor));
        if (confirmations.Contains(other))
        {
            question.Status = "Resolved";
            question.ResolvedAt = DateTime.UtcNow;
        }
    }

    public async Task<CommitteeClarificationResult> ResolveAsync(ClaimsPrincipal actor, int clarificationId,
        CancellationToken cancellationToken = default)
    {
        var memberId = Actor(actor, RoleNames.TopCommittee);
        var tenderId = await store.FindTenderAsync(memberId, RoleNames.TopCommittee, clarificationId, cancellationToken)
            ?? throw new CommitteeTenderNotFoundException();
        return await store.ExecuteAsync(memberId, tenderId, async (session, ct) =>
        {
            var question = TopCommitteeClarificationRules.RequireResolve(session.Context, memberId, clarificationId);
            Confirm(session, question, memberId, question.RecipientUserId);
            await session.SaveAsync(ct);
            return new CommitteeClarificationResult(tenderId, clarificationId);
        }, cancellationToken);
    }
}

internal sealed record CommitteeClarificationContext(Tender? Tender, bool ActorIsActive,
    IReadOnlyList<string> ActorRoles, IReadOnlyList<CommitteeMember> Members, IReadOnlyList<CommitteeManager> Managers);

internal interface ICommitteeClarificationSession
{
    CommitteeClarificationContext Context { get; }
    void Add(Clarification question);
    void Add(ClarificationResponse response);
    Task SaveAsync(CancellationToken cancellationToken);
}

internal interface ITopCommitteeClarificationStore
{
    Task<int?> FindTenderAsync(string actorId, string role, int clarificationId, CancellationToken cancellationToken);
    Task<T> ExecuteAsync<T>(string actorId, int tenderId, Func<ICommitteeClarificationSession, CancellationToken, Task<T>> action, CancellationToken cancellationToken);
}

internal sealed class TopCommitteeClarificationStore(ApplicationDbContext readContext, ITopCommitteeTenderTransaction transaction)
    : ITopCommitteeClarificationStore
{
    public async Task<int?> FindTenderAsync(string actorId, string role, int clarificationId, CancellationToken cancellationToken) =>
        await TopCommitteeClarificationScope.Query(readContext).Where(c => c.ClarificationId == clarificationId &&
            (role == RoleNames.TopCommittee ? c.SenderUserId == actorId : c.RecipientUserId == actorId))
            .Select(c => c.TenderId).SingleOrDefaultAsync(cancellationToken);

    public Task<T> ExecuteAsync<T>(string actorId, int tenderId,
        Func<ICommitteeClarificationSession, CancellationToken, Task<T>> action, CancellationToken cancellationToken) =>
        transaction.ExecuteAsync(tenderId, async (db, ct) =>
        {
            var context = await LoadAsync(db, actorId, tenderId, ct);
            return await action(new Session(db, context), ct);
        }, cancellationToken);

    internal static async Task<CommitteeClarificationContext> LoadAsync(ApplicationDbContext db, string actorId, int tenderId, CancellationToken cancellationToken, bool tracking = true)
    {
        var active = await db.Users.AsNoTracking().AnyAsync(u => u.Id == actorId && u.IsActive, cancellationToken);
        var roles = await (from userRole in db.UserRoles.AsNoTracking()
            join role in db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            where userRole.UserId == actorId select role.Name!).ToListAsync(cancellationToken);
        IQueryable<Tender> tenderQuery = db.Tenders.Include(t => t.TenderApprovals)
            .Include(t => t.Clarifications).ThenInclude(c => c.ClarificationResponses)
            .AsSplitQuery();
        if (!tracking) tenderQuery = tenderQuery.AsNoTracking();
        var tender = await tenderQuery.SingleOrDefaultAsync(t => t.TenderId == tenderId, cancellationToken);
        var access = new TopCommitteeAccessService(db);
        return new CommitteeClarificationContext(tender, active, roles,
            await access.GetMembersAsync(tenderId, cancellationToken), await access.GetManagersAsync(tenderId, cancellationToken));
    }

    private sealed class Session(ApplicationDbContext db, CommitteeClarificationContext context) : ICommitteeClarificationSession
    {
        public CommitteeClarificationContext Context => context;
        public void Add(Clarification question) => db.Clarifications.Add(question);
        public void Add(ClarificationResponse response) => db.ClarificationResponses.Add(response);
        public async Task SaveAsync(CancellationToken cancellationToken)
        {
            await TopCommitteeNotificationEvents.ClarificationsAsync(db,context,cancellationToken);
            var newQuestions = db.ChangeTracker.Entries<Clarification>()
                .Where(entry => entry.State == EntityState.Added).Select(entry => entry.Entity).ToList();
            var followUps = db.ChangeTracker.Entries<Clarification>()
                .Where(entry => entry.State == EntityState.Modified && entry.Entity.Status == "Pending" &&
                    entry.Property(c => c.Status).OriginalValue == "Answered")
                .Select(entry => entry.Entity).ToList();
            var confirmations = db.ChangeTracker.Entries<ClarificationResponse>()
                .Where(entry => entry.State == EntityState.Added && entry.Entity.Message == ClarificationResolution.Confirmation)
                .Select(entry => entry.Entity).ToList();
            foreach (var confirmation in confirmations)
            {
                var question = context.Tender!.Clarifications.Single(c => c.ClarificationId == confirmation.ClarificationId);
                var managerConfirmed = confirmation.RespondedByUserId == question.RecipientUserId;
                db.Notifications.Add(new Notification
                {
                    UserId = managerConfirmed ? question.SenderUserId : question.RecipientUserId,
                    Title = question.Status == "Resolved" ? "Committee clarification resolved" : "Confirm committee clarification resolution",
                    Message = "Open the conversation to review its status.",
                    RelativeUrl = managerConfirmed
                        ? $"/TopCommittee/Clarifications?tenderId={question.TenderId}#question-{question.ClarificationId}"
                        : $"/Clarification/Index/{question.ClarificationId}",
                    CreatedAt = DateTime.UtcNow
                });
            }
            await db.SaveChangesAsync(cancellationToken);
            foreach (var question in newQuestions.Concat(followUps))
            {
                db.Notifications.Add(new Notification
                {
                    UserId = question.RecipientUserId,
                    Title = "Committee clarification needs a reply",
                    Message = "Open the conversation to respond.",
                    RelativeUrl = $"/Clarification/Index/{question.ClarificationId}",
                    CreatedAt = DateTime.UtcNow
                });
            }
            if (newQuestions.Count + followUps.Count > 0)
                await db.SaveChangesAsync(cancellationToken);
        }
    }
}

internal static class TopCommitteeClarificationRules
{
    private static void RequireActor(CommitteeClarificationContext context, string role)
    {
        if (!context.ActorIsActive || !context.ActorRoles.Contains(role)) throw new CommitteeAccessDeniedException();
    }

    private static Tender RequireOpen(CommitteeClarificationContext context)
    {
        var tender = context.Tender ?? throw new CommitteeTenderNotFoundException();
        if (tender.Status != "AwaitingCommitteeDecision" || tender.AwardedBidId.HasValue || tender.AwardedAt.HasValue ||
            tender.TenderApprovals.Any(v => v.Stage == "Top" && v.Decision == "Rejected"))
            throw new CommitteeStateConflictException("This tender is no longer open for committee clarifications.");
        return tender;
    }

    internal static CommitteeManager ResponsibleManager(CommitteeClarificationContext context)
    {
        if (context.Managers.Count != 1 || !context.Managers[0].IsActive || !context.Managers[0].HasManagerRole)
            throw new CommitteeStateConflictException("A single active referring Sourcing Manager must be recorded before a clarification can be processed.");
        return context.Managers[0];
    }

    internal static CommitteeManager RequireCreate(CommitteeClarificationContext context, string memberId)
    {
        RequireActor(context, RoleNames.TopCommittee);
        if (context.Tender == null || !context.Members.Any(m => m.UserId == memberId && m.IsEligible))
            throw new CommitteeTenderNotFoundException();
        var tender = RequireOpen(context);
        if (tender.TenderApprovals.Any(v => v.Stage == "Top" && v.UserId == memberId))
            throw new CommitteeStateConflictException("You cannot create a clarification after recording your final vote.");
        var manager = ResponsibleManager(context);
        if (manager.UserId == memberId)
            throw new CommitteeStateConflictException("A clarification must be addressed to a different responsible Sourcing Manager.");
        return manager;
    }

    internal static Clarification RequireQuestion(CommitteeClarificationContext context, int clarificationId)
    {
        var question = context.Tender?.Clarifications.SingleOrDefault(c => c.ClarificationId == clarificationId);
        if (question == null || !TopCommitteeClarificationScope.Contains(question, context))
            throw new CommitteeTenderNotFoundException();
        return question;
    }

    internal static Clarification RequireAnswer(CommitteeClarificationContext context, string managerId, int clarificationId)
    {
        RequireActor(context, RoleNames.SourcingManager);
        var question = RequireQuestion(context, clarificationId);
        if (question.RecipientUserId != managerId) throw new CommitteeTenderNotFoundException();
        RequireOpen(context);
        if (ResponsibleManager(context).UserId != managerId) throw new CommitteeTenderNotFoundException();
        if (question.Status != "Pending" || question.ResolvedAt.HasValue)
            throw new CommitteeStateConflictException("This clarification has already been answered or resolved. Reload to view its current status.");
        return question;
    }

    internal static Clarification RequireFollowUp(CommitteeClarificationContext context, string memberId, int clarificationId)
    {
        var question = RequireResolve(context, memberId, clarificationId);
        var manager = ResponsibleManager(context);
        var latestResponse = question.ClarificationResponses.Where(r => r.Message != ClarificationResolution.Confirmation).OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.ClarificationResponseId).FirstOrDefault();
        if (latestResponse?.RespondedByUserId != manager.UserId)
            throw new CommitteeStateConflictException("Wait for the Sourcing Manager to answer the last question.");
        return question;
    }

    internal static Clarification RequireManagerConfirmation(CommitteeClarificationContext context, string managerId, int clarificationId)
    {
        RequireActor(context, RoleNames.SourcingManager);
        var question = RequireQuestion(context, clarificationId);
        RequireOpen(context);
        if (question.RecipientUserId != managerId || ResponsibleManager(context).UserId != managerId)
            throw new CommitteeTenderNotFoundException();
        if (question.Status != "Answered" || question.ResolvedAt.HasValue)
            throw new CommitteeStateConflictException("Answer the current question before confirming resolution.");
        return question;
    }

    internal static Clarification RequireResolve(CommitteeClarificationContext context, string memberId, int clarificationId)
    {
        RequireActor(context, RoleNames.TopCommittee);
        var question = RequireQuestion(context, clarificationId);
        if (question.SenderUserId != memberId || !context.Members.Any(m => m.UserId == memberId && m.IsEligible))
            throw new CommitteeTenderNotFoundException();
        RequireOpen(context);
        var manager = ResponsibleManager(context);
        if (question.Status != "Answered" || question.ResolvedAt.HasValue ||
            !question.ClarificationResponses.Any(r => r.RespondedByUserId == manager.UserId && !string.IsNullOrWhiteSpace(r.Message)))
            throw new CommitteeStateConflictException("Only an answered clarification with a valid Manager response can be resolved, once.");
        return question;
    }
}

public sealed class TopCommitteeClarificationQueries(ApplicationDbContext db, TopCommitteeAccessService access) : ITopCommitteeClarificationQueries
{
    public async Task<CommitteeClarificationsPage> GetPageAsync(ClaimsPrincipal actor, int? tenderId, CancellationToken cancellationToken = default)
    {
        var memberId = await access.RequireRoleAsync(actor, RoleNames.TopCommittee, cancellationToken);
        CommitteeClarificationContext? context = null;
        if (tenderId.HasValue)
        {
            await access.RequireAssignmentAsync(actor, tenderId.Value, cancellationToken);
            context = await TopCommitteeClarificationStore.LoadAsync(db, memberId, tenderId.Value, cancellationToken, tracking: false);
        }
        var choices = await db.Tenders.AsNoTracking()
            .Where(t => t.CommitteeAssignments.Any(a => a.CommitteeType == "Top" && a.UserId == memberId))
            .OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.TenderId)
            .Select(t => new CommitteeClarificationTender(t.TenderId, t.Title, t.Status)).ToListAsync(cancellationToken);
        var questions = TopCommitteeClarificationScope.Query(db).Where(c => c.SenderUserId == memberId);
        if (tenderId.HasValue) questions = questions.Where(c => c.TenderId == tenderId);
        var data = await questions.Include(c => c.Tender).Include(c => c.RecipientUser)
            .Include(c => c.ClarificationResponses).ThenInclude(r => r.RespondedByUser)
            .OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.ClarificationId).AsSplitQuery().ToListAsync(cancellationToken);

        string? managerName = null;
        string? blocked = "Choose an assigned tender to create a clarification.";
        if (context != null)
        {
            try { managerName = TopCommitteeClarificationRules.RequireCreate(context, memberId).FullName; blocked = null; }
            catch (CommitteeStateConflictException error) { blocked = error.Message; }
            managerName ??= context.Managers.Count == 1 ? context.Managers[0].FullName : null;
        }
        var threads = data.Select(c =>
        {
            var canResolve = false;
            if (context != null)
            {
                try { TopCommitteeClarificationRules.RequireResolve(context, memberId, c.ClarificationId); canResolve = true; }
                catch (CommitteeStateConflictException) { }
            }
            return new CommitteeClarificationThread(c.ClarificationId, c.TenderId!.Value, c.Tender!.Title,
                c.Subject, c.Description, c.RecipientUser.FullName, c.Status, c.CreatedAt, c.ResolvedAt, canResolve,
                c.ClarificationResponses.OrderBy(r => r.CreatedAt).ThenBy(r => r.ClarificationResponseId)
                    .Select(r => new CommitteeClarificationResponse(r.RespondedByUserId, r.RespondedByUser.FullName,
                        r.Message, r.CreatedAt)).ToList());
        }).ToList();
        return new CommitteeClarificationsPage(choices, threads, new CommitteeCreateClarification { TenderId = tenderId ?? 0 },
            managerName, blocked, context == null ? 0 : TopCommitteeClarificationScope.OpenCount(context));
    }
}

internal static class TopCommitteeClarificationScope
{
    internal static IQueryable<Clarification> Query(ApplicationDbContext db) => db.Clarifications.AsNoTracking()
        .Where(c => c.TenderId.HasValue && !c.PurchaseRequestId.HasValue && !c.BidId.HasValue &&
            db.CommitteeAssignments.Any(a => a.TenderId == c.TenderId && a.CommitteeType == "Top" && a.UserId == c.SenderUserId) &&
            db.TenderApprovals.Any(a => a.TenderId == c.TenderId && a.Stage == "Manager" &&
                a.Decision == "Approved" && a.UserId == c.RecipientUserId));

    internal static bool Contains(Clarification question, CommitteeClarificationContext context) =>
        question.TenderId == context.Tender?.TenderId && question.TenderId.HasValue &&
        !question.PurchaseRequestId.HasValue && !question.BidId.HasValue &&
        context.Members.Any(m => m.UserId == question.SenderUserId) &&
        context.Managers.Any(m => m.UserId == question.RecipientUserId);

    internal static int OpenCount(CommitteeClarificationContext context) => context.Tender?.Clarifications.Count(c =>
        Contains(c, context) && (c.Status is "Pending" or "Answered")) ?? 0;
}
