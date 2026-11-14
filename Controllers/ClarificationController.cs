using System.Data;
using System.ComponentModel.DataAnnotations;
using SourcingTenderManagement.Contracts.TopCommittee;
using SourcingTenderManagement.Services.TopCommittee;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Services;

namespace SourcingTenderManagement.Controllers;

[Authorize(Roles = "Department,SourcingOfficer,Supplier,EvaluationCommittee,SourcingManager,TopCommittee")]
[AutoValidateAntiforgeryToken]
public class ClarificationController(ApplicationDbContext db, UserManager<ApplicationUser> users, ITopCommitteeManagerAnswers committeeAnswers) : Controller
{
    public async Task<IActionResult> Index(int? id, string? direction, string? status,
        string? kind, int? recordId, CancellationToken ct)
    {
        var actor = await users.GetUserAsync(User);
        if (actor?.IsActive != true || !await CanUseInbox(actor.Id, ct)) return Forbid();
        if (User.IsInRole(RoleNames.SourcingManager))
            return RedirectToAction("Clarifications", "SourcingManager", new { id, tenderId = kind == "tender" ? recordId : null });
        var committeeThreads = TopCommitteeClarificationScope.Query(db);
        var threads = db.Clarifications.AsNoTracking().VisibleTo(actor.Id, User.IsInRole(RoleNames.SourcingOfficer));
        if (User.IsInRole(RoleNames.TopCommittee) && !User.IsInRole(RoleNames.SourcingManager))
            threads = threads.Where(c => !committeeThreads.Any(q => q.ClarificationId == c.ClarificationId) &&
                db.CommitteeAssignments.Any(a => a.TenderId == c.TenderId && a.CommitteeType == "Top" && a.UserId == actor.Id));
        if (id.HasValue && !await threads.AnyAsync(c => c.ClarificationId == id, ct)) return NotFound();
        var selected = id.HasValue ? await threads.Where(c => c.ClarificationId == id)
            .Include(c => c.SenderUser).Include(c => c.RecipientUser)
            .Include(c => c.PurchaseRequest).Include(c => c.Tender)
            .Include(c => c.Bid).ThenInclude(b => b!.Invitation).ThenInclude(i => i.Tender)
            .Include(c => c.ClarificationResponses).ThenInclude(r => r.RespondedByUser)
            .SingleAsync(ct) : null;
        ViewBag.Thread = selected;
        ViewBag.CanStartRequestReview = User.IsInRole(RoleNames.SourcingOfficer) &&
            selected?.PurchaseRequest is { Status: "Submitted", ReviewedByUserId: null };
        if (selected != null)
        {
            (kind, recordId) = RecordOf(selected);
            ViewBag.RecordTitle = selected.PurchaseRequest?.Title ?? selected.Tender?.Title ?? selected.Bid?.Invitation.Tender.Title;
            ViewBag.RecordUrl = await RecordUrl(actor.Id, kind, recordId, ct);
        }
        else if (recordId.HasValue)
        {
            ViewBag.RecordUrl = await RecordUrl(actor.Id, kind, recordId, ct);
            if (ViewBag.RecordUrl != null)
                ViewBag.RecordTitle = kind switch
                {
                    "request" => await db.PurchaseRequests.Where(r => r.PurchaseRequestId == recordId).Select(r => r.Title).FirstOrDefaultAsync(ct),
                    "tender" => await db.Tenders.Where(t => t.TenderId == recordId).Select(t => t.Title).FirstOrDefaultAsync(ct),
                    "bid" => await db.Bids.Where(b => b.BidId == recordId).Select(b => b.Invitation.Tender.Title).FirstOrDefaultAsync(ct),
                    _ => null
                };
        }
        if (recordId.HasValue) threads = threads.ForRecord(kind, recordId.Value);
        if (direction == "sent") threads = threads.Where(c => c.SenderUserId == actor.Id);
        if (direction == "received") threads = threads.Where(c => c.RecipientUserId == actor.Id);
        if (status is "Pending" or "Answered" or "Resolved") threads = threads.Where(c => c.Status == status);
        var committeeQuestion = id.HasValue && await TopCommitteeClarificationScope.Query(db)
            .AnyAsync(c => c.ClarificationId == id, ct);
        var assignedReviewer = selected?.PurchaseRequestId is int requestId && User.IsInRole(RoleNames.SourcingOfficer) &&
             await db.PurchaseRequests.AnyAsync(r => r.PurchaseRequestId == requestId &&
                 r.Status != "Draft" && r.ReviewedByUserId == actor.Id, ct);
        var reassigned = selected?.PurchaseRequest is { ReviewedByUserId: not null } request &&
            selected.SenderUserId == request.RequestedByUserId && selected.RecipientUserId == actor.Id &&
            request.ReviewedByUserId != actor.Id;
        ViewBag.CanReply = !reassigned && (committeeQuestion
            ? selected?.RecipientUserId == actor.Id && selected.Status == "Pending"
            : assignedReviewer || selected?.SenderUserId == actor.Id || selected?.RecipientUserId == actor.Id);
        var confirmations = ClarificationResolution.ConfirmedUsers(selected?.ClarificationResponses ?? []);
        ViewBag.HasConfirmed = confirmations.Contains(actor.Id);
        ViewBag.AwaitingConfirmation = confirmations.Count > 0;
        ViewBag.CanResolve = !reassigned && (committeeQuestion
            ? selected?.RecipientUserId == actor.Id && selected.Status == "Answered"
            : selected != null && (assignedReviewer || selected.SenderUserId == actor.Id || selected.RecipientUserId == actor.Id));
        ViewBag.Kind = kind;
        ViewBag.RecordId = recordId;
        ViewBag.Direction = direction;
        ViewBag.Status = status;
        ViewBag.Recipients = recordId.HasValue ? await Recipients(actor, kind, recordId.Value, ct) : [];
        ViewBag.ActorId = actor.Id;
        ViewBag.Records = selected == null && !recordId.HasValue ? await RecordChoices(actor.Id, ct) : [];
        return View(await threads.Include(c => c.SenderUser).Include(c => c.RecipientUser)
            .OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.ClarificationId).ToListAsync(ct));
    }

    private async Task<List<SourcingTenderManagement.Models.ClarificationRecordOption>> RecordChoices(string actorId, CancellationToken ct)
    {
        var officer = User.IsInRole(RoleNames.SourcingOfficer);
        var department = User.IsInRole(RoleNames.Department);
        var supplier = User.IsInRole(RoleNames.Supplier);
        var evaluator = User.IsInRole(RoleNames.EvaluationCommittee);
        var manager = User.IsInRole(RoleNames.SourcingManager);
        var records = new List<SourcingTenderManagement.Models.ClarificationRecordOption>();
        var requests = await db.PurchaseRequests.AsNoTracking().Where(r =>
            (r.Status == "Submitted" || r.Status == "UnderReview" || r.Status == "Returned") &&
            ((department && r.RequestedByUserId == actorId) || (officer && r.Status == "UnderReview" && r.ReviewedByUserId == actorId)))
            .OrderByDescending(r => r.CreatedAt).Select(r => new { r.PurchaseRequestId, r.Title }).ToListAsync(ct);
        records.AddRange(requests.Select(r => new SourcingTenderManagement.Models.ClarificationRecordOption("request", r.PurchaseRequestId, r.Title, $"Purchase request #{r.PurchaseRequestId}")));
        var tenders = db.Tenders.AsNoTracking().Where(t =>
            (officer && t.CreatedByUserId == actorId) ||
            (supplier && t.Invitations.Any(i => i.SupplierProfile.UserId == actorId && (i.Status == "Pending" || i.Status == "Accepted"))) ||
            (evaluator && t.CommitteeAssignments.Any(a => a.CommitteeType == "Evaluation" && a.UserId == actorId)) ||
            (manager && t.Status == "AwaitingManagerReview"));
        var tenderChoices = await tenders.Where(t => t.Status == "Published" || t.Status == "Open" || t.Status == "Closed" || t.Status == "UnderEvaluation" || t.Status == "AwaitingOfficerSummary" || t.Status == "AwaitingManagerReview")
            .OrderByDescending(t => t.CreatedAt).Select(t => new { t.TenderId, t.Title }).ToListAsync(ct);
        records.AddRange(tenderChoices.Select(t => new SourcingTenderManagement.Models.ClarificationRecordOption("tender", t.TenderId, t.Title, $"Tender #{t.TenderId}")));
        var bids = await db.Bids.AsNoTracking().Where(b => b.SubmittedAt != null && b.Status != "Withdrawn" &&
            ((officer && b.Invitation.Tender.CreatedByUserId == actorId) || (supplier && b.Invitation.SupplierProfile.UserId == actorId)))
            .OrderByDescending(b => b.SubmittedAt).Select(b => new { b.BidId, b.Invitation.Tender.Title, b.Invitation.SupplierProfile.CompanyName }).ToListAsync(ct);
        records.AddRange(bids.Select(b => new SourcingTenderManagement.Models.ClarificationRecordOption("bid", b.BidId, b.Title, $"Offer #{b.BidId} · {b.CompanyName}")));
        return records;
    }

    [HttpPost]
    public async Task<IActionResult> Create(string kind, int recordId, string recipient, string? subject,
        string? description, CancellationToken ct)
    {
        var actor = await users.GetUserAsync(User);
        if (actor?.IsActive != true || !await CanUseInbox(actor.Id, ct)) return Forbid();
        subject = subject?.Trim();
        description = description?.Trim();
        if (subject is not { Length: > 0 and <= 200 } || description is not { Length: > 0 and <= 4000 })
            return BadRequest("Enter a subject and question within the displayed limits.");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (!(await Recipients(actor, kind, recordId, ct)).Any(u => u.Id == recipient)) return Forbid();
        var thread = new Clarification
        {
            SenderUserId = actor.Id, RecipientUserId = recipient, Subject = subject,
            Description = description, Status = "Pending", CreatedAt = DateTime.UtcNow,
            PurchaseRequestId = kind == "request" ? recordId : null,
            TenderId = kind == "tender" ? recordId : null,
            BidId = kind == "bid" ? recordId : null
        };
        db.Clarifications.Add(thread);
        await db.SaveChangesAsync(ct);
        Notify(recipient, thread, "New clarification");
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return RedirectToAction(nameof(Index), new { id = thread.ClarificationId, kind, recordId });
    }

    [HttpPost]
    public async Task<IActionResult> Update(int id, string? message, bool resolve, CancellationToken ct)
    {
        var actor = await users.GetUserAsync(User);
        if (actor?.IsActive != true || !await CanUseInbox(actor.Id, ct)) return Forbid();
        var committeeThread = await TopCommitteeClarificationScope.Query(db)
            .SingleOrDefaultAsync(c => c.ClarificationId == id, ct);
        if (committeeThread != null)
        {
            if (!User.IsInRole(RoleNames.SourcingManager) || committeeThread.RecipientUserId != actor.Id) return Forbid();
            try
            {
                if (resolve) await committeeAnswers.ConfirmResolutionAsync(User, id, ct);
                else await committeeAnswers.AnswerAsync(User, id, message ?? "", ct);
            }
            catch (Exception error) when (error is ValidationException or CommitteeStateConflictException)
            {
                TempData["ClarificationError"] = error.Message;
            }
            catch (CommitteeTenderNotFoundException) { return NotFound(); }
            catch (CommitteeAccessDeniedException) { return Forbid(); }

            return RedirectToAction(nameof(Index), new { id });
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var officer = User.IsInRole(RoleNames.SourcingOfficer);
        var query = db.Clarifications.Where(c => c.ClarificationId == id &&
            (c.SenderUserId == actor.Id || c.RecipientUserId == actor.Id ||
             (officer && c.PurchaseRequest != null &&
              c.PurchaseRequest.Status != "Draft" && c.PurchaseRequest.ReviewedByUserId == actor.Id &&
              c.SenderUserId == c.PurchaseRequest.RequestedByUserId)));
        // Serialize changes to a conversation, including confirmations that keep the same status.
        if (await query.ExecuteUpdateAsync(setters => setters.SetProperty(c => c.Status, c => c.Status), ct) == 0)
            return NotFound();
        var thread = await query.AsNoTracking().Include(c => c.PurchaseRequest)
            .Include(c => c.ClarificationResponses).SingleAsync(ct);
        if (thread == null) return NotFound();
        if (User.IsInRole(RoleNames.TopCommittee) && !User.IsInRole(RoleNames.SourcingManager) &&
            !await db.CommitteeAssignments.AnyAsync(a => a.TenderId == thread.TenderId &&
                a.CommitteeType == "Top" && a.UserId == actor.Id, ct))
            return NotFound();
        var request = thread.PurchaseRequest;
        var requestReviewer = request?.ReviewedByUserId;
        if (requestReviewer != null && thread.SenderUserId == request!.RequestedByUserId &&
            thread.RecipientUserId == actor.Id && requestReviewer != actor.Id) return Forbid();
        var other = thread.SenderUserId == actor.Id ? thread.RecipientUserId : thread.SenderUserId;
        if (requestReviewer != null && thread.SenderUserId == request!.RequestedByUserId)
            other = actor.Id == request.RequestedByUserId ? requestReviewer : request.RequestedByUserId;

        if (thread.Status == "Resolved")
        {
            TempData["ClarificationError"] = "This clarification has already been resolved.";
            return RedirectToAction(nameof(Index), new { id });
        }
        var confirmations = ClarificationResolution.ConfirmedUsers(thread.ClarificationResponses);
        if (resolve && confirmations.Contains(actor.Id))
            return RedirectToAction(nameof(Index), new { id });
        message = message?.Trim();
        if (!resolve && (message is not { Length: > 0 and <= 4000 } || message == ClarificationResolution.Confirmation))
        {
            TempData["ClarificationError"] = "Enter a reply of up to 4,000 characters.";
            return RedirectToAction(nameof(Index), new { id });
        }

        var closed = resolve && confirmations.Contains(other) && actor.Id != other;
        var nextStatus = resolve ? (closed ? "Resolved" : thread.Status) : actor.Id != thread.SenderUserId ? "Answered" : "Pending";
        DateTime? resolvedAt = closed ? DateTime.UtcNow : null;
        var updated = await query.ExecuteUpdateAsync(setters => setters
            .SetProperty(c => c.Status, nextStatus).SetProperty(c => c.ResolvedAt, resolvedAt), ct);
        if (updated == 0)
        {
            TempData["ClarificationError"] = "The conversation changed. Review the latest messages and try again.";
            return RedirectToAction(nameof(Index), new { id });
        }
        if (!resolve)
        {
            db.ClarificationResponses.Add(new ClarificationResponse
            {
                ClarificationId = id, RespondedByUserId = actor.Id, Message = message!, CreatedAt = DateTime.UtcNow
            });
            Notify(other, thread, "Clarification reply");
            if (thread.PurchaseRequestId is int replyRequestId)
            {
                var reviewer = await db.PurchaseRequests.Where(r => r.PurchaseRequestId == replyRequestId)
                    .Select(r => r.ReviewedByUserId).SingleAsync(ct);
                if (reviewer != null && reviewer != actor.Id && reviewer != other)
                    Notify(reviewer, thread, "Clarification reply");
            }
        }
        if (resolve)
        {
            db.ClarificationResponses.Add(ClarificationResolution.CreateConfirmation(id, actor.Id));
            Notify(other, thread, closed ? "Clarification resolved" : "Confirm clarification resolution");
            if (thread.PurchaseRequestId is int resolvedRequestId)
            {
                var reviewer = await db.PurchaseRequests.Where(r => r.PurchaseRequestId == resolvedRequestId)
                    .Select(r => r.ReviewedByUserId).SingleAsync(ct);
                if (reviewer != null && reviewer != actor.Id && reviewer != other)
                    Notify(reviewer, thread, closed ? "Clarification resolved" : "Confirm clarification resolution");
            }
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        if (closed && User.IsInRole(RoleNames.SourcingOfficer) && thread.PurchaseRequestId is int requestId &&
            await db.PurchaseRequests.AnyAsync(r => r.PurchaseRequestId == requestId &&
                r.Status == "UnderReview" && r.ReviewedByUserId == actor.Id, ct))
        {
            var open = await db.Clarifications.AnyAsync(c => c.PurchaseRequestId == requestId && c.Status != "Resolved", ct);
            TempData["RequestSuccess"] = open
                ? "Clarification resolved. Other clarifications are still open."
                : "All clarifications resolved. You can now continue the review.";
            return RedirectToAction("Requests", "SourcingOfficer", new { id = requestId });
        }
        return RedirectToAction(nameof(Index), new { id });
    }

    private static (string? Kind, int? Id) RecordOf(Clarification thread) =>
        thread.PurchaseRequestId.HasValue ? ("request", thread.PurchaseRequestId) :
        thread.BidId.HasValue ? ("bid", thread.BidId) :
        thread.TenderId.HasValue ? ("tender", thread.TenderId) : (null, null);

    private async Task<string?> RecordUrl(string actorId, string? kind, int? id, CancellationToken ct)
    {
        if (id is not int recordId) return null;
        if (kind == "request")
        {
            var request = await db.PurchaseRequests.AsNoTracking().SingleOrDefaultAsync(r => r.PurchaseRequestId == recordId, ct);
            if (request == null) return null;
            if (User.IsInRole(RoleNames.Department) && request.RequestedByUserId == actorId)
                return Url.Action("Details", "Department", new { id });
            if (User.IsInRole(RoleNames.SourcingOfficer) &&
                (request.ReviewedByUserId == actorId || (request.ReviewedByUserId == null && request.Status == "Submitted")))
                return Url.Action("Requests", "SourcingOfficer", new { id });
            return null;
        }

        if (kind is not ("bid" or "tender")) return null;
        var bid = kind == "bid" ? await db.Bids.AsNoTracking().Include(b => b.Invitation)
            .ThenInclude(i => i.SupplierProfile).SingleOrDefaultAsync(b => b.BidId == recordId, ct) : null;
        if (kind == "bid" && bid == null) return null;
        var tenderId = bid?.TenderId ?? recordId;
        if (User.IsInRole(RoleNames.SourcingOfficer) &&
            await db.Tenders.AnyAsync(t => t.TenderId == tenderId && t.CreatedByUserId == actorId, ct))
            return bid == null ? Url.Action("Tenders", "SourcingOfficer", new { id = tenderId })
                : Url.Action("Offers", "SourcingOfficer", new { id, tenderId });
        if (User.IsInRole(RoleNames.Supplier))
        {
            if (bid?.Invitation.SupplierProfile.UserId == actorId)
                return Url.Action("Offer", "Supplier", new { id });
            if (bid == null)
            {
                var invitationId = await db.Invitations.Where(i => i.TenderId == tenderId && i.SupplierProfile.UserId == actorId)
                    .OrderByDescending(i => i.InvitationId).Select(i => (int?)i.InvitationId).FirstOrDefaultAsync(ct);
                if (invitationId.HasValue) return Url.Action("Invitation", "Supplier", new { id = invitationId });
            }
        }
        if (User.IsInRole(RoleNames.TopCommittee) && await db.CommitteeAssignments.AnyAsync(a =>
            a.TenderId == tenderId && a.CommitteeType == "Top" && a.UserId == actorId, ct))
            return Url.Action("TenderDetails", "TopCommittee", new { id = tenderId });
        if (User.IsInRole(RoleNames.EvaluationCommittee) && await db.CommitteeAssignments.AnyAsync(a =>
            a.TenderId == tenderId && a.CommitteeType == "Evaluation" && a.UserId == actorId, ct))
            return Url.Action("Evaluations", "EvaluationCommittee", new { id = tenderId });
        if (User.IsInRole(RoleNames.SourcingManager))
        {
            var reportId = await db.EvaluationReports.Where(r => r.TenderId == tenderId && r.Tender.Status == "AwaitingManagerReview" &&
                    (r.Status == "OfficerSubmitted" || r.Status == "CommitteeApproved" || r.Status == "ManagerApproved"))
                .OrderByDescending(r => r.SubmittedAt).ThenByDescending(r => r.EvaluationReportId)
                .Select(r => (int?)r.EvaluationReportId).FirstOrDefaultAsync(ct);
            if (reportId.HasValue) return Url.Action("Submissions", "SourcingManager", new { id = reportId });
        }
        return null;
    }

    private async Task<bool> CanUseInbox(string userId, CancellationToken ct) =>
        !User.IsInRole(RoleNames.Supplier) ||
        await db.SupplierProfiles.AnyAsync(s => s.UserId == userId && s.Status == "Approved", ct);

    private void Notify(string recipient, Clarification thread, string title) => db.Notifications.Add(new Notification
    {
        UserId = recipient, Title = title, Message = thread.Subject,
        RelativeUrl = Url.Action(nameof(Index), "Clarification", new { id = thread.ClarificationId }),
        CreatedAt = DateTime.UtcNow
    });

    private async Task<List<ApplicationUser>> Recipients(ApplicationUser actor, string? kind, int id, CancellationToken ct)
    {
        var allowed = new HashSet<string>();
        var officers = await users.GetUsersInRoleAsync(RoleNames.SourcingOfficer);
        var officerIds = officers.Where(u => u.IsActive).Select(u => u.Id).ToHashSet();
        if (kind == "request")
        {
            var request = await db.PurchaseRequests.AsNoTracking().SingleOrDefaultAsync(r => r.PurchaseRequestId == id, ct);
            if (request?.Status is not ("Submitted" or "UnderReview" or "Returned")) return [];
            if (User.IsInRole(RoleNames.Department) && request.RequestedByUserId == actor.Id)
            {
                if (request.ReviewedByUserId != null) allowed.Add(request.ReviewedByUserId);
                else if (request.Status == "Submitted") allowed.UnionWith(officerIds);
            }
            if (officerIds.Contains(actor.Id) && request.Status == "UnderReview" && request.ReviewedByUserId == actor.Id)
                allowed.Add(request.RequestedByUserId);
        }
        else if (kind is "bid" or "tender")
        {
            var bid = kind == "bid" ? await db.Bids.AsNoTracking().Include(b => b.Invitation)
                .SingleOrDefaultAsync(b => b.BidId == id && b.SubmittedAt != null && b.Status != "Withdrawn", ct) : null;
            if (kind == "bid" && bid == null) return [];
            var tenderId = bid?.TenderId ?? id;
            var tender = await db.Tenders.AsNoTracking().Include(t => t.CommitteeAssignments)
                .SingleOrDefaultAsync(t => t.TenderId == tenderId, ct);
            if (tender == null) return [];
            var owner = tender.CreatedByUserId;
            var isOfficer = owner == actor.Id && officerIds.Contains(actor.Id);

            if (tender.Status is "Published" or "Open" or "Closed" or "UnderEvaluation" || (bid != null && tender.Status == "AwaitingOfficerSummary"))
            {
                var invited = await db.Invitations.Where(i => i.TenderId == tenderId &&
                    (i.Status == "Pending" || i.Status == "Accepted") && i.SupplierProfile.Status == "Approved" &&
                    (bid == null || i.InvitationId == bid.InvitationId))
                    .Select(i => i.SupplierProfile.UserId).ToListAsync(ct);
                var supplierIds = (await users.GetUsersInRoleAsync(RoleNames.Supplier)).Select(u => u.Id).ToHashSet();
                invited.RemoveAll(id => !supplierIds.Contains(id));
                if (isOfficer) allowed.UnionWith(invited);
                if (User.IsInRole(RoleNames.Supplier) && invited.Contains(actor.Id) && officerIds.Contains(owner)) allowed.Add(owner);
            }

            if (bid == null && tender.Status is ("UnderEvaluation" or "AwaitingOfficerSummary" or "AwaitingManagerReview"))
            {
                var evaluators = (await users.GetUsersInRoleAsync(RoleNames.EvaluationCommittee))
                    .Where(u => tender.CommitteeAssignments.Any(a => a.CommitteeType == "Evaluation" && a.UserId == u.Id))
                    .Select(u => u.Id).ToArray();
                if (isOfficer) allowed.UnionWith(evaluators);
                if (evaluators.Contains(actor.Id) && officerIds.Contains(owner)) allowed.Add(owner);

                if (tender.Status == "AwaitingManagerReview")
                {
                    var managers = (await users.GetUsersInRoleAsync(RoleNames.SourcingManager)).Select(u => u.Id).ToArray();
                    if (isOfficer || evaluators.Contains(actor.Id)) allowed.UnionWith(managers);
                    if (managers.Contains(actor.Id))
                    {
                        if (officerIds.Contains(owner)) allowed.Add(owner);
                        allowed.UnionWith(evaluators);
                    }
                }
            }
        }

        allowed.Remove(actor.Id);
        return await db.Users.AsNoTracking().Where(u => u.IsActive && allowed.Contains(u.Id))
            .OrderBy(u => u.FullName).ToListAsync(ct);
    }
}
