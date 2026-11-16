using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Controllers;
public class HomeController(ApplicationDbContext db, ILogger<HomeController> logger) : Controller
{
    public IActionResult Index() => View();
    public IActionResult About() => View();
    public IActionResult HowItWorks() => View();
    public IActionResult Team() => View(TeamMember.Roster);
    public IActionResult InsideTiryaq() => View();
    [HttpGet, AllowAnonymous]
    public IActionResult Contact() => View(new ContactMessageInput());

    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken, RequestSizeLimit(65536)]
    public async Task<IActionResult> Contact(ContactMessageInput input, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(input.Email) && !ContactMessageInput.IsValidAddress(input.Email))
            ModelState.AddModelError(nameof(input.Email), "Enter a valid email address.");
        if (input.Subject?.Any(char.IsControl) == true)
            ModelState.AddModelError(nameof(input.Subject), "Enter a subject on one line.");
        if (input.FullName?.Any(char.IsControl) == true)
            ModelState.AddModelError(nameof(input.FullName), "Enter a name on one line.");
        if (!ModelState.IsValid)
            return View(input);

        var message = new ContactMessage
        {
            UserId = User.Identity?.IsAuthenticated == true ? User.FindFirstValue(ClaimTypes.NameIdentifier) : null,
            FullName = input.FullName?.Trim() ?? "",
            Email = input.Email,
            Subject = input.Subject?.Trim() ?? "",
            Message = input.Message.Trim(),
            SubmittedAt = DateTime.UtcNow,
            Status = ContactMessage.Unread
        };

        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var recent = message.SubmittedAt.AddMinutes(-1);
            var alreadySaved = await db.ContactMessages.AnyAsync(existing => existing.SubmittedAt >= recent
                && existing.UserId == message.UserId && existing.Email == message.Email
                && existing.FullName == message.FullName && existing.Subject == message.Subject
                && existing.Message == message.Message, cancellationToken);

            if (!alreadySaved)
            {
                db.ContactMessages.Add(message);
                await db.SaveChangesAsync(cancellationToken);

                var adminIds = await (
                    from user in db.Users
                    join assignment in db.UserRoles on user.Id equals assignment.UserId
                    join role in db.Roles on assignment.RoleId equals role.Id
                    where user.IsActive && role.Name == RoleNames.Admin
                    select user.Id).ToListAsync(cancellationToken);
                var detailsUrl = Url.Action(nameof(AdminController.ContactMessageDetails), "Admin", new { id = message.ContactMessageId });
                foreach (var adminId in adminIds)
                {
                    db.Notifications.Add(new Notification
                    {
                        UserId = adminId,
                        Title = "New contact message",
                        Message = $"{message.FullName}: {message.Subject}",
                        RelativeUrl = detailsUrl,
                        CreatedAt = message.SubmittedAt
                    });
                }
                await db.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception error) when (error is DbUpdateException or SqlException)
        {
            logger.LogWarning("Saving a contact message failed ({ErrorType}).", error.GetType().Name);
            ModelState.AddModelError("", "Your message could not be saved. Please try again.");
            return View(input);
        }

        TempData["ContactSuccess"] = "Your message has been sent successfully!";
        return RedirectToAction(nameof(Contact), null, null, "contact");
    }
    public IActionResult Error() { Response.StatusCode = 500; return View(); }
}
