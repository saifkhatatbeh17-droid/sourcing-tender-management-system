using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;

namespace SourcingTenderManagement.ViewComponents;

// View component (a reusable piece of a page): the celebration pop-up a supplier sees when their offer wins.
public sealed record AwardCelebrationViewModel(int NotificationId, int BidId, int TenderId, string TenderTitle,
    string CompanyName, decimal Total, string Currency, DateTime? AwardedAt, int MoreCount);

/// <summary>
/// Shows the sealed-envelope award popup to a supplier while their "offer selected" notification is unread.
/// The popup clears once the supplier opens the offer or closes it (both mark the notification read).
/// </summary>
public partial class AwardCelebrationViewComponent(ApplicationDbContext db, IWebHostEnvironment env) : ViewComponent
{
    // Must match the winner notification title written in TopCommitteeNotificationEvents.DecisionAsync.
    public const string WinTitle = "Your offer was selected";

    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (!HttpContext.User.IsInRole(RoleNames.Supplier)) return Content(string.Empty);
        var userId = HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Content(string.Empty);

        // Development-only preview with sample data (?awardPreview=1); id 0 matches no notification, so nothing is written.
        if (env.IsDevelopment() && HttpContext.Request.Query.ContainsKey("awardPreview"))
            return View(new AwardCelebrationViewModel(0, 1042, 318, "Cold-chain vaccine storage units",
                "Preview Supplier Co.", 184250.75m, "JOD", DateTime.UtcNow, 0));

        var wins = await db.Notifications.AsNoTracking()
            .Where(n => n.UserId == userId && !n.IsRead && n.Title == WinTitle)
            .OrderBy(n => n.CreatedAt).ThenBy(n => n.NotificationId)
            .Select(n => new { n.NotificationId, n.RelativeUrl })
            .Take(20).ToListAsync();

        foreach (var win in wins)
        {
            var match = OfferUrl().Match(win.RelativeUrl ?? "");
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out var bidId)) continue;
            var award = await db.Bids.AsNoTracking()
                .Where(b => b.BidId == bidId && b.Status == "Accepted" && b.Invitation.SupplierProfile.UserId == userId)
                .Select(b => new
                {
                    b.BidId, b.Currency, b.TaxAmount, b.AdditionalCosts,
                    Items = b.BidItems.Sum(i => i.Quantity * i.UnitPrice),
                    b.Invitation.SupplierProfile.CompanyName,
                    b.Invitation.Tender.TenderId, b.Invitation.Tender.Title, b.Invitation.Tender.AwardedAt
                }).SingleOrDefaultAsync();
            if (award == null) continue;
            return View(new AwardCelebrationViewModel(win.NotificationId, award.BidId, award.TenderId, award.Title,
                award.CompanyName, award.Items + award.TaxAmount + award.AdditionalCosts, award.Currency,
                award.AwardedAt, wins.Count - 1));
        }
        return Content(string.Empty);
    }

    [GeneratedRegex("^/Supplier/Offer/([1-9][0-9]*)$", RegexOptions.CultureInvariant)]
    private static partial Regex OfferUrl();
}
