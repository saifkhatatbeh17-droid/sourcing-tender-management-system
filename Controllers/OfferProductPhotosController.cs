using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Services;

namespace SourcingTenderManagement.Controllers;

// Serves a product photo from a supplier's offer, but only to people allowed to see that offer.
[Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class OfferProductPhotosController(ApplicationDbContext db, OfferProductPhotos photos, UserManager<ApplicationUser> users) : Controller
{
    [HttpGet("/OfferProductPhotos/{id:int}")]
    public async Task<IActionResult> Image(int id)
    {
        if ((await users.GetUserAsync(User))?.IsActive != true) return Forbid();
        var document = await db.BidDocuments.AsNoTracking().SingleOrDefaultAsync(d => d.BidDocumentId == id && d.DocumentType.StartsWith(OfferProductPhotos.Prefix));
        if (document == null || !await photos.VisibleBids(User).AnyAsync(b => b.BidId == document.BidId)) return NotFound();
        var path = photos.PathFor(document.StoredFileName);
        if (!System.IO.File.Exists(path)) return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return PhysicalFile(path, "image/jpeg");
    }
}
