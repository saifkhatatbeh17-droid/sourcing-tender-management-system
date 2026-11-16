using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Controllers;

public partial class AdminController
{
    [HttpGet]
    public async Task<IActionResult> ContactMessages(string? search, string? status, int page = 1,
        CancellationToken cancellationToken = default)
    {
        const int pageSize = 7;
        search = search?.Trim();
        if (search?.Length > 254) search = search[..254];
        status = status is ContactMessage.Unread or ContactMessage.Read ? status : null;
        var query = db.ContactMessages.AsNoTracking();
        if (!string.IsNullOrEmpty(search))
            query = query.Where(message => message.FullName.Contains(search)
                || message.Email.Contains(search) || message.Subject.Contains(search));
        if (status == ContactMessage.Unread)
            query = query.Where(message => message.Status == ContactMessage.Unread);
        if (status == ContactMessage.Read)
            query = query.Where(message => message.Status != ContactMessage.Unread);

        var total = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling((double)total / pageSize));
        page = Math.Clamp(page, 1, totalPages);
        var messages = await query.OrderByDescending(message => message.SubmittedAt)
            .ThenByDescending(message => message.ContactMessageId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);

        return View(new ContactMessagesPage(messages, search, status, page, totalPages));
    }

    [HttpGet]
    public async Task<IActionResult> ContactMessageDetails(int id, CancellationToken cancellationToken)
    {
        ViewData["ActivePage"] = nameof(ContactMessages);
        var message = await db.ContactMessages.AsNoTracking()
            .SingleOrDefaultAsync(message => message.ContactMessageId == id, cancellationToken);
        return message == null ? NotFound() : View(message);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkContactMessageRead(int id, CancellationToken cancellationToken)
    {
        var adminId = users.GetUserId(User);
        if (adminId == null) return Forbid();

        var message = await db.ContactMessages.FindAsync([id], cancellationToken);
        if (message == null) return NotFound();

        if (message.IsUnread) message.Status = ContactMessage.Read;
        var detailsUrl = Url.Action(nameof(ContactMessageDetails), new { id });
        var relatedNotifications = await db.Notifications.Where(notification =>
            notification.UserId == adminId && !notification.IsRead && notification.RelativeUrl == detailsUrl)
            .ToListAsync(cancellationToken);
        foreach (var notification in relatedNotifications)
            notification.IsRead = true;

        await db.SaveChangesAsync(cancellationToken);
        TempData["ContactMessageFeedback"] = "Message marked as read.";
        return RedirectToAction(nameof(ContactMessageDetails), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> OpenContactNotification(int id, CancellationToken cancellationToken)
    {
        var adminId = users.GetUserId(User);
        if (adminId == null) return Forbid();

        var notification = await db.Notifications.SingleOrDefaultAsync(item =>
            item.NotificationId == id && item.UserId == adminId, cancellationToken);
        var detailsPrefix = Url.Action(nameof(ContactMessageDetails), "Admin", new { id = "" }) + "/";
        if (notification?.RelativeUrl is not string url || !Url.IsLocalUrl(url)
            || !url.StartsWith(detailsPrefix, StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(url.AsSpan(detailsPrefix.Length), out var messageId) || messageId <= 0)
            return NotFound();

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            await db.SaveChangesAsync(cancellationToken);
        }
        return RedirectToAction(nameof(ContactMessageDetails), new { id = messageId });
    }
}
