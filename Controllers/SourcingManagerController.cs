using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using SourcingTenderManagement.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Controllers;

// The Sourcing Manager workspace: review the officer's submitted reports and forward them to the CEO & Chiefs.
// This class is split into several files (SourcingManagerController.*.cs).
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
        var approvedRecommendations = _context.EvaluationReports.Count(report => report.Status == ManagerApproved
            && report.Tender.Status == AwaitingManagerReview);
        var tendersWithTopCommittee = _context.Tenders.Count(tender => tender.Status == AwaitingCommitteeDecision);
        var pendingQuestions = _context.Clarifications.Count(question =>
            question.RecipientUserId == ManagerId && question.Status == "Pending");

        ViewBag.DashboardReportStates = _context.EvaluationReports.AsNoTracking()
            .Where(r => r.Status == "OfficerSubmitted" || r.Status == "ManagerApproved" || r.Status == "ManagerRejected" ||
                r.Status == "TopApproved" || r.ManagerSummary != null || r.Tender.Status == AwaitingManagerReview ||
                r.Tender.Status == AwaitingCommitteeDecision || r.Tender.Status == "Awarded")
            .GroupBy(r => r.Status == "ManagerRejected" || r.Tender.TenderApprovals.Any(a => a.Stage == "Top" && a.Decision == "Rejected") ? "Rejected" :
            r.Tender.Status == AwaitingManagerReview && r.Status == ManagerApproved ? "Ready to forward" :
            r.Tender.Status == AwaitingManagerReview
                && (r.Status == "OfficerSubmitted" || r.Status == "CommitteeApproved") ? "Needs your review" :
            r.Tender.Status == AwaitingCommitteeDecision ? "With CEO & Chiefs" : r.Tender.Status == "Awarded" ? "Awarded" :
            r.Tender.Status == "Cancelled" ? "Cancelled" : r.Status == "ReturnedForReview" ? "Back with committee" : "With sourcing team")
            .Select(g => new { Status = g.Key, Count = g.Count() }).ToDictionary(g => g.Status, g => g.Count);
        var dashboard = new ManagerDashboard(
            reportsAwaitingReview, approvedRecommendations, tendersWithTopCommittee, pendingQuestions);
        return View(dashboard);
    }

    [HttpGet]
    public IActionResult Submissions(int? id, int? rejectedTenderId = null, bool showRejections = false)
    {
        var rejections = ManagedRejections();
        var editRequests = ManagedTopEditRequests();
        if (rejectedTenderId.HasValue)
        {
            var inRejections = rejections.Any(t => t.TenderId == rejectedTenderId);
            var inEdits = editRequests.Any(t => t.TenderId == rejectedTenderId);
            if (!inRejections && !inEdits) return NotFound();
            if (inRejections) rejections = rejections.Where(t => t.TenderId == rejectedTenderId);
            if (inEdits) editRequests = editRequests.Where(t => t.TenderId == rejectedTenderId);
        }
        var editList = editRequests.OrderByDescending(t => t.TopEditRequestedAt ?? t.CreatedAt).ToList();
        var requesterIds = editList.Select(t => t.TopEditRequestedByUserId)
            .Where(uid => !string.IsNullOrWhiteSpace(uid))
            .Distinct()
            .ToArray();
        ViewBag.TopEditRequests = editList;
        ViewBag.TopEditRequesters = requesterIds.Length == 0
            ? new Dictionary<string, string>()
            : _context.Users.AsNoTracking()
                .Where(u => requesterIds.Contains(u.Id))
                .ToDictionary(u => u.Id, u => u.FullName);
        ViewBag.RejectedTenders = rejections.OrderByDescending(t => t.CreatedAt).ToList();
        ViewBag.OpenRejections = showRejections || rejectedTenderId.HasValue;
        var evaluationReports = _context.EvaluationReports
            .Include(report => report.Tender).ThenInclude(t => t.OfferExtensions)
            .InManagerReview()
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
        ViewData["AiChatRecordId"] = selectedReport.EvaluationReportId;
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
            .ThenInclude(score => score.EvaluationCriterion)
            .Include(bid => bid.Evaluations.Where(evaluation =>
                evaluation.Status == "Submitted" || evaluation.Status == "Approved"))
            .ThenInclude(evaluation => evaluation.CommitteeAssignment);

        var supplierOffers = bidsWithEvaluations.AsSplitQuery()
            .OrderBy(bid => bid.BidId)
            .ToList().OrderByDescending(bid => bid.BidId == selectedReport.RecommendedBidId).ToList();

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

        // Serializable is the strictest transaction setting: nobody else can change the rows read here until it finishes.
        using var transaction = _context.Database.BeginTransaction(IsolationLevel.Serializable);

        var evaluationReport = _context.EvaluationReports.Find(input.ReportId);

        if (evaluationReport == null ||
            (evaluationReport.Status != "OfficerSubmitted" && evaluationReport.Status != "CommitteeApproved") ||
            evaluationReport.SubmittedAt?.Ticks != input.ReviewVersion)
        {
            return SubmissionMessage("This report changed. Review the latest submission before deciding.");
        }

        var tender = _context.Tenders.Find(evaluationReport.TenderId);

        if (tender == null
            || _context.TenderApprovals.Any(a => a.TenderId == tender.TenderId
                && a.Stage == "Top"
                && a.Decision == "Rejected")
            || tender.Status != AwaitingManagerReview)
        {
            return SubmissionMessage("This report changed. Review the latest submission before deciding.");
        }

        if (HasOpenClarifications(tender.TenderId))
            return SubmissionMessage("Both sides must resolve open clarifications before recording a decision.", input.ReportId);

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
            evaluationReport.RecommendedBidId = null;
            evaluationReport.CommitteeSummary = null;
            _context.TenderApprovals.RemoveRange(_context.TenderApprovals.Where(a => a.TenderId == tender.TenderId
                && a.Stage == "Evaluation"));
            _context.CommitteeTieVotes.RemoveRange(_context.CommitteeTieVotes.Where(v => v.TenderId == tender.TenderId));
            foreach (var review in _context.Evaluations.Where(e => e.TenderId == tender.TenderId).ToList())
            { review.Status = "Submitted"; review.ApprovedAt = null; }

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
    public async Task<IActionResult> DraftReferral(int reportId,
        [FromServices] SourcingTenderManagement.Services.Evaluation.BidRecommendationService ai, CancellationToken cancellationToken)
    {
        var report = await _context.EvaluationReports.AsNoTracking()
            .Include(r => r.Tender)
            .ThenInclude(t => t.PurchaseRequest)
            .ThenInclude(r => r.Department)
            .Include(r => r.Tender).ThenInclude(t => t.OfferExtensions)
            .Include(r => r.Tender).ThenInclude(t => t.PurchaseRequest).ThenInclude(r => r.PurchaseRequestItems)
            .Include(r => r.Bid).ThenInclude(b => b!.Invitation).ThenInclude(i => i.SupplierProfile)
            .Include(r => r.Bid).ThenInclude(b => b!.BidItems)
            .SingleOrDefaultAsync(r => r.EvaluationReportId == reportId, cancellationToken);
        if (report == null
            || _context.TenderApprovals.Any(a => a.TenderId == report.TenderId
                && a.Stage == "Top"
                && a.Decision == "Rejected")
            || report.Status != ManagerApproved
            || report.Tender.Status != AwaitingManagerReview
            || report.Bid == null)
            return BadRequest(new { error = "Only an approved recommendation awaiting referral can be summarized." });
        try
        {
            var winnerMarks = await _context.Evaluations.AsNoTracking()
                .Where(e => e.TenderId == report.TenderId && e.BidId == report.Bid.BidId && e.Status == "Approved"
                    && e.ApprovedAt != null && e.FinancialMark != null && e.TechnicalMark != null)
                .Select(e => new { e.FinancialMark, e.TechnicalMark }).ToListAsync(cancellationToken);
            var excludedSuppliers = await _context.Bids.AsNoTracking()
                .Where(b => b.TenderId == report.TenderId && b.BidId != report.Bid.BidId)
                .Select(b => b.Invitation.SupplierProfile.CompanyName).Distinct().ToListAsync(cancellationToken);
            var summary = await ai.DraftReferral(new { tender = report.Tender.Title, report.Tender.Description, report.Tender.Terms,
                offerRoundsAndException = SourcingTenderManagement.Services.Reports.OfferRoundContext.Describe(report.Tender),
                purchaseRequest = new { report.Tender.PurchaseRequest.Title, report.Tender.PurchaseRequest.Reason,
                    department = report.Tender.PurchaseRequest.Department.Name, report.Tender.PurchaseRequest.Priority, report.Tender.PurchaseRequest.TargetDate,
                    items = report.Tender.PurchaseRequest.PurchaseRequestItems.OrderBy(i => i.PurchaseRequestItemId)
                        .Select(i => new { i.PurchaseRequestItemId, i.ItemName, i.Quantity, i.Unit, i.Specifications }) },
                outputScope = "Summarize only the first-ranked winning offer. The full top-three committee report is retained separately for reference, not summarized here.",
                selectionBasis = winnerMarks.Count == 4 ? "Highest recorded committee score after every offer received four member reviews." : "Recorded committee recommendation.",
                winningScore = winnerMarks.Count == 4 ? winnerMarks.Average(e => e.FinancialMark!.Value + e.TechnicalMark!.Value) : (decimal?)null,
                offer = new { report.Bid.BidId, supplier = report.Bid.Invitation.SupplierProfile.CompanyName,
                    total = CommitteeEvaluationPage.Total(report.Bid), report.Bid.Currency, report.Bid.DeliveryDays, report.Bid.PaymentTerms, report.Bid.TechnicalDetails,
                    items = report.Bid.BidItems.Select(i => new { i.PurchaseRequestItemId, i.Quantity, i.UnitPrice, i.OfferedSpecifications }) } }, cancellationToken);
            if (!summary.Contains(report.Bid.Invitation.SupplierProfile.CompanyName, StringComparison.OrdinalIgnoreCase)
                || excludedSuppliers.Any(name => !string.Equals(name, report.Bid.Invitation.SupplierProfile.CompanyName, StringComparison.OrdinalIgnoreCase)
                    && summary.Contains(name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("The manager summary must cover only the winning supplier.");
            return Json(new { summary });
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException or InvalidOperationException or KeyNotFoundException)
        { return StatusCode(503, new { error = "Could not generate the summary. Check your vLLM / AI endpoint or API key and try again." }); }
    }

    [HttpPost]
    public IActionResult Forward(ManagerForwardInput input)
    {
        var reason = input.Reason?.Trim();
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(reason))
        {
            return SubmissionMessage("Enter the policy reason for referral.", input.ReportId);
        }

        // Serializable is the strictest transaction setting: nobody else can change the rows read here until it finishes.
        using var transaction = _context.Database.BeginTransaction(IsolationLevel.Serializable);

        var evaluationReport = _context.EvaluationReports.Find(input.ReportId);

        if (evaluationReport == null || evaluationReport.Status != ManagerApproved)
        {
            return SubmissionMessage("Only an approved recommendation awaiting referral can be forwarded.");
        }

        var tender = _context.Tenders.Find(evaluationReport.TenderId);

        if (tender == null
            || _context.TenderApprovals.Any(a => a.TenderId == tender.TenderId
                && a.Stage == "Top"
                && a.Decision == "Rejected")
            || tender.Status != AwaitingManagerReview)
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
                    return SubmissionMessage($"Admin must provide exactly one active CEO & Chiefs account for {role}.", input.ReportId);
                memberIds.Add(candidates[0]);
            }
            if (memberIds.Distinct().Count() != 4)
                return SubmissionMessage("Four different CEO & Chiefs accounts are required.", input.ReportId);
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
            return SubmissionMessage("The tender needs four distinct, active CEO & Chiefs members before referral.", input.ReportId);
        }

        if (HasOpenClarifications(tender.TenderId))
            return SubmissionMessage("Both sides must resolve open clarifications before referral.", input.ReportId);

        _context.Entry(tender).Reference(t => t.PurchaseRequest).Query().Include(r => r.Department)
            .Include(r => r.PurchaseRequestItems).Load();
        var winningOffer = _context.Bids.Include(b => b.Invitation).ThenInclude(i => i.SupplierProfile)
            .Include(b => b.BidItems).SingleOrDefault(b => b.TenderId == tender.TenderId
                && b.BidId == evaluationReport.RecommendedBidId);
        if (winningOffer == null) return SubmissionMessage("The recommended offer is missing. Return this report for correction.", input.ReportId);

        tender.Status = AwaitingCommitteeDecision;
        if (string.IsNullOrWhiteSpace(evaluationReport.ManagerSummary))
        {
            evaluationReport.ManagerSummary = $"Referral reason: {reason}";
        }
        else
        {
            evaluationReport.ManagerSummary += $"\n\nReferral reason: {reason}";
        }
        _context.Entry(tender).Collection(t => t.OfferExtensions).Load();
        evaluationReport.ManagerSummary = SourcingTenderManagement.Services.Reports.ManagerReferralContext.Describe(tender, winningOffer)
            + "\n\n## Manager assessment\n" + evaluationReport.ManagerSummary;

        NotifyAssignedCommittee(tender.TenderId, "Top", "Tender awaiting committee decision",
            $"Tender #{tender.TenderId} has been referred for final decision.", "TopCommittee", "Decisions");

        _context.SaveChanges();
        transaction.Commit();
        _logger.LogInformation("Manager {ManagerId} referred tender {TenderId} to the CEO & Chiefs.", ManagerId, tender.TenderId);

        return SubmissionMessage("Tender forwarded to the CEO & Chiefs.");
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
        var managerDecisions = SourcingTenderManagement.Services.Reports.Data.ApprovalsReport.ManagerDecisions(_context, search, decision, ManagerId)
            .Include(approval => approval.User)
            .Include(approval => approval.Tender)
            .OrderByDescending(approval => approval.DecidedAt)
            .ToList();

        var page = new ManagerReportPage(managerDecisions, search, decision);
        return View(page);
    }

    private bool HasOpenClarifications(int tenderId) => _context.Clarifications.Any(c =>
        c.Status != "Resolved" && (c.TenderId == tenderId || (c.Bid != null && c.Bid.TenderId == tenderId)));

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
