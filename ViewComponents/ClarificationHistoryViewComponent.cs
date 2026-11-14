using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Services;
using SourcingTenderManagement.Services.TopCommittee;
using System.Security.Claims;

namespace SourcingTenderManagement.ViewComponents;

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
