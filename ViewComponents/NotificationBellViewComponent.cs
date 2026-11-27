using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.TopCommittee;
using Microsoft.Extensions.Primitives;

namespace SourcingTenderManagement.ViewComponents;

// View component: the notification bell in the top bar, with the unread count and latest messages.
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
