using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Services;
using SourcingTenderManagement.Services.TopCommittee;
using QRCoder;
using SourcingTenderManagement.Models.TopCommittee;
using Microsoft.Extensions.Primitives;
using SourcingTenderManagement.Services.Integrity;

namespace SourcingTenderManagement.ViewComponents;

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

public sealed class ClarificationHistoryViewComponent(ApplicationDbContext db) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(string kind, int recordId, string? title = null)
    {
        var id = HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (id == null) return Content("");
        var threads = db.Clarifications.AsNoTracking().VisibleTo(id, HttpContext.User.IsInRole(RoleNames.SourcingOfficer));
        threads = threads.ForRecord(kind, recordId);
        if (HttpContext.User.IsInRole(RoleNames.TopCommittee) && !HttpContext.User.IsInRole(RoleNames.SourcingManager))
        {
            var committeeThreads = TopCommitteeClarificationScope.Query(db);
            threads = threads.Where(c => !committeeThreads.Any(q => q.ClarificationId == c.ClarificationId));
        }
        ViewData["Title"] = title ?? "Clarification history";
        ViewData["Kind"] = kind;
        ViewData["RecordId"] = recordId;
        return View(await threads.Include(c => c.SenderUser).Include(c => c.RecipientUser)
            .Include(c => c.ClarificationResponses).ThenInclude(r => r.RespondedByUser)
            .OrderBy(c => c.Status == "Resolved").ThenByDescending(c => c.CreatedAt)
            .ThenByDescending(c => c.ClarificationId).ToListAsync(HttpContext.RequestAborted));
    }
}

public sealed class DetailsQrViewComponent(IConfiguration configuration) : ViewComponent
{
    public IViewComponentResult Invoke(string controller, string action, int id, string label, bool compact = false)
    {
        var detailsUrl = Url.Action(action, controller, new { id });
        if (detailsUrl is null)
            return Content(string.Empty);

        var origin = new Uri($"{Request.Scheme}://{Request.Host.ToUriComponent()}");
        if (Uri.TryCreate(configuration["Links:PublicOrigin"], UriKind.Absolute, out var publicOrigin)
            && publicOrigin.Scheme is "http" or "https"
            && publicOrigin.Host.Length > 0 && publicOrigin.UserInfo.Length == 0
            && publicOrigin.AbsolutePath == "/" && publicOrigin.Query.Length == 0 && publicOrigin.Fragment.Length == 0)
        {
            origin = publicOrigin;
        }

        var scanUrl = new Uri(origin, detailsUrl).AbsoluteUri;
        using var data = QRCodeGenerator.GenerateQrCode(scanUrl, QRCodeGenerator.ECCLevel.M);
        using var qr = new PngByteQRCode(data);
        var imageData = "data:image/png;base64," + Convert.ToBase64String(qr.GetGraphic(pixelsPerModule: 8));

        return View(new DetailsQrViewModel(label, detailsUrl, scanUrl, imageData, origin.IsLoopback, compact));
    }
}

public sealed record DetailsQrViewModel(string Label, string DetailsUrl, string ScanUrl, string ImageData, bool IsLoopback, bool Compact = false);

public class NotificationBellViewComponent(ApplicationDbContext db) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var userId = HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var notifications = db.Notifications.AsNoTracking().Where(n => n.UserId == userId);
        var workspace = ViewContext.ViewData["WorkspaceController"]?.ToString()
            ?? RouteData.Values["controller"]?.ToString();
        if (workspace == "TopCommittee" && HttpContext.User.IsInRole(RoleNames.TopCommittee))
        {
            const int pageSize = 10;
            var total = await notifications.CountAsync();
            var pages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
            var page = int.TryParse(HttpContext.Request.Query["notificationPage"], out var requested)
                ? Math.Clamp(requested, 1, pages) : 1;
            string PageUrl(int number)
            {
                var query = HttpContext.Request.Query.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
                query["notificationPage"] = new StringValues(number.ToString(System.Globalization.CultureInfo.InvariantCulture));
                return $"{HttpContext.Request.PathBase}{HttpContext.Request.Path}{QueryString.Create(query)}";
            }
            return View("~/Views/TopCommittee/Components/NotificationBell/Default.cshtml", new CommitteeNotificationBellViewModel
            {
                Total = total, Page = page, Pages = pages,
                UnreadCount = await notifications.CountAsync(n => !n.IsRead),
                Items = await notifications.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.NotificationId)
                    .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(),
                ReturnUrl = PageUrl(page), PreviousUrl = page > 1 ? PageUrl(page - 1) : null,
                NextUrl = page < pages ? PageUrl(page + 1) : null,
                OpenOnLoad = HttpContext.Request.Query.ContainsKey("notificationPage")
            });
        }
        return View(new NotificationBellViewModel
        {
            UnreadCount = await notifications.CountAsync(n => !n.IsRead),
            Items = await notifications.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.NotificationId).Take(10).ToListAsync()
        });
    }
}

public sealed class OfferGuaranteesViewComponent(ApplicationDbContext db, OfferProductPhotos access) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(int bidId)
    {
        if (!await access.VisibleBids(UserClaimsPrincipal).AnyAsync(b => b.BidId == bidId))
            return Content("");
        var guarantees = await db.BidGuarantees.AsNoTracking().Where(g => g.BidId == bidId)
            .OrderBy(g => g.Kind).ToListAsync();
        return View(guarantees);
    }
}

public sealed class OfferProductPhotosViewComponent(OfferProductPhotos photos) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(int bidId) => View(await photos.ListAsync(bidId, HttpContext.User));
}

/// <summary>One line about the suppliers on a tender. Null means no two suppliers look linked.</summary>
public sealed record SupplierCheck(string Level, int Pairs, IReadOnlyList<string> Names, IReadOnlyList<string> Reasons, string Url);

public sealed record TenderIntegrityModel(int TenderId, SupplierCheck? Suppliers);

/// <summary>
/// Compact "Integrity checks" card for a tender page: whether any suppliers on the tender look linked, with a link to the
/// full supplier map on AI Intelligence. Callers place it only on pages already authorised for the tender.
/// </summary>
public sealed class TenderIntegrityViewComponent(SupplierLinks links) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(int tenderId, string workspace)
    {
        var found = await links.ForTenderAsync(tenderId, HttpContext.RequestAborted);
        if (found.Count == 0) return View(new TenderIntegrityModel(tenderId, null));

        var top = found[0];
        var names = found.SelectMany(l => new[] { l.NameA, l.NameB }).Distinct().ToList();
        var reasons = found.SelectMany(l => l.Evidence).Select(e => e.Kind).Distinct().Take(3).Select(SupplierLinks.KindLabel).ToList();
        var suppliers = new SupplierCheck(
            found.Any(l => l.Level == SupplierLinks.High) ? SupplierLinks.High : SupplierLinks.Medium,
            found.Count, names, reasons,
            $"/{workspace}/AiIntelligence?pair={top.SupplierA}-{top.SupplierB}&from={tenderId}#supplier-links");
        return View(new TenderIntegrityModel(tenderId, suppliers));
    }
}
