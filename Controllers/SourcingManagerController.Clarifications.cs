using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Controllers;

// Sourcing Manager clarifications: ask, reply to and resolve questions about a submission.
public partial class SourcingManagerController
{
    // GET /SourcingManager/Clarifications: Shows the "Clarifications" page.
    [HttpGet]
    public IActionResult Clarifications(int? tenderId, int? id)
    {
        Clarification? selected = null;
        if (id.HasValue)
        {
            selected = _context.Clarifications.Find(id.Value);

            if (selected == null || (selected.SenderUserId != ManagerId && selected.RecipientUserId != ManagerId))
            {
                return NotFound();
            }
        }

        var conversations = _context.Clarifications
            .Where(c => c.SenderUserId == ManagerId || c.RecipientUserId == ManagerId)
            .Include(c => c.SenderUser)
            .Include(c => c.RecipientUser)
            .Include(c => c.Tender)
            .Include(c => c.Bid).ThenInclude(b => b!.Invitation).ThenInclude(i => i.Tender)
            .Include(c => c.PurchaseRequest)
            .Include(c => c.ClarificationResponses.OrderBy(r => r.CreatedAt))
                .ThenInclude(r => r.RespondedByUser)
            .OrderByDescending(c => c.CreatedAt).ToList();
        if (selected != null && !conversations.Contains(selected))
        {
            return NotFound();
        }

        var committeeAssignments = EligibleClarificationRecipients()
            .AsNoTracking()
            .Include(assignment => assignment.Tender)
            .Include(assignment => assignment.User)
            .OrderBy(assignment => assignment.Tender.Title)
            .ThenBy(assignment => assignment.Position)
            .ToList();

        var tenders = committeeAssignments
            .Select(assignment => assignment.Tender)
            .DistinctBy(tender => tender.TenderId)
            .ToList();

        var selectedTenderId = tenderId;
        if (!selectedTenderId.HasValue && tenders.Count > 0)
        {
            selectedTenderId = tenders[0].TenderId;
        }

        var recipients = committeeAssignments
            .Where(assignment => assignment.TenderId == selectedTenderId)
            .ToList();

        var committeeQuestion = selected != null
            && SourcingTenderManagement.Services.TopCommittee.TopCommitteeClarificationScope.Query(_context)
            .Any(c => c.ClarificationId == selected.ClarificationId);
        var canReply = selected != null && selected.Status != "Resolved" &&
            (!committeeQuestion || (selected.RecipientUserId == ManagerId && selected.Status == "Pending"));
        var confirmations = SourcingTenderManagement.Services.ClarificationResolution.ConfirmedUsers(selected?.ClarificationResponses
            ?? []);
        ViewBag.HasConfirmed = confirmations.Contains(ManagerId);
        ViewBag.CanResolve = selected != null && selected.Status != "Resolved" && !confirmations.Contains(ManagerId) &&
            (!committeeQuestion || (selected.RecipientUserId == ManagerId && selected.Status == "Answered"));

        var page = new ManagerClarificationPage(
            tenders, selectedTenderId, recipients, conversations, selected, ManagerId, canReply);
        return View(page);
    }

    // POST /SourcingManager/CreateClarification: Create clarification. Runs when its form or button is submitted.
    [HttpPost]
    public IActionResult CreateClarification(ManagerClarificationInput input)
    {
        if (!ModelState.IsValid)
        {
            return ClarificationMessage("Enter a subject, description, and an eligible committee member.", input.TenderId);
        }

        using var transaction = _context.Database.BeginTransaction(IsolationLevel.RepeatableRead);
        var recipientAssignment = EligibleClarificationRecipients().SingleOrDefault(assignment =>
            assignment.TenderId == input.TenderId && assignment.UserId == input.RecipientId);
        if (recipientAssignment == null)
        {
            return ClarificationMessage("The recipient is no longer eligible for this tender.", input.TenderId);
        }

        var clarification = new Clarification
        {
            TenderId = input.TenderId,
            SenderUserId = ManagerId,
            RecipientUserId = input.RecipientId,
            Subject = input.Subject.Trim(),
            Description = input.Description.Trim(),
            Status = "Pending",
            CreatedAt = DateTime.UtcNow
        };
        _context.Clarifications.Add(clarification);
        _context.SaveChanges();
        _context.Notifications.Add(new Notification
        {
            UserId = clarification.RecipientUserId,
            Title = "New clarification",
            Message = "Open the conversation to respond.",
            RelativeUrl = Url.Action("Index", "Clarification", new { id = clarification.ClarificationId }),
            CreatedAt = DateTime.UtcNow
        });
        _context.SaveChanges();
        transaction.Commit();

        return ClarificationMessage("Question sent.", input.TenderId, clarification.ClarificationId);
    }

    // POST /SourcingManager/ReplyToClarification: Reply to clarification. Runs when its form or button is submitted.
    [HttpPost]
    public IActionResult ReplyToClarification(ManagerClarificationReply input)
    {
        if (!ModelState.IsValid
            || input.Message.Trim() == SourcingTenderManagement.Services.ClarificationResolution.Confirmation)
        {
            return ClarificationMessage("Enter a response of up to 4,000 characters.", id: input.Id);
        }

        // Serializable is the strictest transaction setting: nobody else can change the rows read here until it finishes.
        using var transaction = _context.Database.BeginTransaction(IsolationLevel.Serializable);
        var clarification = _context.Clarifications.Find(input.Id);

        if (clarification == null || clarification.RecipientUserId != ManagerId)
        {
            return NotFound();
        }

        var tenderId = clarification.TenderId;
        if (clarification.BidId.HasValue)
        {
            var bid = _context.Bids.Find(clarification.BidId.Value);
            tenderId = bid?.TenderId;
        }

        var senderAssignment = EligibleClarificationRecipients().SingleOrDefault(assignment =>
            assignment.TenderId == tenderId && assignment.UserId == clarification.SenderUserId);
        if (senderAssignment == null)
        {
            return ClarificationMessage("This tender is no longer in a stage that allows a reply to this sender.", id: input.Id);
        }

        if (clarification.Status != "Pending")
        {
            return ClarificationMessage("This question has already been answered or resolved.", id: input.Id);
        }

        clarification.Status = "Answered";

        var response = new ClarificationResponse
        {
            ClarificationId = clarification.ClarificationId,
            RespondedByUserId = ManagerId,
            Message = input.Message.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        _context.ClarificationResponses.Add(response);
        NotifyCommittee(senderAssignment, "Clarification answered", senderAssignment.TenderId);

        _context.SaveChanges();
        transaction.Commit();

        return ClarificationMessage("Response sent.", id: input.Id);
    }

    // POST /SourcingManager/ResolveClarification: Resolve clarification. Runs when its form or button is submitted.
    [HttpPost]
    public IActionResult ResolveClarification(int id)
    {
        // Serializable is the strictest transaction setting: nobody else can change the rows read here until it finishes.
        using var transaction = _context.Database.BeginTransaction(IsolationLevel.Serializable);

        var clarification = _context.Clarifications.Find(id);

        if (clarification == null || clarification.SenderUserId != ManagerId || clarification.Status != "Answered")
        {
            return NotFound();
        }

        return RedirectToAction("Index", "Clarification", new { id });
    }

    private IQueryable<CommitteeAssignment> EligibleClarificationRecipients()
    {
        var committeeRoles =
            from membership in _context.UserRoles
            join role in _context.Roles on membership.RoleId equals role.Id
            select new { membership.UserId, RoleName = role.Name };

        var evaluationMemberIds = committeeRoles
            .Where(member => member.RoleName == RoleNames.EvaluationCommittee ||
                RoleNames.EvaluationPositions.Contains(member.RoleName!))
            .Select(member => member.UserId);

        var topCommitteeMemberIds = committeeRoles
            .Where(member => member.RoleName == RoleNames.TopCommittee ||
                RoleNames.TopPositions.Contains(member.RoleName!))
            .Select(member => member.UserId);

        var otherActiveMembers = _context.CommitteeAssignments
            .Where(assignment => assignment.UserId != ManagerId && assignment.User.IsActive);

        return otherActiveMembers.Where(assignment =>
            (assignment.CommitteeType == "Evaluation" &&
                (assignment.Tender.Status == "UnderEvaluation" ||
                 assignment.Tender.Status == AwaitingManagerReview) &&
                evaluationMemberIds.Contains(assignment.UserId)) ||
            (assignment.CommitteeType == "Top" &&
                assignment.Tender.Status == AwaitingCommitteeDecision &&
                topCommitteeMemberIds.Contains(assignment.UserId)));
    }

    private void NotifyCommittee(CommitteeAssignment assignment, string title, int tenderId)
    {
        var controller = "EvaluationCommittee";
        if (assignment.CommitteeType == "Top")
        {
            controller = "TopCommittee";
        }

        Notify(assignment.UserId, title, $"A clarification for tender #{tenderId} was updated.", controller, "Clarifications");
    }

    private IActionResult ClarificationMessage(string message, int? tenderId = null, int? id = null)
    {
        TempData["ManagerMessage"] = message;
        return RedirectToAction(nameof(Clarifications), new { tenderId, id });
    }
}
