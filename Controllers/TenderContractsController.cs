using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Services.Reports;

namespace SourcingTenderManagement.Controllers;

// The contract for an awarded tender: generate it, upload the signed copy and download it.
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TenderContractsController(ApplicationDbContext db, IWebHostEnvironment environment) : Controller
{
    private const int MaxFileSize = 10 * 1024 * 1024;
    private const string Editors = RoleNames.Admin + "," + RoleNames.SourcingManager;
    private string? UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);
    private string Storage => Path.Combine(environment.ContentRootPath, "App_Data", "private-files", "contracts");

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var tender = await AwardQuery(id).Select(t => new { t.Title, t.TenderId }).SingleOrDefaultAsync(ct);
        if (tender is null) return NotFound();
        var contract = await db.TenderContracts.AsNoTracking().Where(c => c.TenderId == id)
            .Select(c => new { c.GeneratedAt, c.SignedFileName, c.SignedUploadedAt, c.Notes }).SingleOrDefaultAsync(ct);
        return View(new TenderContractPage(tender.TenderId, tender.Title, contract?.GeneratedAt,
            contract?.SignedFileName, contract?.SignedUploadedAt, contract?.Notes, User.IsInRole(RoleNames.Admin) || User.IsInRole(RoleNames.SourcingManager)));
    }

    [Authorize(Roles = Editors), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Generate(int id, CancellationToken ct)
    {
        if (UserId is not { } userId) return Forbid();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Lock this row until the transaction ends, so two people cannot change the same record at the same time.
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @id int; SELECT @id= TenderId FROM dbo.Tenders WITH (UPDLOCK,HOLDLOCK) WHERE TenderId={id};", ct);
        var tender = await AwardQuery(id).Include(t => t.PurchaseRequest)
            .ThenInclude(r => r.PurchaseRequestItems)
            .SingleOrDefaultAsync(ct);
        if (tender is null) return NotFound();
        var winner = await db.Bids.AsNoTracking().Include(b => b.Invitation).ThenInclude(i => i.SupplierProfile)
            .Include(b => b.BidItems).ThenInclude(i => i.PurchaseRequestItem)
            .SingleOrDefaultAsync(b => b.BidId == tender.AwardedBidId && b.TenderId == id, ct);
        if (winner is null) return Conflict("The awarded offer is unavailable.");
        var pdf = ReportPdfExport.AwardContract(tender, winner);
        var contract = await db.TenderContracts.SingleOrDefaultAsync(c => c.TenderId == id, ct);
        if (contract is null)
        {
            contract = new TenderContract { TenderId = id, BidId = winner.BidId, GeneratedPdf = pdf, GeneratedByUserId = userId };
            db.TenderContracts.Add(contract);
        }
        contract.GeneratedAt = DateTime.UtcNow;
        contract.GeneratedByUserId = userId;
        contract.BidId = winner.BidId;
        contract.GeneratedPdf = pdf;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Roles = Editors), HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(MaxFileSize + 128 * 1024)]
    public async Task<IActionResult> UploadSigned(int id, IFormFile? signedFile, string? notes, CancellationToken ct)
    {
        if (UserId is not { } userId) return Forbid();
        var fileName = Path.GetFileName(signedFile?.FileName ?? "");
        if (signedFile is null || signedFile.Length < 5 || signedFile.Length > MaxFileSize || fileName.Length > 255 ||
            !fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) || notes?.Length > 1000)
            return BadRequest("Choose a PDF up to 10 MB. Notes may be up to 1,000 characters.");
        using var buffer = new MemoryStream((int)signedFile.Length);
        await signedFile.CopyToAsync(buffer, ct);
        var content = buffer.ToArray();
        if (!IsPdf(content)) return BadRequest("The signed copy must be a valid PDF.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Lock this row until the transaction ends, so two people cannot change the same record at the same time.
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @id int; SELECT @id=TenderId FROM dbo.Tenders WITH (UPDLOCK,HOLDLOCK) WHERE TenderId={id};", ct);
        var contract = await db.TenderContracts.SingleOrDefaultAsync(c => c.TenderId == id && AwardQuery(id).Any(), ct);
        if (contract is null) return NotFound();

        Directory.CreateDirectory(Storage);
        var previous = contract.SignedStoredFileName;
        var stored = Guid.NewGuid().ToString("N") + ".pdf";
        var path = Path.Combine(Storage, stored);
        try
        {
            await using (var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                await target.WriteAsync(content, ct);
            contract.SignedFileName = fileName;
            contract.SignedStoredFileName = stored;
            contract.SignedContentType = "application/pdf";
            contract.SignedSizeBytes = signedFile.Length;
            contract.SignedUploadedByUserId = userId;
            contract.SignedUploadedAt = DateTime.UtcNow;
            contract.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            DeleteFile(path);
            throw;
        }
        if (previous is not null) DeleteFile(Path.Combine(Storage, Path.GetFileName(previous)));
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Download(int id, bool signed, CancellationToken ct)
    {
        var contract = await db.TenderContracts.AsNoTracking().Where(c => c.TenderId == id && AwardQuery(id).Any())
            .Select(c => new { Generated = signed ? null : c.GeneratedPdf, c.SignedFileName, c.SignedStoredFileName }).SingleOrDefaultAsync(ct);
        if (contract is null) return NotFound();
        if (!signed
            && contract.Generated is { } bytes) return File(bytes, "application/pdf", $"tender-{id}-award-contract.pdf");
        if (contract.SignedStoredFileName is not { } stored || Path.GetFileName(stored) != stored) return NotFound();
        var path = Path.Combine(Storage, stored);
        return System.IO.File.Exists(path) ? PhysicalFile(path, "application/pdf", contract.SignedFileName) : NotFound();
    }

    private IQueryable<Tender> AwardQuery(int id)
    {
        var user = UserId;
        var rows = db.Tenders.AsNoTracking()
            .Where(t => t.TenderId == id && t.Status == "Awarded" && t.AwardedBidId != null);
        if (user is null) return rows.Where(_ => false);
        if (User.IsInRole(RoleNames.Admin) || User.IsInRole(RoleNames.SourcingManager)) return rows;
        if (User.IsInRole(RoleNames.TopCommittee))
            return rows.Where(t => t.CommitteeAssignments.Any(a => a.CommitteeType == "Top" && a.UserId == user));
        if (User.IsInRole(RoleNames.Supplier))
            return rows.Where(t => t.Bid != null
                && t.Bid.TenderId == t.TenderId
                && t.Bid.Invitation.SupplierProfile.UserId == user);
        return rows.Where(_ => false);
    }

    private static bool IsPdf(byte[] bytes)
    {
        if (!bytes.AsSpan().StartsWith("%PDF-"u8)) return false;
        try
        {
            using var pdf = UglyToad.PdfPig.PdfDocument.Open(bytes);
            return pdf.NumberOfPages > 0;
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
        {
            return false;
        }
    }

    private static void DeleteFile(string path)
    {
        try { System.IO.File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

public sealed record TenderContractPage(int TenderId, string Tender, DateTime? GeneratedAt,
    string? SignedFileName, DateTime? SignedAt, string? Notes, bool CanEdit);
