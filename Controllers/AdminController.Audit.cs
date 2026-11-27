using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Services.Reports;

namespace SourcingTenderManagement.Controllers;

// Admin pages for the audit trail: one audit entry, and a tender's evaluation report as PDF.
public partial class AdminController
{
    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> TenderEvaluationPdf(int id, CancellationToken ct)
    {
        var report = await db.EvaluationReports.AsNoTracking().Include(r => r.Tender).Include(r => r.PreparedByUser)
            .Include(r => r.Bid).ThenInclude(b => b!.Invitation).ThenInclude(i => i.SupplierProfile)
            .SingleOrDefaultAsync(r => r.TenderId == id, ct);
        return report is null ? NotFound() : ReportPdfDownload.Create(this, () => ReportPdfExport.OfficerReport(report),
            $"tender-{id}-evaluation.pdf");
    }

    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> AuditEntry(long id, CancellationToken ct)
    {
        var entry = await db.AuditEntries.AsNoTracking().SingleOrDefaultAsync(a => a.AuditEntryId == id, ct);
        return entry is null ? NotFound() : View(entry);
    }
}
