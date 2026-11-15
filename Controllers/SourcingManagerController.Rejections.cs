using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Contracts.TopCommittee;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Controllers;

public partial class SourcingManagerController
{
    private IQueryable<Tender> ManagedRejections() => _context.Tenders.AsNoTracking()
        .Include(t => t.TenderApprovals).ThenInclude(a => a.User)
        .Where(t => t.TenderApprovals.Any(a => a.Stage == "Top" && a.Decision == "Rejected") &&
            t.TenderApprovals.Any(a => a.Stage == "Manager" && a.UserId == ManagerId && a.Decision == "Approved"));

    // Keep links from existing notifications working without a standalone rejection page.
    [HttpGet]
    public IActionResult Rejections(int? id) => RedirectToAction(nameof(Submissions), new { rejectedTenderId = id, showRejections = true });

    [HttpPost]
    public async Task<IActionResult> ForwardRejection(int id, [FromServices] ITopCommitteeTenderTransaction coordinator, CancellationToken ct)
    {
        var outcome = await coordinator.ExecuteAsync(id, async (db, token) =>
        {
            var access = await Services.TopCommittee.TopCommitteeClarificationStore.LoadAsync(db, ManagerId, id, token);
            var tender = access.Tender;
            if (!access.ActorIsActive || !access.ActorRoles.Contains(RoleNames.SourcingManager) ||
                tender == null || !access.Managers.Any(m => m.UserId == ManagerId && m.IsActive && m.HasManagerRole)) return 403;
            if (tender.Status != "AwaitingManagerReview" || tender.RejectionForwardedAt.HasValue ||
                !tender.TenderApprovals.Any(a => a.Stage == "Top" && a.Decision == "Rejected")) return 409;
            var officerActive = await db.Users.AnyAsync(u => u.Id == tender.CreatedByUserId && u.IsActive &&
                db.UserRoles.Any(ur => ur.UserId == u.Id && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == RoleNames.SourcingOfficer)), token);
            if (!officerActive) return 409;
            tender.RejectionForwardedAt = DateTime.UtcNow;
            tender.RejectionForwardedByUserId = ManagerId;
            tender.Status = "Closed";
            db.Notifications.Add(new Notification { UserId = tender.CreatedByUserId, Title = "Rejected tender forwarded to you",
                Message = $"Review tender #{id} and prepare a replacement draft. The original rejection is preserved.",
                RelativeUrl = "/SourcingOfficer/Tenders", CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync(token);
            return 200;
        }, ct);
        if (outcome == 403) return Forbid();
        if (outcome == 409) return Conflict("This rejection is unavailable, already forwarded, or its responsible officer is inactive.");
        return RedirectToAction(nameof(Submissions), new { rejectedTenderId = id, showRejections = true });
    }
}
