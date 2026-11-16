using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Contracts.TopCommittee;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Services.Chat;

public sealed class AiChatContext(ApplicationDbContext db, ITopCommitteeQueries topCommittee,
    ITopCommitteeVotingReadiness votingReadiness)
{
    internal async Task<object?> ReadAsync(AiChatRecord record, int id, ClaimsPrincipal user, string userId,
        int? departmentId, CancellationToken ct)
    {
        switch (record)
        {
            case AiChatRecord.DepartmentRequest when departmentId.HasValue:
                return await ReadRequestAsync(db.PurchaseRequests.InDepartment(departmentId), id, userId, ct);
            case AiChatRecord.OfficerRequest:
                return await ReadRequestAsync(db.PurchaseRequests.ReviewableBy(userId), id, userId, ct);
            case AiChatRecord.OfficerTender:
                return await db.Tenders.AsNoTracking().Where(t => t.TenderId == id && t.CreatedByUserId == userId)
                    .Select(t => new { t.Title, t.Status, t.OpensAt, t.ClosesAt }).SingleOrDefaultAsync(ct);
            case AiChatRecord.EvaluationTender:
                return await db.Tenders.AsNoTracking().InEvaluationWorkspace(userId).Where(t => t.TenderId == id)
                    .Select(t => new
                    {
                        t.Title, t.Status,
                        IsAssigned = t.CommitteeAssignments.Any(a => a.CommitteeType == "Evaluation" && a.UserId == userId),
                        WeightsLocked = t.CommitteeAssignments.Any(a => a.CommitteeType == "Evaluation" && a.UserId == userId)
                            ? (bool?)(t.CommitteeWeightsLockedAt != null) : null
                    }).SingleOrDefaultAsync(ct);
            case AiChatRecord.ManagerReport:
                return await db.EvaluationReports.AsNoTracking().InManagerReview().Where(r => r.EvaluationReportId == id)
                    .Select(r => new { r.Tender.Title, TenderStatus = r.Tender.Status, ReportStatus = r.Status, r.SubmittedAt })
                    .SingleOrDefaultAsync(ct);
            case AiChatRecord.TopTender:
                try
                {
                    var tender = await topCommittee.GetTenderDetailsAsync(user, id, ct);
                    return new
                    {
                        tender.Title, tender.Status, tender.OpensAt, tender.ClosesAt,
                        tender.OpenClarifications, tender.AwardedAt,
                        CanCurrentMemberVote = tender.Readiness.CanCurrentMemberVote &&
                            await votingReadiness.IsDatabaseReadyAsync(user, id, ct)
                    };
                }
                catch (CommitteeTenderNotFoundException) { return null; }
                catch (CommitteeAccessDeniedException) { return null; }
            case AiChatRecord.SupplierInvitation:
                return await db.Invitations.AsNoTracking().Where(i => i.InvitationId == id &&
                        i.SupplierProfile.UserId == userId && i.SupplierProfile.Status == "Approved")
                    .Select(i => new { i.Tender.Title, InvitationStatus = i.Status, TenderStatus = i.Tender.Status, i.Tender.ClosesAt })
                    .SingleOrDefaultAsync(ct);
            case AiChatRecord.SupplierOffer:
                return await db.Bids.AsNoTracking().Where(b => b.BidId == id &&
                        b.Invitation.SupplierProfile.UserId == userId && b.Invitation.SupplierProfile.Status == "Approved")
                    .Select(b => new
                    {
                        b.Invitation.Tender.Title, OfferStatus = b.Status, TenderStatus = b.Invitation.Tender.Status,
                        b.SubmittedAt, b.DeliveryDays, b.Currency, b.Invitation.Tender.ClosesAt
                    }).SingleOrDefaultAsync(ct);
            case AiChatRecord.SupplierApplication:
                return await db.SupplierProfiles.AsNoTracking().Where(s => s.SupplierProfileId == id)
                    .Select(s => new { s.CompanyName, s.Status, s.Category, s.ReviewedAt })
                    .SingleOrDefaultAsync(ct);
            case AiChatRecord.ContactMessage:
                return await db.ContactMessages.AsNoTracking().Where(m => m.ContactMessageId == id)
                    .Select(m => new { m.Status, m.SubmittedAt }).SingleOrDefaultAsync(ct);
            default:
                return null;
        }
    }

    private static async Task<object?> ReadRequestAsync(IQueryable<PurchaseRequest> requests, int id, string userId,
        CancellationToken ct) =>
        await requests.AsNoTracking().Where(r => r.PurchaseRequestId == id)
            .Select(r => new
            {
                r.Title, r.Status, r.Priority, r.TargetDate,
                ReturnReason = r.Status == "Returned" ? r.ReviewNotes : null,
                CanRequesterEdit = r.RequestedByUserId == userId && r.Status == "Returned"
            }).SingleOrDefaultAsync(ct);
}
