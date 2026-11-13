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

    private IQueryable<EvaluationReport> ReportsAwaitingManagerDecision => _context.EvaluationReports.Where(report =>
        report.Tender.Status == AwaitingManagerReview &&
        (report.Status == "OfficerSubmitted" || report.Status == "CommitteeApproved"));

    [HttpGet]
    public IActionResult Index()
    {
        return RedirectToAction(nameof(Dashboard));
    }

    [HttpGet]
    public IActionResult Dashboard()
    {
        var reportsAwaitingReview = ReportsAwaitingManagerDecision.Count();
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
            .AsNoTracking()
            .Include(report => report.Tender)
            .Where(report => report.Tender.Status == AwaitingManagerReview &&
                (report.Status == "OfficerSubmitted" ||
                 report.Status == "CommitteeApproved" ||
                 report.Status == ManagerApproved))
            .OrderByDescending(report => report.SubmittedAt)
            .ThenByDescending(report => report.EvaluationReportId)
            .ToList();

        var selectedReport = evaluationReports.FirstOrDefault();
        if (id.HasValue)
        {
            selectedReport = evaluationReports.FirstOrDefault(report => report.EvaluationReportId == id.Value);
            if (selectedReport == null)
            {
                return NotFound();
            }
        }

        if (selectedReport == null)
        {
            var emptyPage = new ManagerReviewPage(evaluationReports, null, new List<Bid>(), null);
            return View(emptyPage);
        }

        var tenderId = selectedReport.TenderId;
        var supplierOffers = _context.Bids
            .AsNoTracking()
            .AsSplitQuery()
            .Where(bid => bid.TenderId == tenderId && bid.Status != "Draft" && bid.Status != "Withdrawn")
            .Include(bid => bid.Invitation)
                .ThenInclude(invitation => invitation.SupplierProfile)
            .Include(bid => bid.BidItems)
            .Include(bid => bid.Evaluations.Where(evaluation =>
                evaluation.Status == "Submitted" || evaluation.Status == "Approved"))
                .ThenInclude(evaluation => evaluation.EvaluationScores)
                .ThenInclude(score => score.EvaluationCriterion)
            .OrderBy(bid => bid.BidId)
            .ToList();

        var page = new ManagerReviewPage(evaluationReports, selectedReport, supplierOffers, selectedReport.SubmittedAt);
        return View(page);
    }


    [HttpPost]
    public IActionResult Decide(ManagerDecisionInput input)
    {
        var newReportStatus = input.Decision switch
        {
            "Approved" => ManagerApproved,
            "Rejected" => "ManagerRejected",
            ReturnedForReview => ReturnedForReview,
            _ => null
        };
        var notes = input.Notes?.Trim();
        if (!ModelState.IsValid || newReportStatus is null || (input.Decision != "Approved" && string.IsNullOrWhiteSpace(notes)))
            return SubmissionMessage("Choose a valid decision and provide comments when rejecting or returning a report.", input.ReportId);

        using var transaction = _context.Database.BeginTransaction(IsolationLevel.Serializable);

        var evaluationReport = ReportsAwaitingManagerDecision.Include(report => report.Tender)
            .SingleOrDefault(report => report.EvaluationReportId == input.ReportId);
        if (evaluationReport is null || evaluationReport.SubmittedAt?.Ticks != input.ReviewVersion)
        {
            return SubmissionMessage("This report changed. Review the latest submission before deciding.");
        }

        var tender = evaluationReport.Tender;
        if (input.Decision == "Approved")
        {
            var recommendedBid = _context.Bids.SingleOrDefault(bid => bid.BidId == evaluationReport.RecommendedBidId);
            if (recommendedBid is null || recommendedBid.TenderId != tender.TenderId ||
                recommendedBid.Status is "Draft" or "Withdrawn" or "Rejected" or "NonCompliant" ||
                recommendedBid.ComplianceStatus == "NonCompliant")
            {
                return SubmissionMessage("The report must identify an eligible recommended offer before approval.", input.ReportId);
            }
        }

        evaluationReport.Status = newReportStatus;
        evaluationReport.ManagerSummary = notes;

        if (input.Decision == ReturnedForReview)
        {
            tender.Status = "UnderEvaluation";
        }
        else if (input.Decision == "Rejected")
        {
            tender.Status = "Closed";
        }

        // The existing schema keeps one approval row per manager and tender.
        var managerDecision = _context.TenderApprovals.SingleOrDefault(a =>
            a.TenderId == tender.TenderId && a.Stage == "Manager" && a.UserId == ManagerId);
        if (managerDecision is null)
        {
            managerDecision = new TenderApproval { TenderId = tender.TenderId, Stage = "Manager", UserId = ManagerId };
            _context.TenderApprovals.Add(managerDecision);
        }
        var previousDecision = managerDecision.Decision;
        managerDecision.Decision = input.Decision;
        managerDecision.Notes = notes;
        managerDecision.DecidedAt = DateTime.UtcNow;
        if (input.Decision != "Approved")
        {
            var recipients = _context.CommitteeAssignments
                .Where(a => a.TenderId == tender.TenderId && a.CommitteeType == "Evaluation" &&
                    a.UserId != ManagerId && a.User.IsActive)
                .Select(a => a.UserId).ToList();
            foreach (var recipient in recipients)
                Notify(recipient, "Manager review completed", $"Tender #{tender.TenderId}: {input.Decision}.", "EvaluationCommittee", "Reports");
        }
        _context.SaveChanges();
        transaction.Commit();
        _logger.LogInformation("Manager {ManagerId} recorded {Decision} for tender {TenderId}. Previous decision: {PreviousDecision}.",
            ManagerId, input.Decision, tender.TenderId, previousDecision);

        return SubmissionMessage("Decision saved.", input.Decision == "Approved" ? input.ReportId : null);
    }

    [HttpPost]
    public IActionResult Forward(ManagerForwardInput input)
    {
        var reason = input.Reason?.Trim();
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(reason))
            return SubmissionMessage("Enter the policy reason for referral.", input.ReportId);

        using var transaction = _context.Database.BeginTransaction(IsolationLevel.Serializable);

        var evaluationReport = _context.EvaluationReports.Include(report => report.Tender)
            .SingleOrDefault(report => report.EvaluationReportId == input.ReportId &&
                report.Status == ManagerApproved && report.Tender.Status == AwaitingManagerReview);
        if (evaluationReport is null)
        {
            return SubmissionMessage("Only an approved recommendation awaiting referral can be forwarded.");
        }

        var tender = evaluationReport.Tender;
        var committeeMemberIds = _context.CommitteeAssignments
            .Where(assignment => assignment.TenderId == tender.TenderId &&
                assignment.CommitteeType == "Top" && assignment.User.IsActive && assignment.UserId != ManagerId)
            .Select(assignment => assignment.UserId)
            .ToList();

        tender.Status = AwaitingCommitteeDecision;
        if (string.IsNullOrWhiteSpace(evaluationReport.ManagerSummary))
        {
            evaluationReport.ManagerSummary = $"Referral reason: {reason}";
        }
        else
        {
            evaluationReport.ManagerSummary += $"\n\nReferral reason: {reason}";
        }

        foreach (var committeeMemberId in committeeMemberIds)
        {
            Notify(committeeMemberId, "Tender awaiting committee decision",
                $"Tender #{tender.TenderId} has been referred for final decision.", "TopCommittee", "Decisions");
        }

        _context.SaveChanges();
        transaction.Commit();
        _logger.LogInformation("Manager {ManagerId} referred tender {TenderId} to the Top Committee.", ManagerId, tender.TenderId);

        return SubmissionMessage("Tender forwarded to the Top Committee.");
    }

    [HttpGet]
    public IActionResult ChosenOffers()
    {
        var tenders = _context.Tenders.AsNoTracking()
            .Where(t => t.Status == "Awarded" && t.EvaluationReport != null)
            .Include(t => t.Bid).ThenInclude(b => b!.Invitation).ThenInclude(i => i.SupplierProfile)
            .Include(t => t.EvaluationReport)
            .OrderByDescending(t => t.AwardedAt).ToList();
        return View(tenders);
    }

    [HttpGet]
    public IActionResult Reports(string? search, string? decision)
    {
        var query = _context.TenderApprovals.AsNoTracking().Where(a => a.Stage == "Manager");
        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();
            query = query.Where(a => a.Tender.Title.Contains(search));
        }
        if (decision is "Approved" or "Rejected" or ReturnedForReview)
            query = query.Where(a => a.Decision == decision);

        var managerDecisions = query.Include(a => a.User).Include(a => a.Tender).ThenInclude(t => t.EvaluationReport)
            .OrderByDescending(a => a.DecidedAt).ToList();
        return View(new ManagerReportPage(managerDecisions, search, decision));
    }

    private IActionResult SubmissionMessage(string message, int? id = null)
    {
        TempData["ManagerMessage"] = message;
        return RedirectToAction(nameof(Submissions), new { id });
    }

    private void Notify(string recipientId, string title, string message, string controller, string action)
    {
        _context.Notifications.Add(new Notification
        {
            UserId = recipientId,
            Title = title,
            Message = message,
            RelativeUrl = Url.Action(action, controller),
            CreatedAt = DateTime.UtcNow
        });
    }
}
