using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Services.Ai;
using SourcingTenderManagement.Services.Compliance;

namespace SourcingTenderManagement.Controllers;

// AI document review for one submitted offer. Read-only: it never changes the compliance decision.
public partial class SourcingOfficerController
{
    [HttpPost, ValidateAntiForgeryToken]
    [EnableRateLimiting("AiChat")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> AiOfferReview(int id, [FromServices] OfferPreCheck precheck, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        // Same scope as the Offers page: submitted offers on tenders this officer created.
        var bid = await db.Bids.AsNoTracking()
            .Include(b => b.BidItems)
            .Include(b => b.BidDocuments)
            .Include(b => b.Invitation).ThenInclude(i => i.Tender)
                .ThenInclude(t => t.PurchaseRequest)
                .ThenInclude(r => r.PurchaseRequestItems)
            .SingleOrDefaultAsync(b => b.BidId == id
                && b.SubmittedAt != null
                && b.Invitation.Tender.CreatedByUserId == userId, ct);
        if (bid == null) return NotFound(new { error = "This offer is not available in your workspace." });

        var tender = bid.Invitation.Tender;
        try
        {
            var review = await precheck.ReviewAsync(bid, tender, tender.PurchaseRequest.PurchaseRequestItems.ToList(), DateTime.UtcNow, ct);
            return Ok(new
            {
                summary = review.Summary,
                findings = review.Findings,
                hiddenUnverifiable = review.HiddenUnverifiable,
                documentNotes = review.DocumentNotes
            });
        }
        catch (AiLimitReachedException ex)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new { error = ex.Message });
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException ||
                                   (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "The AI review is unavailable right now. The automatic checks above still apply; try again in a moment." });
        }
    }
}
