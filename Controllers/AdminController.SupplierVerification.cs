using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SourcingTenderManagement.Controllers;

// Admin actions on a supplier account: suspend or reactivate it, and mark its documents as verified.
public partial class AdminController
{
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetSupplierSuspension(int id, bool suspend, string? reason, CancellationToken ct)
    {
        reason = reason?.Trim();
        if (suspend
            && (string.IsNullOrEmpty(reason) || reason.Length > 1000)) return BadRequest("Enter a suspension reason of up to 1,000 characters.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Lock this row until the transaction ends, so two people cannot change the same record at the same time.
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
    public async Task<IActionResult> VerifySupplierDocument(int id, string status, CancellationToken ct)
    {
        // A document can only be marked verified here. Rejecting a supplier is done on the Decision step.
        if (status != "Verified") return BadRequest("Documents can only be marked as verified. Reject the supplier on the Decision step.");
        var document = await db.SupplierDocuments.SingleOrDefaultAsync(d => d.SupplierDocumentId == id, ct);
        if (document is null) return NotFound();
        document.VerificationStatus = status;
        document.VerificationNote = null;
        document.VerifiedAt = DateTime.UtcNow;
        document.VerifiedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        await db.SaveChangesAsync(ct);
        TempData["SupplierReviewFeedback"] = "Document verification saved.";
        return RedirectToAction(nameof(SupplierDetails), new { id = document.SupplierProfileId });
    }
}
