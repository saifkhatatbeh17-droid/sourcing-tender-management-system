using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Controllers;

[Authorize(Roles = RoleNames.SourcingManager)]
[AutoValidateAntiforgeryToken]
public partial class SourcingManagerController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<SourcingManagerController> _logger;

    public SourcingManagerController(ApplicationDbContext context, ILogger<SourcingManagerController> logger)
    {
        _context = context;
        _logger = logger;
    }

    private const string AwaitingManagerReview = "AwaitingManagerReview";
    private const string AwaitingCommitteeDecision = "AwaitingCommitteeDecision";
    private const string ManagerApproved = "ManagerApproved";
    private const string ReturnedForReview = "ReturnedForReview";

    private string ManagerId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet]
    public IActionResult Index()
    {
        return RedirectToAction(nameof(Dashboard));
    }

    [HttpGet]
    public IActionResult Dashboard()
    {
        var reportsAwaitingReview = _context.EvaluationReports.Count(report =>
            report.Tender.Status == AwaitingManagerReview &&
            (report.Status == "OfficerSubmitted" || report.Status == "CommitteeApproved"));
        var approvedRecommendations = _context.EvaluationReports.Count(report => report.Status == ManagerApproved && report.Tender.Status == AwaitingManagerReview);
        var tendersWithTopCommittee = _context.Tenders.Count(tender => tender.Status == AwaitingCommitteeDecision);
        var pendingQuestions = _context.Clarifications.Count(question =>
            question.RecipientUserId == ManagerId && question.Status == "Pending");

        var dashboard = new ManagerDashboard(
            reportsAwaitingReview, approvedRecommendations, tendersWithTopCommittee, pendingQuestions);
        return View(dashboard);
    }

    [HttpGet]
    public IActionResult Submissions(int? id)
    {
        var evaluationReports = _context.EvaluationReports
            .Include(report => report.Tender)
            .Where(report => report.Tender.Status == AwaitingManagerReview &&
                (report.Status == "OfficerSubmitted" ||
                 report.Status == "CommitteeApproved" ||
                 report.Status == ManagerApproved))
            .OrderByDescending(report => report.SubmittedAt)
            .ThenByDescending(report => report.EvaluationReportId)
            .ToList();

        EvaluationReport? selectedReport;
        if (id.HasValue)
        {
            selectedReport = _context.EvaluationReports.Find(id.Value);

            if (selectedReport == null || !evaluationReports.Contains(selectedReport))
            {
                return NotFound();
            }
        }
        else
        {
            selectedReport = evaluationReports.FirstOrDefault();
        }

        if (selectedReport == null)
        {
            var emptyPage = new ManagerReviewPage(evaluationReports, null, new List<Bid>(), null);
            return View(emptyPage);
        }

        var tenderId = selectedReport.TenderId;
        var submittedBids = _context.Bids.AsNoTracking()
            .Where(bid => bid.TenderId == tenderId && bid.Status != "Draft" && bid.Status != "Withdrawn");

        var bidsWithSuppliers = submittedBids
            .Include(bid => bid.Invitation)
            .ThenInclude(invitation => invitation.SupplierProfile)
            .Include(bid => bid.BidItems);

        var bidsWithEvaluations = bidsWithSuppliers
            .Include(bid => bid.Evaluations.Where(evaluation =>
                evaluation.Status == "Submitted" || evaluation.Status == "Approved"))
            .ThenInclude(evaluation => evaluation.EvaluationScores)
            .ThenInclude(score => score.EvaluationCriterion);

        var supplierOffers = bidsWithEvaluations.AsSplitQuery()
            .OrderBy(bid => bid.BidId)
            .ToList();

        var page = new ManagerReviewPage(evaluationReports, selectedReport, supplierOffers, selectedReport.SubmittedAt);
        return View(page);
    }

    [HttpPost]
    public IActionResult Decide(ManagerDecisionInput input)
    {
        var decision = input.Decision;
        var notes = input.Notes?.Trim();
        var isApproval = decision == "Approved";
        var isRejection = decision == "Rejected";
        var isReturn = decision == ReturnedForReview;

        if (!ModelState.IsValid || (!isApproval && !isRejection && !isReturn) ||
            (!isApproval && string.IsNullOrWhiteSpace(notes)))
        {
            return SubmissionMessage("Choose a valid decision and provide comments when rejecting or returning a report.", input.ReportId);
        }

        using var transaction = _context.Database.BeginTransaction(IsolationLevel.Serializable);

        var evaluationReport = _context.EvaluationReports.Find(input.ReportId);

        if (evaluationReport == null ||
            (evaluationReport.Status != "OfficerSubmitted" && evaluationReport.Status != "CommitteeApproved") ||
            evaluationReport.SubmittedAt?.Ticks != input.ReviewVersion)
        {
            return SubmissionMessage("This report changed. Review the latest submission before deciding.");
        }

        var tender = _context.Tenders.Find(evaluationReport.TenderId);

        if (tender == null || tender.Status != AwaitingManagerReview)
        {
            return SubmissionMessage("This report changed. Review the latest submission before deciding.");
        }

        if (isApproval)
        {
            var recommendedBid = _context.Bids.Find(evaluationReport.RecommendedBidId);

            if (recommendedBid == null || recommendedBid.TenderId != tender.TenderId ||
                recommendedBid.Status == "Draft" || recommendedBid.Status == "Withdrawn" ||
                recommendedBid.Status == "Rejected" || recommendedBid.Status == "NonCompliant" ||
                recommendedBid.ComplianceStatus == "NonCompliant")
            {
                return SubmissionMessage("The report must identify an eligible recommended offer before approval.", input.ReportId);
            }

            evaluationReport.Status = ManagerApproved;
        }
        else if (isRejection)
        {
            evaluationReport.Status = "ManagerRejected";
            tender.Status = "Closed";
        }
        else
        {
            evaluationReport.Status = ReturnedForReview;
            tender.Status = "UnderEvaluation";
        }

        evaluationReport.ManagerSummary = notes;

        // The existing schema keeps one approval row per manager and tender.
        var managerDecision = _context.TenderApprovals.SingleOrDefault(approval =>
            approval.TenderId == tender.TenderId && approval.Stage == "Manager" && approval.UserId == ManagerId);

        if (managerDecision == null)
        {
            managerDecision = new TenderApproval
            {
                TenderId = tender.TenderId,
                Stage = "Manager",
                UserId = ManagerId
            };
            _context.TenderApprovals.Add(managerDecision);
        }

        var previousDecision = managerDecision.Decision;
        managerDecision.Decision = decision;
        managerDecision.Notes = notes;
        managerDecision.DecidedAt = DateTime.UtcNow;
        if (!isApproval)
        {
            NotifyAssignedCommittee(tender.TenderId, "Evaluation", "Manager review completed",
                $"Tender #{tender.TenderId}: {decision}.", "EvaluationCommittee", "Reports");
        }

        _context.SaveChanges();
        transaction.Commit();
        _logger.LogInformation("Manager {ManagerId} recorded {Decision} for tender {TenderId}. Previous decision: {PreviousDecision}.",
            ManagerId, decision, tender.TenderId, previousDecision);

        if (isApproval)
        {
            return SubmissionMessage("Decision saved.", evaluationReport.EvaluationReportId);
        }

        return SubmissionMessage("Decision saved.");
    }

    [HttpPost]
    public IActionResult Forward(ManagerForwardInput input)
    {
        var reason = input.Reason?.Trim();
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(reason))
        {
            return SubmissionMessage("Enter the policy reason for referral.", input.ReportId);
        }

        using var transaction = _context.Database.BeginTransaction(IsolationLevel.Serializable);

        var evaluationReport = _context.EvaluationReports.Find(input.ReportId);

        if (evaluationReport == null || evaluationReport.Status != ManagerApproved)
        {
            return SubmissionMessage("Only an approved recommendation awaiting referral can be forwarded.");
        }

        var tender = _context.Tenders.Find(evaluationReport.TenderId);

        if (tender == null || tender.Status != AwaitingManagerReview)
        {
            return SubmissionMessage("Only an approved recommendation awaiting referral can be forwarded.");
        }

        var assignments = _context.CommitteeAssignments
            .Where(a => a.TenderId == tender.TenderId && a.CommitteeType == "Top").ToList();
        if (assignments.Count == 0)
        {
            var memberIds = new List<string>();
            foreach (var role in RoleNames.TopPositions)
            {
                var candidates = _context.Users.Where(user => user.IsActive &&
                    _context.UserRoles.Any(link => link.UserId == user.Id &&
                        _context.Roles.Any(r => r.Id == link.RoleId && r.Name == role)) &&
                    _context.UserRoles.Any(link => link.UserId == user.Id &&
                        _context.Roles.Any(r => r.Id == link.RoleId && r.Name == RoleNames.TopCommittee)))
                    .Select(user => user.Id).ToList();
                if (candidates.Count != 1)
                    return SubmissionMessage($"Admin must provide exactly one active Top Committee account for {role}.", input.ReportId);
                memberIds.Add(candidates[0]);
            }
            if (memberIds.Distinct().Count() != 4)
                return SubmissionMessage("Four different Top Committee accounts are required.", input.ReportId);
            for (var position = 1; position <= 4; position++)
                _context.CommitteeAssignments.Add(new CommitteeAssignment
                {
                    TenderId = tender.TenderId, UserId = memberIds[position - 1],
                    CommitteeType = "Top", Position = position
                });
            // Save inside this transaction so the notification query can find the new assignments.
            _context.SaveChanges();
        }
        else if (assignments.Count != 4 || assignments.Select(a => a.UserId).Distinct().Count() != 4 ||
            assignments.Any(a => !_context.Users.Any(user => user.Id == a.UserId && user.IsActive &&
                _context.UserRoles.Any(link => link.UserId == user.Id &&
                    _context.Roles.Any(r => r.Id == link.RoleId && r.Name == RoleNames.TopCommittee)))))
        {
            return SubmissionMessage("The tender needs four distinct, active Top Committee members before referral.", input.ReportId);
        }

        tender.Status = AwaitingCommitteeDecision;
        if (string.IsNullOrWhiteSpace(evaluationReport.ManagerSummary))
        {
            evaluationReport.ManagerSummary = $"Referral reason: {reason}";
        }
        else
        {
            evaluationReport.ManagerSummary += $"\n\nReferral reason: {reason}";
        }

        NotifyAssignedCommittee(tender.TenderId, "Top", "Tender awaiting committee decision",
            $"Tender #{tender.TenderId} has been referred for final decision.", "TopCommittee", "Decisions");

        _context.SaveChanges();
        transaction.Commit();
        _logger.LogInformation("Manager {ManagerId} referred tender {TenderId} to the Top Committee.", ManagerId, tender.TenderId);

        return SubmissionMessage("Tender forwarded to the Top Committee.");
    }

    [HttpGet]
    public IActionResult ChosenOffers()
    {
        var tenders = _context.Tenders.AsNoTracking()
            .Where(tender => tender.Status == "Awarded" && tender.EvaluationReport != null)
            .Include(tender => tender.Bid)
            .ThenInclude(bid => bid!.Invitation)
            .ThenInclude(invitation => invitation.SupplierProfile)
            .OrderByDescending(tender => tender.AwardedAt)
            .ToList();
        return View(tenders);
    }

    [HttpGet]
    public IActionResult Reports(string? search, string? decision)
    {
        var managerApprovals = _context.TenderApprovals.AsNoTracking()
            .Where(approval => approval.Stage == "Manager");

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();
            managerApprovals = managerApprovals.Where(approval => approval.Tender.Title.Contains(search));
        }

        if (decision == "Approved" || decision == "Rejected" || decision == ReturnedForReview)
        {
            managerApprovals = managerApprovals.Where(approval => approval.Decision == decision);
        }

        var managerDecisions = managerApprovals
            .Include(approval => approval.User)
            .Include(approval => approval.Tender)
            .OrderByDescending(approval => approval.DecidedAt)
            .ToList();

        var page = new ManagerReportPage(managerDecisions, search, decision);
        return View(page);
    }

    private IActionResult SubmissionMessage(string message, int? id = null)
    {
        TempData["ManagerMessage"] = message;
        return RedirectToAction(nameof(Submissions), new { id });
    }

    private void NotifyAssignedCommittee(
        int tenderId, string committeeType, string title, string message, string controller, string action)
    {
        var memberIds = _context.CommitteeAssignments
            .Where(assignment => assignment.TenderId == tenderId &&
                assignment.CommitteeType == committeeType &&
                assignment.UserId != ManagerId && assignment.User.IsActive)
            .Select(assignment => assignment.UserId)
            .ToList();

        foreach (var memberId in memberIds)
        {
            Notify(memberId, title, message, controller, action);
        }
    }

    private void Notify(string recipientId, string title, string message, string controller, string action)
    {
        var notification = new Notification
        {
            UserId = recipientId,
            Title = title,
            Message = message,
            RelativeUrl = Url.Action(action, controller),
            CreatedAt = DateTime.UtcNow
        };

        _context.Notifications.Add(notification);
    }
}
