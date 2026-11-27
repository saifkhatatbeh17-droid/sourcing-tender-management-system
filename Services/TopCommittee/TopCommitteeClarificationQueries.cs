using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Contracts.TopCommittee;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.TopCommittee;

namespace SourcingTenderManagement.Services.TopCommittee;

// Loads the CEO & Chiefs clarifications page.
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
