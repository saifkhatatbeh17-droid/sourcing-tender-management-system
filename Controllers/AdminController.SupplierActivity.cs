using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Services.Reports;

namespace SourcingTenderManagement.Controllers;

public partial class AdminController
{
    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> SupplierActivity(int? supplierId, CancellationToken ct)
    {
        var suppliers = await db.SupplierProfiles.AsNoTracking()
            .Where(s => supplierId == null || s.SupplierProfileId == supplierId)
            .OrderBy(s => s.CompanyName).ThenBy(s => s.SupplierProfileId)
            .Select(s => new { s.SupplierProfileId, s.CompanyName, s.Status, s.RegistrationNumber, s.Category }).ToListAsync(ct);
        if (supplierId.HasValue && suppliers.Count == 0) return NotFound();
        var submissions = await db.Bids.AsNoTracking().Where(b => b.SubmittedAt != null)
            .Where(b => supplierId == null || b.Invitation.SupplierProfileId == supplierId)
            .Select(b => new
            {
                b.Invitation.SupplierProfileId, b.TenderId, b.BidId, b.SubmittedAt,
                b.Status, b.ComplianceStatus, b.Currency,
                Total = (b.BidItems.Sum(i => (decimal?)(i.Quantity * i.UnitPrice)) ?? 0) + b.TaxAmount + b.AdditionalCosts,
                b.Invitation.Tender.Title, TenderStatus = b.Invitation.Tender.Status,
                b.Invitation.Tender.AwardedBidId,
                RejectedByCommittee = b.Invitation.Tender.TenderApprovals.Any(a => a.Stage == "Top" && a.Decision == "Rejected")
            }).ToListAsync(ct);
        var history = submissions.GroupBy(b => new { b.SupplierProfileId, b.TenderId })
            .Select(group => group.OrderByDescending(b => b.BidId == b.AwardedBidId)
                .ThenByDescending(b => b.SubmittedAt).ThenByDescending(b => b.BidId).First())
            .ToLookup(b => b.SupplierProfileId, b => new SupplierTenderActivity(b.TenderId, b.Title, b.BidId, b.SubmittedAt!.Value,
                b.TenderStatus == "Awarded" && b.AwardedBidId == b.BidId ? "Awarded" :
                b.Status == "Withdrawn" ? "Withdrawn" :
                b.TenderStatus == "Cancelled" && !b.RejectedByCommittee ? "Cancelled" :
                b.RejectedByCommittee || b.TenderStatus == "Awarded" || b.Status is "Rejected" or "NonCompliant" ||
                b.ComplianceStatus == "NonCompliant" ? "Rejected" : "Pending", b.Total, b.Currency, b.ComplianceStatus));
        var report = suppliers.Select(s => new SupplierActivityRow(s.SupplierProfileId, s.CompanyName, s.Status, s.RegistrationNumber, s.Category,
            history[s.SupplierProfileId].OrderByDescending(b => b.SubmittedAt).ToArray())).ToArray();
        if (!supplierId.HasValue) return View(report);
        return ReportPdfDownload.Create(this, () => ReportPdfExport.SupplierActivity(report[0]), $"supplier-{supplierId}-activity.pdf");
    }
}
