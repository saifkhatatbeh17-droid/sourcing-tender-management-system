using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QRCoder;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Services;
using Microsoft.AspNetCore.Identity;

namespace SourcingTenderManagement.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class GuaranteesController(
    ApplicationDbContext db, OfferGuarantees guarantees, OfferProductPhotos access, UserManager<ApplicationUser> users) : Controller
{
    [AllowAnonymous, HttpGet("/Guarantees/Verify/{token}")]
    public async Task<IActionResult> Verify(string token, string? number)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        var guarantee = await FindPublicRecord(token);
        var model = OfferGuarantees.Verification(guarantee, number, DateOnly.FromDateTime(DateTime.UtcNow));
        if (guarantee is null) Response.StatusCode = 404;
        return View(model);
    }

    [Authorize, HttpGet("/Guarantees/Document/{id:int}")]
    public async Task<IActionResult> Document(int id)
    {
        var guarantee = await db.BidGuarantees.AsNoTracking().SingleOrDefaultAsync(g => g.BidGuaranteeId == id);
        if (guarantee is null || !await access.VisibleBids(User).AnyAsync(b => b.BidId == guarantee.BidId))
            return NotFound();
        var path = guarantees.PathFor(guarantee.StoredFileName);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return System.IO.File.Exists(path)
            ? PhysicalFile(path, guarantee.ContentType, guarantee.OriginalFileName) : NotFound();
    }

    [AllowAnonymous, HttpGet("/Guarantees/Qr/{token}")]
    public async Task<IActionResult> Qr(string token)
    {
        if (await FindPublicRecord(token) is null) return NotFound();
        var url = Url.Action(nameof(Verify), "Guarantees", new { token }, Request.Scheme)!;
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
        using var qr = new SvgQRCode(data);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(Encoding.UTF8.GetBytes(qr.GetGraphic(5)), "image/svg+xml");
    }

    private async Task<BidGuarantee?> FindPublicRecord(string token)
    {
        if (token.Length != 32 || !Guid.TryParseExact(token, "N", out _)) return null;
        var owner = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return await db.BidGuarantees.AsNoTracking()
            .Include(g => g.Bid).ThenInclude(b => b.Invitation).ThenInclude(i => i.Tender)
            .SingleOrDefaultAsync(g => g.Kind == "Bank" && g.VerificationToken == token &&
                (g.Bid.SubmittedAt != null || owner != null && g.Bid.Invitation.SupplierProfile.UserId == owner));
    }

    // Offer product photos (/OfferProductPhotos/{id}).
    [Authorize, HttpGet("/OfferProductPhotos/{id:int}")]
    public async Task<IActionResult> Image(int id)
    {
        if ((await users.GetUserAsync(User))?.IsActive != true) return Forbid();
        var document = await db.BidDocuments.AsNoTracking().SingleOrDefaultAsync(d => d.BidDocumentId == id && d.DocumentType.StartsWith(OfferProductPhotos.Prefix));
        if (document == null || !await access.VisibleBids(User).AnyAsync(b => b.BidId == document.BidId)) return NotFound();
        var path = access.PathFor(document.StoredFileName);
        if (!System.IO.File.Exists(path)) return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return PhysicalFile(path, "image/jpeg");
    }
}
