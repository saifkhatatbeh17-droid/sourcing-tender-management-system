using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Models.TopCommittee;

namespace SourcingTenderManagement.Services.TopCommittee;

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
