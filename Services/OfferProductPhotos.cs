using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Services;

public sealed record ProductPhotoView(int DocumentId, string ItemName);
public sealed class OfferProductPhotos(ApplicationDbContext db, IWebHostEnvironment env)
{
    public const string Prefix = "Product photo ";
    public static string Type(int itemId) => Prefix + itemId;
    public string PathFor(string stored) => Path.Combine(env.ContentRootPath, "App_Data", "private-files", "offers", Path.GetFileName(stored));

    public static async Task<byte[]> PrepareAsync(IFormFile file, CancellationToken ct = default)
    {
        if (file.Length is <= 0 or > 5 * 1024 * 1024 || Path.GetExtension(file.FileName).ToLowerInvariant() is not (".jpg" or ".jpeg" or ".png" or ".webp"))
            throw new ValidationException("Product photos must be JPG, PNG or WebP, up to 5 MB each.");
        using var input = new MemoryStream(); await file.CopyToAsync(input, ct); input.Position = 0;
        using var data = SKData.Create(input);
        using var codec = SKCodec.Create(data);
        if (codec == null || codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png or SKEncodedImageFormat.Webp))
            throw new ValidationException("The product photo is not a readable image. Choose JPG, PNG or WebP.");
        var info = codec.Info;
        if ((long)info.Width * info.Height > 20_000_000 || info.Width <= 0 || info.Height <= 0)
            throw new ValidationException("Product photos must be smaller than 20 megapixels.");
        using var bitmap = SKBitmap.Decode(codec) ?? throw new ValidationException("The product photo could not be decoded.");
        var scale = Math.Min(1d, 1600d / Math.Max(info.Width, info.Height));
        using var resized = bitmap.Resize(new SKImageInfo(Math.Max(1, (int)(info.Width * scale)), Math.Max(1, (int)(info.Height * scale))), new SKSamplingOptions(SKFilterMode.Linear));
        using var image = SKImage.FromBitmap(resized ?? bitmap);
        using var jpeg = image.Encode(SKEncodedImageFormat.Jpeg, 85);
        return jpeg.ToArray(); // Re-encode to a trusted format without source metadata.
    }

    public IQueryable<Bid> VisibleBids(ClaimsPrincipal principal)
    {
        var user = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var bids = db.Bids.AsNoTracking();
        if (user == null) return bids.Where(_ => false);
        if (principal.IsInRole(RoleNames.Admin)) return bids;
        return bids.Where(b =>
            (principal.IsInRole(RoleNames.Supplier) && b.Invitation.SupplierProfile.UserId == user) ||
            (principal.IsInRole(RoleNames.SourcingOfficer) && b.Status != "Draft" && b.Invitation.Tender.CreatedByUserId == user) ||
            (principal.IsInRole(RoleNames.EvaluationCommittee) && b.ComplianceStatus == "Compliant" &&
                (b.Invitation.Tender.Status == "UnderEvaluation" || b.Invitation.Tender.CommitteeAssignments.Any(a => a.CommitteeType == "Evaluation" && a.UserId == user))) ||
            (principal.IsInRole(RoleNames.SourcingManager) && b.Status != "Draft" && b.Invitation.Tender.EvaluationReport != null &&
                (b.Invitation.Tender.Status == "AwaitingManagerReview" || b.Invitation.Tender.Status == "AwaitingCommitteeDecision" || b.Invitation.Tender.Status == "Awarded")) ||
            (principal.IsInRole(RoleNames.TopCommittee) && b.Status != "Draft" && b.Invitation.Tender.CommitteeAssignments.Any(a => a.CommitteeType == "Top" && a.UserId == user)));
    }

    public async Task<List<ProductPhotoView>> ListAsync(int bidId, ClaimsPrincipal principal)
    {
        if (!await VisibleBids(principal).AnyAsync(b => b.BidId == bidId)) return [];
        var documents = await db.BidDocuments.AsNoTracking().Where(d => d.BidId == bidId && d.DocumentType.StartsWith(Prefix)).OrderBy(d => d.BidDocumentId).ToListAsync();
        var items = await db.BidItems.AsNoTracking().Where(i => i.BidId == bidId).Select(i => new { i.PurchaseRequestItemId, i.PurchaseRequestItem.ItemName }).ToListAsync();
        return documents.Select(d => new ProductPhotoView(d.BidDocumentId, items.FirstOrDefault(i => Type(i.PurchaseRequestItemId) == d.DocumentType)?.ItemName ?? "Product")).ToList();
    }
}
