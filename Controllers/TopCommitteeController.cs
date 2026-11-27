using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using SourcingTenderManagement.Contracts.TopCommittee;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.TopCommittee;
namespace SourcingTenderManagement.Controllers;
// The CEO & Chiefs (Top Committee) workspace: review recommended tenders, vote, and ask clarifications.
// This class is split into several files (TopCommitteeController.*.cs).
[Authorize(Roles = RoleNames.TopCommittee)]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public partial class TopCommitteeController(ITopCommitteeQueries queries, ITopCommitteeClarifications clarifications,
    ITopCommitteeClarificationQueries clarificationQueries, ITopCommitteeVoting voting,
    ITopCommitteeVotingReadiness votingReadiness) : Controller
{
    [HttpGet]
    public IActionResult Index() => RedirectToAction(nameof(Dashboard));

    [HttpGet]
    public async Task<IActionResult> Dashboard(CancellationToken cancellationToken) =>
        await ReadAsync(async () => View(await queries.GetDashboardAsync(User, cancellationToken)));

    [HttpGet]
    public async Task<IActionResult> Decisions([FromQuery] CommitteeTenderFilter filter, CancellationToken cancellationToken)
    {
        // Browser-supplied Status cannot widen the pending-list scope.
        var pending = new CommitteeTenderFilter { Search = filter.Search, FromDate = filter.FromDate,
            ToDate = filter.ToDate, Page = filter.Page, PageSize = filter.PageSize, Status = "AwaitingCommitteeDecision" };
        ViewData["ActivePage"] = nameof(Decisions);
        return await ReadAsync(async () =>
        {
            try
            {
                if (!ModelState.IsValid) throw new ValidationException("Check the search, dates and page settings below.");
                var page = await queries.GetTendersAsync(User, pending, cancellationToken);
                return View(new CommitteePendingPage(page, pending));
            }
            catch (ValidationException error)
            {
                ModelState.AddModelError("", error.Message);
                Response.StatusCode = StatusCodes.Status400BadRequest;
                return View(new CommitteePendingPage(new CommitteePage<CommitteeTenderRow>([], 0, 1, 20), pending));
            }
        });
    }

    [HttpGet]
    public async Task<IActionResult> TenderDetails(int id, int? bidId, CancellationToken cancellationToken)
    {
        ViewData["ActivePage"] = nameof(Decisions);
        return await ReadAsync(async () =>
        {
            var tender = await queries.GetTenderDetailsAsync(User, id, cancellationToken);
            var selected = bidId.HasValue ? tender.Bids.SingleOrDefault(b => b.BidId == bidId.Value)
                : tender.Bids.FirstOrDefault(b => b.IsNominated) ?? tender.Bids.FirstOrDefault();
            if (bidId.HasValue && selected == null) return NotFound();
            return View(await VotingPageAsync(tender, selected, null, cancellationToken));
        });
    }

    private async Task<CommitteeTenderPage> VotingPageAsync(CommitteeTenderDetails tender, CommitteeBid? selected,
        CommitteeVoteInput? input, CancellationToken cancellationToken) =>
        new(tender, selected, input ?? new CommitteeVoteInput { TenderId = tender.TenderId,
            ExpectedNominatedBidId = tender.Report?.RecommendedBidId ?? 0 },
            tender.Readiness.CanCurrentMemberVote && await votingReadiness.IsDatabaseReadyAsync(User, tender.TenderId, cancellationToken));

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> AssessImportance(int id,
        [FromServices] SourcingTenderManagement.Services.Evaluation.BidRecommendationService ai,
        [FromServices] ApplicationDbContext db, CancellationToken cancellationToken) => ReadAsync(async () =>
    {
        var tender = await queries.GetTenderDetailsAsync(User, id, cancellationToken);
        if (tender.Report == null || string.IsNullOrWhiteSpace(tender.Report.CommitteeSummary))
            return BadRequest(new { error = "A committee report is required before analysis." });
        var bid = tender.Bids.FirstOrDefault(b => b.IsNominated);
        // Budget and history facts are computed here so the AI cites real numbers instead of guessing scale.
        var business = await SourcingTenderManagement.Services.Evaluation.ProcurementBusinessContext.LoadAsync(db, tender.TenderId,
            bid?.Total, bid?.Currency, DateOnly.FromDateTime(DateTime.Now), cancellationToken);
        try
        {
            var result = await ai.AssessImportance(new { tender.Title, tender.Description, tender.Terms,
                purchaseRequest = tender.PurchaseRequest,
                report = new { tender.Report.CommitteeSummary, tender.Report.ManagerSummary, tender.Report.OfficerSummary },
                offer = bid == null ? null : new { bid.BidId, bid.SupplierName, bid.Total, bid.Currency, bid.DeliveryDays,
                    bid.PaymentTerms, bid.TechnicalDetails, bid.Items, bid.ComplianceStatus, bid.ComplianceNotes },
                businessContext = business }, cancellationToken);
            return Json(new { result.Level, result.Reason, result.MissingInformation, facts = AssessmentFacts(business) });
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException or InvalidOperationException or KeyNotFoundException)
        { return StatusCode(503, new { error = "Could not assess importance. Check your vLLM / AI endpoint or API key and try again." }); }
    });

    private static object AssessmentFacts(SourcingTenderManagement.Services.Evaluation.BusinessContext business)
    {
        var budget = business.DepartmentBudget;
        var offer = business.Offer;
        return new
        {
            currency = budget?.Currency ?? business.Company?.Currency,
            fiscalYear = budget?.FiscalYear,
            department = budget?.Department,
            allocated = budget?.Allocated,
            remaining = budget?.Remaining,
            usedPercent = budget?.UsedPercent,
            fiscalYearElapsedPercent = budget?.FiscalYearElapsedPercent,
            offerTotal = offer?.OfferTotal,
            offerCurrency = offer?.OfferCurrency,
            percentOfRemaining = offer?.PercentOfDepartmentRemaining,
            varianceFromEstimatePercent = offer?.VarianceFromEstimatePercent,
            estimatedBudget = business.Request.EstimatedBudget,
            comparablePurchases = business.ComparablePurchases.Count,
            missingData = business.MissingData
        };
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitVote([Bind(Prefix = "Vote")] CommitteeVoteInput input, CancellationToken cancellationToken)
    {
        ViewData["ActivePage"] = nameof(Decisions);
        return await ReadAsync(async () =>
        {
            if (ModelState.IsValid)
            {
                try
                {
                    var result = await voting.SubmitAsync(User, input, cancellationToken);
                    TempData["VotingSuccess"] = result.IsAwarded
                        ? "Your approval was recorded. All four members approved, and the nominated bid was awarded."
                        : result.Decision == "RequestEdit"
                            ? "Your edit request was sent to the Sourcing Manager to answer directly or push back to the Sourcing Officer."
                            : result.Decision == "Rejected"
                                ? "Your complete rejection was recorded. Voting has permanently ended and the tender has returned to Sourcing Manager review."
                                : "Your final approval was recorded. Reload this page to see the other members' decisions.";
                    return RedirectToAction(nameof(TenderDetails), new { id = result.TenderId });
                }
                catch (ValidationException error) { ModelState.AddModelError("", error.Message); }
                catch (CommitteeStateConflictException error) { ModelState.AddModelError("", error.Message); }
            }
            Response.StatusCode = StatusCodes.Status400BadRequest;
            if (input.TenderId <= 0) return BadRequest("Choose an assigned tender before voting.");
            var tender = await queries.GetTenderDetailsAsync(User, input.TenderId, cancellationToken);
            var selected = tender.Bids.FirstOrDefault(b => b.IsNominated) ?? tender.Bids.FirstOrDefault();
            return View("TenderDetails", await VotingPageAsync(tender, selected, input, cancellationToken));
        });
    }

    private async Task<IActionResult> ReadAsync(Func<Task<IActionResult>> read)
    {
        try { return await read(); }
        catch (CommitteeAccessDeniedException) { return Forbid(); }
        catch (CommitteeTenderNotFoundException) { return NotFound(); }
    }
    [HttpGet]
    public async Task<IActionResult> Clarifications(int? tenderId, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return BadRequest("Select a valid tender.");
        return await ReadAsync(async () => View(await clarificationQueries.GetPageAsync(User, tenderId, cancellationToken)));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateClarification([Bind(Prefix = "Create")] CommitteeCreateClarification input, CancellationToken cancellationToken)
    {
        ViewData["ActivePage"] = nameof(Clarifications);
        return await ReadAsync(async () =>
        {
            if (ModelState.IsValid)
            {
                try
                {
                    var result = await clarifications.CreateAsync(User, input, cancellationToken);
                    TempData["ClarificationSuccess"] = "Clarification sent to the Sourcing Manager. Committee voting is paused until all open clarifications are resolved.";
                    return RedirectToAction(nameof(Clarifications), new { tenderId = result.TenderId });
                }
                catch (ValidationException error) { ModelState.AddModelError("", error.Message); }
                catch (CommitteeStateConflictException error) { ModelState.AddModelError("", error.Message); }
            }
            Response.StatusCode = StatusCodes.Status400BadRequest;
            if (input.TenderId <= 0) return BadRequest("Select an assigned tender before creating a clarification.");
            var page = await clarificationQueries.GetPageAsync(User, input.TenderId, cancellationToken);
            return View("Clarifications", page with { Create = input });
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> FollowUpClarification(int id, int tenderId, string? message, CancellationToken cancellationToken) =>
        await ReadAsync(async () =>
        {
            try
            {
                var result = await clarifications.FollowUpAsync(User, id, message ?? "", cancellationToken);
                TempData["ClarificationSuccess"] = "Follow-up sent.";
                return RedirectToAction(nameof(Clarifications), null, new { tenderId = result.TenderId }, fragment: $"question-{id}");
            }
            catch (Exception error) when (error is ValidationException or CommitteeStateConflictException)
            {
                TempData["ClarificationError"] = error.Message;
                return RedirectToAction(nameof(Clarifications), null, new { tenderId }, fragment: $"question-{id}");
            }
        });

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ResolveClarification(int id, CancellationToken cancellationToken) =>
        await ReadAsync(async () =>
        {
            try
            {
                var result = await clarifications.ResolveAsync(User, id, cancellationToken);
                TempData["ClarificationSuccess"] = "Resolution confirmed. Voting can resume when both sides confirm every open clarification.";
                return RedirectToAction(nameof(Clarifications), null, new { tenderId = result.TenderId }, fragment: $"question-{id}");
            }
            catch (CommitteeStateConflictException error)
            {
                TempData["ClarificationError"] = error.Message;
                return RedirectToAction(nameof(Clarifications));
            }
        });
}
