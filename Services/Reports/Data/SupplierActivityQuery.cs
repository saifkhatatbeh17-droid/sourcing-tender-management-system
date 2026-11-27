using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models;

namespace SourcingTenderManagement.Services.Reports.Data;

// Counts each supplier's invitations, offers and awards (used by the supplier reports).
public sealed class SupplierActivityQuery(ApplicationDbContext db)
{
    public async Task<ILookup<int, SupplierTenderActivity>> ReadAsync(IQueryable<int> tenderIds,
        int? supplierId, CancellationToken ct)
    {
        var bids = await db.Bids.AsNoTracking().Where(b => b.SubmittedAt != null && tenderIds.Contains(b.TenderId))
            .Where(b => supplierId == null || b.Invitation.SupplierProfileId == supplierId)
            .Select(b => new
            {
                b.Invitation.SupplierProfileId, b.TenderId, b.BidId, b.SubmittedAt, b.Status, b.ComplianceStatus, b.Currency,
                Total = (b.BidItems.Sum(i => (decimal?)(i.Quantity * i.UnitPrice))
                    ?? 0) + b.TaxAmount + b.AdditionalCosts,
                b.Invitation.Tender.Title, TenderStatus = b.Invitation.Tender.Status, b.Invitation.Tender.AwardedBidId,
                RejectedByCommittee = b.Invitation.Tender.TenderApprovals.Any(a => a.Stage == "Top"
                    && a.Decision == "Rejected")
            }).ToListAsync(ct);
        return bids.GroupBy(b => new { b.SupplierProfileId, b.TenderId })
            .Select(group => group.OrderByDescending(b => b.BidId == b.AwardedBidId)
                .ThenByDescending(b => b.SubmittedAt).ThenByDescending(b => b.BidId).First())
            .ToLookup(b => b.SupplierProfileId, b => new SupplierTenderActivity(b.TenderId, b.Title, b.BidId, b.SubmittedAt!.Value,
                b.TenderStatus == "Awarded" && b.AwardedBidId == b.BidId ? "Awarded" :
                b.Status == "Withdrawn" ? "Withdrawn" :
                b.TenderStatus == "Cancelled" && !b.RejectedByCommittee ? "Cancelled" :
                b.RejectedByCommittee || b.TenderStatus == "Awarded" || b.Status is "Rejected" or "NonCompliant" ||
                b.ComplianceStatus == "NonCompliant" ? "Rejected" : "Pending", b.Total, b.Currency, b.ComplianceStatus));
    }
}
