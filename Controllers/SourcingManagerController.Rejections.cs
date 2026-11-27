using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Contracts.TopCommittee;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Controllers;

// Sourcing Manager handling of tenders the CEO & Chiefs rejected or asked to edit.
public partial class SourcingManagerController
{
    private IQueryable<Tender> ManagedRejections() => _context.Tenders.AsNoTracking()
        .Include(t => t.TenderApprovals).ThenInclude(a => a.User)
        .Where(t => t.TenderApprovals.Any(a => a.Stage == "Top" && a.Decision == "Rejected") &&
            t.TenderApprovals.Any(a => a.Stage == "Manager" && a.UserId == ManagerId && a.Decision == "Approved"));

    private IQueryable<Tender> ManagedTopEditRequests() => _context.Tenders.AsNoTracking()
        .AsSplitQuery()
        .Include(t => t.EvaluationReport).ThenInclude(r => r!.Bid)
            .ThenInclude(b => b!.Invitation)
            .ThenInclude(i => i.SupplierProfile)
        .Include(t => t.EvaluationReport).ThenInclude(r => r!.Bid).ThenInclude(b => b!.BidItems)
        .Include(t => t.TenderApprovals)
        .Where(t => t.TopEditState != null &&
            t.TenderApprovals.Any(a => a.Stage == "Manager" && a.UserId == ManagerId && a.Decision == "Approved"));

    // Keep links from existing notifications working without a standalone rejection page.
    [HttpGet]
    public IActionResult Rejections(int? id) => RedirectToAction(nameof(Submissions), new { rejectedTenderId = id, showRejections = true });

    // POST /SourcingManager/ForwardRejection: Forward rejection. Runs when its form or button is submitted.
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
                Message = $"Review tender #{id} and prepare a replacement tender. The original rejection is preserved.",
                RelativeUrl = $"/SourcingOfficer/Offers?tenderId={id}", CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync(token);
            return 200;
        }, ct);
        if (outcome == 403) return Forbid();
        if (outcome == 409) return Conflict("This rejection is unavailable, already forwarded, or its responsible officer is inactive.");
        return RedirectToAction(nameof(Submissions), new { rejectedTenderId = id, showRejections = true });
    }

    // POST /SourcingManager/AnswerTopEditRequest: Answer top edit request. Runs when its form or button is submitted.
    [HttpPost]
    public async Task<IActionResult> AnswerTopEditRequest(
        int id,
        string? answerNotes,
        [FromServices] ITopCommitteeTenderTransaction coordinator,
        CancellationToken ct)
    {
        var trimmed = answerNotes?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length > 4000)
        {
            TempData["ManagerMessage"] = "Enter your answer (1–4,000 characters) before returning the tender to the CEO & Chiefs.";
            return RedirectToAction(nameof(Submissions), new { rejectedTenderId = id, showRejections = true });
        }

        var (status, errorMsg) = await coordinator.ExecuteAsync(id, async (db, token) =>
        {
            var access = await Services.TopCommittee.TopCommitteeClarificationStore.LoadAsync(db, ManagerId, id, token);
            var tender = access.Tender;
            if (!access.ActorIsActive || !access.ActorRoles.Contains(RoleNames.SourcingManager) ||
                tender == null || !access.Managers.Any(m => m.UserId == ManagerId && m.IsActive && m.HasManagerRole))
                return (403, null);

            if (tender.Status != "AwaitingManagerReview"
                || tender.TopEditState is not ("WithManager" or "OfficerAnswered"))
                return (409, "This tender is not currently awaiting your edit response.");

            var hasOpenClarifications = await db.Clarifications.AnyAsync(c =>
                c.Status != "Resolved" && (c.TenderId == id || (c.Bid != null && c.Bid.TenderId == id)), token);
            if (hasOpenClarifications)
                return (400, "Resolve open clarifications before returning the tender to the CEO & Chiefs.");

            var report = await db.EvaluationReports.SingleOrDefaultAsync(r => r.TenderId == id, token);
            if (report == null || !report.RecommendedBidId.HasValue)
                return (400, "An eligible recommended offer is required before returning to the CEO & Chiefs.");

            var winningOffer = await db.Bids
                .Include(b => b.Invitation).ThenInclude(i => i.SupplierProfile)
                .Include(b => b.BidItems)
                .SingleOrDefaultAsync(b => b.TenderId == id && b.BidId == report.RecommendedBidId.Value &&
                    b.SubmittedAt != null && b.ComplianceStatus == "Compliant" &&
                    b.Status != "Draft" && b.Status != "Withdrawn" && b.Status != "Rejected" && b.Status != "NonCompliant", token);
            if (winningOffer == null)
                return (400, "The nominated offer is no longer eligible. Push back to the Sourcing Officer to select a compliant offer.");

            var topMembers = access.Members.Where(m => m.IsEligible).ToList();
            if (topMembers.Count != 4)
                return (400, "Four active assigned CEO & Chiefs members are required before returning the tender.");

            await db.Entry(tender).Reference(t => t.PurchaseRequest).Query()
                .Include(r => r.Department)
                .Include(r => r.PurchaseRequestItems)
                .LoadAsync(token);
            await db.Entry(tender).Collection(t => t.OfferExtensions).LoadAsync(token);

            var existingTopVotes = tender.TenderApprovals.Where(a => a.Stage == "Top" && a.Decision != "Rejected")
                .ToList();
            if (existingTopVotes.Count > 0)
                db.TenderApprovals.RemoveRange(existingTopVotes);

            var cycleLog = $"CEO & Chiefs requested edit: {tender.TopEditRequestNotes}\n" +
                           (!string.IsNullOrWhiteSpace(tender.TopEditOfficerNotes)
                               ? $"Sourcing Officer update: {tender.TopEditOfficerNotes}\n"
                               : "") +
                           $"Sourcing Manager answer: {trimmed}";

            report.ManagerSummary = Services.Reports.ManagerReferralContext.Describe(tender, winningOffer)
                + "\n\n## Manager assessment & edit response\n" + cycleLog;
            report.Status = ManagerApproved;

            var managerApproval = tender.TenderApprovals.SingleOrDefault(a => a.Stage == "Manager"
                && a.UserId == ManagerId);
            if (managerApproval != null)
            {
                managerApproval.Decision = "Approved";
                managerApproval.Notes = trimmed;
                managerApproval.DecidedAt = DateTime.UtcNow;
            }

            tender.TopEditManagerNotes = trimmed;
            tender.TopEditState = null;
            tender.Status = AwaitingCommitteeDecision;

            foreach (var member in topMembers)
            {
                db.Notifications.Add(new Notification
                {
                    UserId = member.UserId,
                    Title = "Edits answered — tender ready for decision",
                    Message = $"Sourcing Manager answered the edit request on Tender #{id} (nominated offer #{winningOffer.BidId} " +
                            $"· {winningOffer.Invitation.SupplierProfile.CompanyName}). Please record your decision.",
                    RelativeUrl = $"/TopCommittee/TenderDetails/{id}",
                    CreatedAt = DateTime.UtcNow
                });
            }

            await db.SaveChangesAsync(token);
            return (200, (string?)null);
        }, ct);

        if (status == 403) return Forbid();
        if (status != 200)
        {
            TempData["ManagerMessage"] = errorMsg ?? "Could not return this tender right now.";
            return RedirectToAction(nameof(Submissions), new { rejectedTenderId = id, showRejections = true });
        }

        TempData["ManagerMessage"] = $"Answered and returned Tender #{id} to the CEO & Chiefs for voting.";
        return RedirectToAction(nameof(Submissions));
    }

    // POST /SourcingManager/PushTopEditToOfficer: Push top edit to officer. Runs when its form or button is submitted.
    [HttpPost]
    public async Task<IActionResult> PushTopEditToOfficer(
        int id,
        string? managerNotes,
        [FromServices] ITopCommitteeTenderTransaction coordinator,
        CancellationToken ct)
    {
        var trimmed = managerNotes?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length > 4000)
        {
            TempData["ManagerMessage"] = "Enter instructions (1–4,000 characters) before pushing back to the Sourcing Officer.";
            return RedirectToAction(nameof(Submissions), new { rejectedTenderId = id, showRejections = true });
        }

        var (status, errorMsg) = await coordinator.ExecuteAsync(id, async (db, token) =>
        {
            var access = await Services.TopCommittee.TopCommitteeClarificationStore.LoadAsync(db, ManagerId, id, token);
            var tender = access.Tender;
            if (!access.ActorIsActive || !access.ActorRoles.Contains(RoleNames.SourcingManager) ||
                tender == null || !access.Managers.Any(m => m.UserId == ManagerId && m.IsActive && m.HasManagerRole))
                return (403, null);

            if (tender.Status != "AwaitingManagerReview"
                || tender.TopEditState is not ("WithManager" or "OfficerAnswered"))
                return (409, "This edit request is not currently awaiting your action.");

            var officerActive = await db.Users.AnyAsync(u => u.Id == tender.CreatedByUserId && u.IsActive &&
                db.UserRoles.Any(ur => ur.UserId == u.Id && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == RoleNames.SourcingOfficer)), token);
            if (!officerActive)
                return (409, "The responsible Sourcing Officer account is inactive.");

            var report = await db.EvaluationReports.SingleOrDefaultAsync(r => r.TenderId == id, token);
            if (report != null)
                report.Status = ReturnedForReview;

            tender.TopEditManagerNotes = trimmed;
            tender.TopEditState = "WithOfficer";
            tender.Status = "AwaitingOfficerSummary";

            db.Notifications.Add(new Notification
            {
                UserId = tender.CreatedByUserId,
                Title = "CEO & Chiefs edit request pushed to you",
                Message = $"Tender #{id}: Sourcing Manager forwarded an edit request from CEO & Chiefs. Answer and return " +
                        $"to Manager, or choose another compliant offer instead.",
                RelativeUrl = $"/SourcingOfficer/Offers?tenderId={id}",
                CreatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync(token);
            return (200, (string?)null);
        }, ct);

        if (status == 403) return Forbid();
        if (status != 200)
        {
            TempData["ManagerMessage"] = errorMsg ?? "Could not push this edit request to the Sourcing Officer.";
            return RedirectToAction(nameof(Submissions), new { rejectedTenderId = id, showRejections = true });
        }

        TempData["ManagerMessage"] = $"Pushed Tender #{id} back to the Sourcing Officer to answer or select another offer.";
        return RedirectToAction(nameof(Submissions));
    }
}
