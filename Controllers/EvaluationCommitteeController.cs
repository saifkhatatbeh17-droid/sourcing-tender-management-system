using System.Text.Json;
using SourcingTenderManagement.Services.Evaluation;
using SourcingTenderManagement.Services;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Controllers;

// The Evaluation Committee workspace: agree the scoring weights, score each offer and approve the report.
// This class is split into several files (EvaluationCommitteeController.*.cs).
[Authorize(Roles = RoleNames.EvaluationCommittee)]
public partial class EvaluationCommitteeController(ApplicationDbContext db,
    UserManager<ApplicationUser> users,
    BidRecommendationService ai,
    ILogger<EvaluationCommitteeController> logger) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    public IActionResult Index() => RedirectToAction(nameof(Dashboard));
    public async Task<IActionResult> Dashboard() => View(await LoadPage(null));
    public async Task<IActionResult> Evaluations(int? id, int? offerId = null)
    {
        var page = await LoadPage(id, selectFirst: false);
        if (offerId.HasValue && !page.Offers.Any(b => b.BidId == offerId.Value)) return NotFound();
        page.ReviewOfferId = offerId;
        return View(page);
    }
    public async Task<IActionResult> Reports(int? id)
    {
        var page = await LoadPage(id, selectFirst: false);
        return View(page);
    }
    public IActionResult Clarifications() => RedirectToAction("Index", "Clarification");

    private async Task<CommitteeEvaluationPage> LoadPage(int? id, bool selectFirst = true)
    {
        var page = new CommitteeEvaluationPage { UserId = UserId };
        page.Tenders = await db.Tenders.AsNoTracking()
            .AsSplitQuery().Include(t => t.EvaluationReport)
                .Include(t => t.CommitteeAssignments)
                .Include(t => t.TenderApprovals)
            .InEvaluationWorkspace(UserId)
            .OrderByDescending(t => t.CreatedAt).ToListAsync();
        var selected = id ?? (selectFirst ? page.Tenders.FirstOrDefault()?.TenderId : null);
        if (selected != null && page.Tenders.Any(t => t.TenderId == selected))
        {
            page.Tender = await TenderQuery().SingleAsync(t => t.TenderId == selected);
            page.Evaluations = await db.Evaluations.AsNoTracking().Where(e => e.TenderId == selected)
                .Include(e => e.EvaluationScores).ToListAsync();
        }
        if (page.Tender != null)
        {
            var saved = await db.AiAnalyses.AsNoTracking().Where(a => a.TenderId == page.Tender.TenderId)
                .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.AiAnalysisId).FirstOrDefaultAsync();
            if (saved != null)
            {
                try
                {
                    var result = JsonSerializer.Deserialize<SavedBidRecommendation>(saved.ResultJson, BidRecommendationService.Json);
                    page.AiOutdated = result?.Fingerprint != BidRecommendationService.Fingerprint(page)
                        || !page.PercentageReady || saved.FinancialWeight != page.FinancialWeight || saved.RequirementsWeight != page.TechnicalWeight;
                    if (!page.AiOutdated && result != null)
                    {
                        BidRecommendationService.Validate(result.Recommendation, page,
                            result.Recommendation.Scenarios?.Length == 1 ? saved.FinancialWeight : null);
                        page.AiRecommendation = result.Recommendation;
                        page.AiCreatedAt = saved.CreatedAt;
                    }
                }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException) { page.AiOutdated = true; }
            }
        }
        return page;
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RecommendBids(int id,
        decimal financialWeight,
        decimal requirementsWeight,
        CancellationToken cancellationToken,
        bool writeReport = false)
    {
        var tender = await TenderQuery().AsNoTracking().SingleOrDefaultAsync(t => t.TenderId == id, cancellationToken);
        if (tender == null) return NotFound();
        var page = new CommitteeEvaluationPage { Tender = tender, UserId = UserId,
            Evaluations = await db.Evaluations.AsNoTracking().Where(e => e.TenderId == id).ToListAsync(cancellationToken) };
        if (!page.Members.Any(m => m.UserId == UserId)) return Forbid();
        if (writeReport && !page.IsDepartmentManager) return Forbid();
        try
        {
            if (page.Locked || !page.ConsensusLocked || !page.WeightsReady)
                throw new InvalidOperationException("All four members must confirm their percentages before AI comparison.");
            if (!ModelState.IsValid
                || financialWeight != page.FinancialWeight
                || requirementsWeight != page.TechnicalWeight)
                throw new InvalidOperationException("AI percentages must match the locked committee average.");
            if (writeReport && (page.Locked || !page.WeightsReady || financialWeight != page.FinancialWeight))
                throw new InvalidOperationException("Generate the report with the agreed weights while the tender is under evaluation.");
            if (writeReport && page.Approvals.Select(a => a.UserId).Distinct().Count() != 4)
                throw new InvalidOperationException("All four committee members must approve before generating the final report.");
            if (writeReport && !page.SelectionReady)
                throw new InvalidOperationException("Complete every offer review and resolve any score tie before generating the report.");
            if (writeReport && !AllOfferScoring.Complete(page))
                throw new InvalidOperationException("All four members must submit financial and technical marks before generating the report.");
            var fingerprint = BidRecommendationService.Fingerprint(page);
            var version = page.SharedVersion;
            var result = writeReport
                ? await ai.FinalReport(page, financialWeight, cancellationToken)
                : await ai.Recommend(page, financialWeight, cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            // Lock this row until the transaction ends, so two people cannot change the same record at the same time.
            await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @locked int; SELECT @locked = TenderId FROM dbo.Tenders WITH (UPDLOCK, HOLDLOCK) WHERE TenderId = {id};", cancellationToken);
            var currentTender = await TenderQuery().AsNoTracking()
                .SingleAsync(t => t.TenderId == id, cancellationToken);
            if (!currentTender.CommitteeAssignments.Any(m => m.CommitteeType == "Evaluation"
                && m.UserId == UserId)) return Forbid();
            if (writeReport
                && !currentTender.CommitteeAssignments.Any(m => m.CommitteeType == "Evaluation" && m.Position == 1 && m.UserId == UserId)) return Forbid();
            var currentReviews = await db.Evaluations.AsNoTracking()
                .Where(e => e.TenderId == id)
                .ToListAsync(cancellationToken);
            if (new CommitteeEvaluationPage { Tender = currentTender, Evaluations = currentReviews }.SharedVersion != version
                || fingerprint != BidRecommendationService.Fingerprint(new CommitteeEvaluationPage { Tender = currentTender }))
                throw new InvalidOperationException("The bids changed during analysis. Run the comparison again.");
            if (writeReport)
            {
                var current = new CommitteeEvaluationPage { Tender = await TenderQuery().SingleAsync(t => t.TenderId == id, cancellationToken),
                    UserId = UserId,
                    Evaluations = currentReviews };
                if (current.Locked
                    || current.SharedVersion != version) throw new InvalidOperationException("The evaluation changed during generation. Reload and try again.");
                if (current.Approvals.Select(a => a.UserId).Distinct().Count() != 4
                    || await db.Clarifications.AnyAsync(c => c.Status != "Resolved" && (c.TenderId == id || (c.Bid != null && c.Bid.TenderId == id)), cancellationToken))
                    throw new InvalidOperationException("Four approvals and resolved clarifications are required before sending the final report.");
                if (string.IsNullOrWhiteSpace(result.Report) || result.Scenarios[0].RecommendedBidId == null)
                    throw new InvalidOperationException("The AI could not produce a report with a clear recommended offer. Review the bids and try again.");
                if (!current.SelectionReady
                    || result.Scenarios[0].RecommendedBidId != current.Tender!.EvaluationReport!.RecommendedBidId)
                    throw new InvalidOperationException("The report must describe the committee-selected offer. No changes were saved.");
                current.Tender!.EvaluationReport ??= new EvaluationReport { Status = "Draft" };
                current.Evaluations = await db.Evaluations.AsNoTracking()
                    .Where(e => e.TenderId == id)
                    .ToListAsync(cancellationToken);
                if (!AllOfferScoring.Complete(current)) throw new InvalidOperationException("All four member marks are required.");
                current.Tender.EvaluationReport.CommitteeSummary = result.Report.Trim() + "\n\n" + AllOfferScoring
                    .Report(current);
                current.Tender.EvaluationReport.PreparedByUserId = UserId;
                current.Tender.EvaluationReport.Status = "Draft";
                current.Tender.EvaluationReport.SubmittedAt = null;
                CompleteWeightApproval(current);
                TempData["CommitteeSuccess"] = result.MissingInformation.Length > 0
                    ? "Top-three report sent using saved scores and quotation facts. The AI narrative could not be verified and was omitted."
                    : "Top-three committee report generated and sent to the sourcing officer. The manager summary will cover the winner only.";
            }
            if (!writeReport) db.AiAnalyses.Add(new AiAnalysis { TenderId = id, RequestedByUserId = UserId,
                FinancialWeight = financialWeight, RequirementsWeight = requirementsWeight,
                ModelName = result.ModelName
                    ?? throw new InvalidOperationException("The comparison did not identify its AI model."),
                CreatedAt = DateTime.UtcNow,
                ResultJson = JsonSerializer.Serialize(new SavedBidRecommendation(fingerprint, result), BidRecommendationService.Json) });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            // The user-facing text hides which check failed; keep the real reason in the server log.
            logger.LogWarning("AI comparison for tender {TenderId} rejected: {Reason}", id, ex.InnerException?.Message
                ?? ex.Message);
            TempData["AiError"] = ex.Message;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or KeyNotFoundException)
        { logger.LogWarning(ex, "AI comparison for tender {TenderId} failed to reach the model or read its answer", id);
          TempData["AiError"] = "Could not reach the vLLM / AI endpoint or finish the comparison. Check your Vllm configuration or API key, then try again."; }
        return RedirectToAction(nameof(Evaluations), new { id });
    }

    private IQueryable<Tender> TenderQuery() => db.Tenders.AsSplitQuery()
        .Include(t => t.PurchaseRequest).ThenInclude(r => r.PurchaseRequestItems)
        .Include(t => t.Invitations).ThenInclude(i => i.SupplierProfile)
        .Include(t => t.Invitations).ThenInclude(i => i.Bids.Where(b => b.ComplianceStatus == "Compliant"))
            .ThenInclude(b => b.BidItems)
        .Include(t => t.CommitteeWeightProposals)
        .Include(t => t.CommitteeTieVotes)
        .Include(t => t.EvaluationCriteria)
        .Include(t => t.CommitteeAssignments).ThenInclude(a => a.User)
        .Include(t => t.TenderApprovals)
        .Include(t => t.OfferExtensions)
        .Include(t => t.EvaluationReport);

    // All committee writes lock the tender first, so simultaneous saves/approvals cannot race.
    private async Task<IActionResult> Change(int id, Func<CommitteeEvaluationPage, Task<string?>> change, bool initialize = false)
    {
        // Navigation hints belong to this save, not an earlier tender or browser tab.
        TempData.Remove("EvaluationOffer");
        TempData.Remove("EvaluationFocus");
        TempData.Remove("EvaluationTab");
        if (!ModelState.IsValid)
        {
            TempData["CommitteeError"] = "Check the entered values. Scores must be 0–100 and weights must be positive.";
            return RedirectToAction(nameof(Evaluations), new { id });
        }
        await using var transaction = await db.Database.BeginTransactionAsync();
        // Lock this row until the transaction ends, so two people cannot change the same record at the same time.
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @locked int; SELECT @locked = TenderId FROM dbo.Tenders WITH (UPDLOCK, HOLDLOCK) WHERE TenderId = {id};");
        var tender = await TenderQuery().SingleOrDefaultAsync(t => t.TenderId == id);
        if (tender == null) return NotFound();
        if (tender.Status != "UnderEvaluation") return BadRequest("This tender is no longer under evaluation.");
        var page = new CommitteeEvaluationPage { Tender = tender, UserId = UserId,
            Evaluations = await db.Evaluations.Include(e => e.EvaluationScores).Where(e => e.TenderId == id).ToListAsync() };
        if (!initialize && !page.Members.Any(m => m.UserId == UserId)) return Forbid();
        if (!initialize
            && ControllerContext.ActionDescriptor.ActionName != nameof(ConfirmPercentage)
            && !page.ConsensusLocked)
        {
            TempData["CommitteeError"] = "Evaluation is locked until all four members confirm their percentages.";
            return RedirectToAction(nameof(Evaluations), new { id });
        }
        var error = await change(page);
        if (error != null)
        {
            TempData["CommitteeError"] = error;
            return RedirectToAction(nameof(Evaluations), new { id });
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        TempData["CommitteeSuccess"] = ControllerContext.ActionDescriptor.ActionName switch
        {
            "Approve" => "Your approval was recorded. The final report is available after all four members approve.",
            "SelectOffer" => "The committee-selected offer was saved. Review it with the shared percentages before approving.",
            "ConfirmPercentage" => "Your percentage is confirmed and locked. The average locks after all four members confirm.",
            "RejectOffer" => "Your rejection was recorded and the selected offer was returned for review.",
            "Start" => "Committee evaluation initialized. Confirm your percentage, then review every offer.",
            "ScoreOffer" => page.SelectionReady ? "All reviews are complete. The highest-scoring offer is ready for its report." : "Your marks are saved and locked for this offer.",
            "ResolveScoreTie" => page.SelectionReady ? "The committee resolved the tie. The winning offer is ready for its report." : "Tie decision recorded. All four members must agree.",
            "SaveComment" => "Your comment was saved and is visible to the committee.",
            _ => "Saved successfully."
        };
        return RedirectToAction(nameof(Evaluations), new { id,
            tab = TempData["EvaluationTab"]?.ToString(),
            focus = TempData["EvaluationFocus"]?.ToString(),
            offerId = TempData["EvaluationOffer"] });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Start(int id) => Change(id, async page =>
    {
        if (page.Members.Count != 0) return "This evaluation has already been started.";
        if (page.Offers.Count == 0) return "At least one compliant offer is required.";
        var members = new List<ApplicationUser>();
        foreach (var role in RoleNames.EvaluationPositions)
        {
            var candidates = (await users.GetUsersInRoleAsync(role)).Where(u => u.IsActive).ToList();
            if (candidates.Count != 1 || !await users.IsInRoleAsync(candidates[0], RoleNames.EvaluationCommittee))
                return $"Admin must provide exactly one active committee account for {role}.";
            members.Add(candidates[0]);
        }
        if (members.Select(m => m.Id).Distinct().Count() != 4) return "Four different committee accounts are required.";
        if (!members.Any(m => m.Id == UserId)) return "Your account is not one of the four committee members.";
        for (var i = 0; i < members.Count; i++)
            page.Tender!.CommitteeAssignments.Add(new CommitteeAssignment { UserId = members[i].Id, CommitteeType = "Evaluation", Position = i + 1 });
        if (page.Criteria.Count == 0)
        {
            page.Tender!.EvaluationCriteria.Add(new EvaluationCriterion { Name = "Price", Category = "Financial", Weight = 40 });
            page.Tender.EvaluationCriteria.Add(new EvaluationCriterion { Name = "Technical compliance", Category = "Requirements", Weight = 60 });
        }
        page.Tender!.EvaluationReport ??= new EvaluationReport { Status = "Draft", PreparedByUserId = UserId };
        TempData["EvaluationTab"] = "offers";
        TempData["EvaluationFocus"] = "ai";
        return null;
    }, initialize: true);

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult SaveComment(int id) => BadRequest("Add your comment to the review for each offer. Submitted reviews are locked.");

    internal static Evaluation UpdateMemberComment(CommitteeEvaluationPage page, string comment)
    {
        var member = page.Members.Single(m => m.UserId == page.UserId);
        var bid = page.Offers.First();
        var entry = page.Evaluation(member, bid);
        if (entry == null)
        {
            entry = new Evaluation { TenderId = page.Tender!.TenderId, BidId = bid.BidId,
                CommitteeAssignmentId = member.CommitteeAssignmentId, Status = "Draft" };
            page.Evaluations.Add(entry);
        }
        entry.Notes = comment;
        entry.SubmittedAt = DateTime.UtcNow;
        return entry;
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult SaveWeights(int id) => BadRequest("Each member must confirm their own percentage. The calculated average cannot be edited.");

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult SelectOffer(int id) => BadRequest("Every offer must be reviewed. The highest average score determines the recommended offer.");

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Approve(int id) => BadRequest("Submit a financial and technical review for each offer using its scoring form.");

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult RejectOffer(int id) => BadRequest("Evaluate every offer using its financial and technical marks. Manual selection and rejection " +
            "are unavailable in the scoring cycle.");

    internal static void CompleteWeightApproval(CommitteeEvaluationPage page)
    {
        if (page.WeightsReady
            && page.ReportReady
            && page.SelectionReady
            && AllOfferScoring.Complete(page)
            && page.Approvals
            .Select(a => a.UserId)
            .Distinct()
            .Count() == 4)
        {
            page.Tender!.EvaluationReport ??= new EvaluationReport { PreparedByUserId = page.UserId };
            var report = page.Tender.EvaluationReport;
            report.Status = "CommitteeApproved";
            report.SubmittedAt = DateTime.UtcNow;
            page.Tender.Status = "AwaitingOfficerSummary";
        }
    }

}
