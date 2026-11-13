using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Controllers;

public partial class SourcingManagerController
{
    [HttpGet]
    public IActionResult Clarifications(int? tenderId, int? id)
    {
        var conversations = _context.Clarifications.AsNoTracking()
            .Where(c => c.SenderUserId == ManagerId || c.RecipientUserId == ManagerId)
            .Include(c => c.SenderUser)
            .Include(c => c.RecipientUser)
            .Include(c => c.Tender)
            .Include(c => c.Bid).ThenInclude(b => b!.Invitation).ThenInclude(i => i.Tender)
            .Include(c => c.PurchaseRequest)
            .Include(c => c.ClarificationResponses.OrderBy(r => r.CreatedAt))
                .ThenInclude(r => r.RespondedByUser)
            .OrderByDescending(c => c.CreatedAt).ToList();
        var selected = conversations.FirstOrDefault(c => c.ClarificationId == id);
        if (id.HasValue && selected is null)
        {
            return NotFound();
        }

        var assignments = EligibleClarificationRecipients().AsNoTracking()
            .Include(a => a.Tender).Include(a => a.User)
            .OrderBy(a => a.Tender.Title).ThenBy(a => a.Position).ToList();
        var tenders = assignments.Select(a => a.Tender).DistinctBy(t => t.TenderId).ToList();
        var selectedTenderId = tenderId ?? tenders.FirstOrDefault()?.TenderId;
        var recipients = assignments.Where(a => a.TenderId == selectedTenderId).ToList();
        var conversationTenderId = selected?.TenderId ?? selected?.Bid?.TenderId;
        var canReply = selected is not null && selected.RecipientUserId == ManagerId &&
            selected.Status == "Pending" && assignments.Any(a =>
                a.TenderId == conversationTenderId && a.UserId == selected.SenderUserId);

        return View(new ManagerClarificationPage(tenders, selectedTenderId, recipients, conversations, selected, ManagerId, canReply));
    }

    [HttpPost]
    public IActionResult CreateClarification(ManagerClarificationInput input)
    {
        if (!ModelState.IsValid)
        {
            return ClarificationMessage("Enter a subject, description, and an eligible committee member.", input.TenderId);
        }

        using var transaction = _context.Database.BeginTransaction(IsolationLevel.RepeatableRead);
        var assignment = EligibleClarificationRecipients().SingleOrDefault(a =>
            a.TenderId == input.TenderId && a.UserId == input.RecipientId);
        if (assignment is null)
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
        NotifyCommittee(assignment, "New clarification", input.TenderId);
        _context.SaveChanges();
        transaction.Commit();

        return ClarificationMessage("Question sent.", input.TenderId, clarification.ClarificationId);
    }

    [HttpPost]
    public IActionResult ReplyToClarification(ManagerClarificationReply input)
    {
        if (!ModelState.IsValid)
        {
            return ClarificationMessage("Enter a response of up to 4,000 characters.", id: input.Id);
        }

        using var transaction = _context.Database.BeginTransaction(IsolationLevel.Serializable);
        var clarification = _context.Clarifications.Include(clarification => clarification.Bid)
            .SingleOrDefault(c => c.ClarificationId == input.Id && c.RecipientUserId == ManagerId);
        if (clarification is null)
        {
            return NotFound();
        }

        var tenderId = clarification.TenderId ?? clarification.Bid?.TenderId;
        var assignment = EligibleClarificationRecipients().SingleOrDefault(a =>
            a.TenderId == tenderId && a.UserId == clarification.SenderUserId);
        if (assignment is null)
        {
            return ClarificationMessage("This tender is no longer in a stage that allows a reply to this sender.", id: input.Id);
        }

        if (clarification.Status != "Pending")
        {
            return ClarificationMessage("This question has already been answered or resolved.", id: input.Id);
        }

        clarification.Status = "Answered";


        _context.ClarificationResponses.Add(new ClarificationResponse
        {
            ClarificationId = input.Id,
            RespondedByUserId = ManagerId,
            Message = input.Message.Trim(),
            CreatedAt = DateTime.UtcNow
        });
        NotifyCommittee(assignment, "Clarification answered", assignment.TenderId);
        _context.SaveChanges();
        transaction.Commit();

        return ClarificationMessage("Response sent.", id: input.Id);
    }

    [HttpPost]
    public IActionResult ResolveClarification(int id)
    {
        using var transaction = _context.Database.BeginTransaction(IsolationLevel.Serializable);

        var clarification = _context.Clarifications.SingleOrDefault(question =>
            question.ClarificationId == id && question.SenderUserId == ManagerId && question.Status == "Answered");
        if (clarification is null)
        {
            return NotFound();
        }

        clarification.Status = "Resolved";
        clarification.ResolvedAt = DateTime.UtcNow;
        _context.SaveChanges();
        transaction.Commit();


        return ClarificationMessage("Clarification resolved.", id: id);
    }

    private IQueryable<CommitteeAssignment> EligibleClarificationRecipients()
    {
        return _context.CommitteeAssignments.Where(a => a.UserId != ManagerId && a.User.IsActive &&
            ((a.CommitteeType == "Evaluation" &&
                (a.Tender.Status == "UnderEvaluation" || a.Tender.Status == AwaitingManagerReview)) ||
             (a.CommitteeType == "Top" && a.Tender.Status == AwaitingCommitteeDecision)) &&
            (from membership in _context.UserRoles
             join role in _context.Roles on membership.RoleId equals role.Id
             where membership.UserId == a.UserId &&
                ((a.CommitteeType == "Evaluation" &&
                    (role.Name == RoleNames.EvaluationCommittee || RoleNames.EvaluationPositions.Contains(role.Name!))) ||
                 (a.CommitteeType == "Top" &&
                    (role.Name == RoleNames.TopCommittee || RoleNames.TopPositions.Contains(role.Name!))))
             select membership).Any());
    }

    private void NotifyCommittee(CommitteeAssignment assignment, string title, int tenderId)
    {
        var controller = assignment.CommitteeType == "Top" ? "TopCommittee" : "EvaluationCommittee";
        Notify(assignment.UserId, title, $"A clarification for tender #{tenderId} was updated.", controller, "Clarifications");
    }

    private IActionResult ClarificationMessage(string message, int? tenderId = null, int? id = null)
    {
        TempData["ManagerMessage"] = message;
        return RedirectToAction(nameof(Clarifications), new { tenderId, id });
    }
}
