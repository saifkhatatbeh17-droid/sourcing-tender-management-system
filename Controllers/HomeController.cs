using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;
namespace SourcingTenderManagement.Controllers;
public class HomeController(ApplicationDbContext db, UserManager<ApplicationUser> users) : Controller
{
    public IActionResult Index() => View();
    public IActionResult About() => View();
    public IActionResult HowItWorks() => View();
    [HttpGet]
    public IActionResult Contact() => View(new ContactMessageInputModel());

    [HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("ContactMessage"), RequestSizeLimit(64_000)]
    public async Task<IActionResult> Contact(ContactMessageInputModel input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(input);

        var submittedAt = DateTime.UtcNow;
        var message = new ContactMessage
        {
            UserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
            FullName = input.FullName.Trim(),
            Email = input.Email.Trim(),
            Subject = input.Subject.Trim(),
            Message = input.Message.Trim(),
            SubmittedAt = submittedAt,
            Status = "Unread"
        };
        var admins = await users.GetUsersInRoleAsync(RoleNames.Admin);
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            db.ContactMessages.Add(message);
            await db.SaveChangesAsync(cancellationToken);
            foreach (var admin in admins.Where(user => user.IsActive))
            {
                db.Notifications.Add(new Notification
                {
                    UserId = admin.Id,
                    Title = $"Contact message: {message.Subject}",
                    Message = $"{message.FullName} submitted this message at {submittedAt:yyyy-MM-dd HH:mm} UTC.",
                    RelativeUrl = message.AdminDetailsUrl,
                    CreatedAt = submittedAt
                });
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        TempData["ContactSuccess"] = "Your message was received.";
        return RedirectToAction(nameof(Contact));
    }
    public IActionResult Error() { Response.StatusCode = 500; return View(); }
}
