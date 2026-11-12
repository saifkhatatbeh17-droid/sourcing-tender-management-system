using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models;

namespace SourcingTenderManagement.ViewComponents;

public class NotificationBellViewComponent(ApplicationDbContext db) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var userId = HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var notifications = db.Notifications.AsNoTracking().Where(n => n.UserId == userId);
        return View(new NotificationBellViewModel
        {
            UnreadCount = await notifications.CountAsync(n => !n.IsRead),
            Items = await notifications.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.NotificationId).Take(10).ToListAsync()
        });
    }
}
