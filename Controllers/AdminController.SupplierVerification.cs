using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SourcingTenderManagement.Controllers;

public partial class AdminController
{
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetSupplierSuspension(int id, bool suspend, string? reason, CancellationToken ct)
    {
        reason = reason?.Trim();
        if (suspend && (string.IsNullOrEmpty(reason) || reason.Length > 1000)) return BadRequest("Enter a suspension reason of up to 1,000 characters.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @locked int; SELECT @locked=SupplierProfileId FROM dbo.SupplierProfiles WITH (UPDLOCK,HOLDLOCK) WHERE SupplierProfileId={id};", ct);
        var supplier = await db.SupplierProfiles.SingleOrDefaultAsync(s => s.SupplierProfileId == id, ct);
        if (supplier is null) return NotFound();
        if (supplier.Status != (suspend ? "Approved" : "Suspended")) return Conflict("The supplier status has changed.");
        supplier.Status = suspend ? "Suspended" : "Approved";
        supplier.ReviewNotes = suspend ? reason : "Supplier reactivated.";
        supplier.ReviewedAt = DateTime.UtcNow;
        supplier.ReviewedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        TempData["SupplierReviewFeedback"] = suspend ? "Supplier suspended." : "Supplier reactivated.";
        return RedirectToAction(nameof(SupplierDetails), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifySupplierDocument(int id, string status, string? note, CancellationToken ct)
    {
        note = note?.Trim();
        if (status is not ("Verified" or "Rejected") || note?.Length > 1000 ||
            (status == "Rejected" && string.IsNullOrEmpty(note))) return BadRequest("Enter a valid decision and a rejection note of up to 1,000 characters.");
        var document = await db.SupplierDocuments.SingleOrDefaultAsync(d => d.SupplierDocumentId == id, ct);
        if (document is null) return NotFound();
        document.VerificationStatus = status;
        document.VerificationNote = note;
        document.VerifiedAt = DateTime.UtcNow;
        document.VerifiedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        await db.SaveChangesAsync(ct);
        TempData["SupplierReviewFeedback"] = "Document verification saved.";
        return RedirectToAction(nameof(SupplierDetails), new { id = document.SupplierProfileId });
    }
}
