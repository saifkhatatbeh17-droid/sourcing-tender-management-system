using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Services.Reports;
using SourcingTenderManagement.Services.Reports.Data;

namespace SourcingTenderManagement.Controllers;

// Admin page that shows what each supplier has done (invitations, offers, awards).
public partial class AdminController
{
    // GET /Admin/SupplierActivity: Shows the "Supplier activity report" page.
    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> SupplierActivity(int? supplierId, [FromServices] SupplierActivityQuery activity, CancellationToken ct)
    {
        var suppliers = await db.SupplierProfiles.AsNoTracking()
            .Where(s => supplierId == null || s.SupplierProfileId == supplierId)
            .OrderBy(s => s.CompanyName).ThenBy(s => s.SupplierProfileId)
            .Select(s => new { s.SupplierProfileId, s.CompanyName, s.Status, s.RegistrationNumber, s.Category }).ToListAsync(ct);
        if (supplierId.HasValue && suppliers.Count == 0) return NotFound();
        var history = await activity.ReadAsync(db.Tenders.Select(t => t.TenderId), supplierId, ct);
        var report = suppliers.Select(s => new SupplierActivityRow(s.SupplierProfileId, s.CompanyName, s.Status, s.RegistrationNumber, s.Category,
            history[s.SupplierProfileId].OrderByDescending(b => b.SubmittedAt).ToArray())).ToArray();
        if (!supplierId.HasValue) return View(report);
        return ReportPdfDownload.Create(this, () => ReportPdfExport.SupplierActivity(report[0]), $"supplier-{supplierId}-activity.pdf");
    }
}
