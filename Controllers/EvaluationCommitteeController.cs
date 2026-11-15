using System.Text.Json;
using SourcingTenderManagement.Services.Evaluation;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Controllers;

[Authorize(Roles = RoleNames.EvaluationCommittee)]
public partial class EvaluationCommitteeController(ApplicationDbContext db, UserManager<ApplicationUser> users, BidRecommendationService ai) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    public IActionResult Index() => RedirectToAction(nameof(Dashboard));
    public async Task<IActionResult> Dashboard() => View(await LoadPage(null));
    public async Task<IActionResult> Evaluations(int? id) => View(await LoadPage(id));
    public async Task<IActionResult> Reports(int? id) => View(await LoadPage(id));
    public IActionResult Clarifications() => RedirectToAction("Index", "Clarification");

    private async Task<CommitteeEvaluationPage> LoadPage(int? id)
    {
        var page = new CommitteeEvaluationPage { UserId = UserId };
        page.Tenders = await db.Tenders.AsNoTracking()
            .AsSplitQuery().Include(t => t.EvaluationReport).Include(t => t.CommitteeAssignments).Include(t => t.TenderApprovals)
            .Where(t => t.Status == "UnderEvaluation" || t.CommitteeAssignments.Any(a => a.CommitteeType == "Evaluation" && a.UserId == UserId))
            .OrderByDescending(t => t.CreatedAt).ToListAsync();
        var selected = id ?? page.Tenders.FirstOrDefault()?.TenderId;
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
    public async Task<IActionResult> RecommendBids(int id, decimal financialWeight, decimal requirementsWeight, CancellationToken cancellationToken, bool writeReport = false)
    {
        var tender = await TenderQuery().AsNoTracking().SingleOrDefaultAsync(t => t.TenderId == id, cancellationToken);
        if (tender == null) return NotFound();
        var page = new CommitteeEvaluationPage { Tender = tender, UserId = UserId };
        if (!page.Members.Any(m => m.UserId == UserId)) return Forbid();
        try
        {
            if (page.Locked || !page.ConsensusLocked || !page.WeightsReady)
                throw new InvalidOperationException("All four members must confirm their percentages before AI comparison.");
            if (!ModelState.IsValid || financialWeight != page.FinancialWeight || requirementsWeight != page.TechnicalWeight)
                throw new InvalidOperationException("AI percentages must match the locked committee average.");
            if (writeReport && (page.Locked || !page.WeightsReady || financialWeight != page.Criteria.Where(c => c.Category == "Financial").Sum(c => c.Weight)))
                throw new InvalidOperationException("Generate the report with the agreed weights while the tender is under evaluation.");
            if (writeReport && page.Approvals.Select(a => a.UserId).Distinct().Count() != 4)
                throw new InvalidOperationException("All four committee members must approve before generating the final report.");
            if (writeReport && !page.SelectionReady)
                throw new InvalidOperationException("Select the committee's offer before generating its report.");
            var fingerprint = BidRecommendationService.Fingerprint(page);
            var version = page.SharedVersion;
            var result = writeReport
                ? await ai.FinalReport(page, financialWeight, cancellationToken)
                : await ai.Recommend(page, financialWeight, cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @locked int; SELECT @locked = TenderId FROM dbo.Tenders WITH (UPDLOCK, HOLDLOCK) WHERE TenderId = {id};", cancellationToken);
            var currentTender = await TenderQuery().AsNoTracking().SingleAsync(t => t.TenderId == id, cancellationToken);
            if (!currentTender.CommitteeAssignments.Any(m => m.CommitteeType == "Evaluation" && m.UserId == UserId)) return Forbid();
            if (new CommitteeEvaluationPage { Tender = currentTender }.SharedVersion != version
                || fingerprint != BidRecommendationService.Fingerprint(new CommitteeEvaluationPage { Tender = currentTender }))
                throw new InvalidOperationException("The bids changed during analysis. Run the comparison again.");
            if (writeReport)
            {
                var current = new CommitteeEvaluationPage { Tender = await TenderQuery().SingleAsync(t => t.TenderId == id, cancellationToken), UserId = UserId };
                if (current.Locked || current.SharedVersion != version) throw new InvalidOperationException("The evaluation changed during generation. Reload and try again.");
                if (current.Approvals.Select(a => a.UserId).Distinct().Count() != 4
                    || await db.Clarifications.AnyAsync(c => c.Status != "Resolved" && (c.TenderId == id || (c.Bid != null && c.Bid.TenderId == id)), cancellationToken))
                    throw new InvalidOperationException("Four approvals and resolved clarifications are required before sending the final report.");
                if (string.IsNullOrWhiteSpace(result.Report) || result.Scenarios[0].RecommendedBidId == null)
                    throw new InvalidOperationException("The AI could not produce a report with a clear recommended offer. Review the bids and try again.");
                if (!current.SelectionReady || result.Scenarios[0].RecommendedBidId != current.Tender!.EvaluationReport!.RecommendedBidId)
                    throw new InvalidOperationException("The report must describe the committee-selected offer. No changes were saved.");
                current.Tender!.EvaluationReport ??= new EvaluationReport { Status = "Draft" };
                current.Tender.EvaluationReport.CommitteeSummary = result.Report.Trim();
                current.Tender.EvaluationReport.PreparedByUserId = UserId;
                current.Tender.EvaluationReport.Status = "Draft";
                current.Tender.EvaluationReport.SubmittedAt = null;
                CompleteWeightApproval(current);
                TempData["CommitteeSuccess"] = "Final AI report generated and sent to the sourcing officer with the four approvals.";
            }
            if (!writeReport) db.AiAnalyses.Add(new AiAnalysis { TenderId = id, RequestedByUserId = UserId,
                FinancialWeight = financialWeight, RequirementsWeight = requirementsWeight, ModelName = ai.Model, CreatedAt = DateTime.UtcNow,
                ResultJson = JsonSerializer.Serialize(new SavedBidRecommendation(fingerprint, result), BidRecommendationService.Json) });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (InvalidOperationException ex) { TempData["AiError"] = ex.Message; }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or KeyNotFoundException)
        { TempData["AiError"] = "Could not reach local Ollama or finish the comparison. Check that Ollama is running, then try again."; }
        return RedirectToAction(nameof(Evaluations), new { id });
    }

    private IQueryable<Tender> TenderQuery() => db.Tenders.AsSplitQuery()
        .Include(t => t.PurchaseRequest).ThenInclude(r => r.PurchaseRequestItems)
        .Include(t => t.Invitations).ThenInclude(i => i.SupplierProfile)
        .Include(t => t.Invitations).ThenInclude(i => i.Bids.Where(b => b.ComplianceStatus == "Compliant")).ThenInclude(b => b.BidItems)
        .Include(t => t.CommitteeWeightProposals)
        .Include(t => t.EvaluationCriteria)
        .Include(t => t.CommitteeAssignments).ThenInclude(a => a.User)
        .Include(t => t.TenderApprovals)
        .Include(t => t.EvaluationReport);

    // All committee writes lock the tender first, so simultaneous saves/approvals cannot race.
    private async Task<IActionResult> Change(int id, Func<CommitteeEvaluationPage, Task<string?>> change, bool initialize = false)
    {
        if (!ModelState.IsValid)
        {
            TempData["CommitteeError"] = "Check the entered values. Scores must be 0–100 and weights must be positive.";
            return RedirectToAction(nameof(Evaluations), new { id });
        }
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @locked int; SELECT @locked = TenderId FROM dbo.Tenders WITH (UPDLOCK, HOLDLOCK) WHERE TenderId = {id};");
        var tender = await TenderQuery().SingleOrDefaultAsync(t => t.TenderId == id);
        if (tender == null) return NotFound();
        if (tender.Status != "UnderEvaluation") return BadRequest("This tender is no longer under evaluation.");
        var page = new CommitteeEvaluationPage { Tender = tender, UserId = UserId,
            Evaluations = await db.Evaluations.Include(e => e.EvaluationScores).Where(e => e.TenderId == id).ToListAsync() };
        if (!initialize && !page.Members.Any(m => m.UserId == UserId)) return Forbid();
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
            "Start" => "Committee evaluation initialized. You can now compare and choose an offer.",
            "SaveComment" => "Your comment was saved and is visible to the committee.",
            _ => "Saved successfully."
        };
        return RedirectToAction(nameof(Evaluations), new { id, tab = TempData["EvaluationTab"]?.ToString(), focus = TempData["EvaluationFocus"]?.ToString() });
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
    public Task<IActionResult> SaveComment(int id, string? comment) => Change(id, page =>
    {
        if (string.IsNullOrWhiteSpace(comment) || comment.Trim().Length > 4000)
            return Task.FromResult<string?>("Enter a comment of up to 4,000 characters.");
        if (page.Offers.Count == 0) return Task.FromResult<string?>("No eligible offers are available for this evaluation.");
        var entry = UpdateMemberComment(page, comment.Trim());
        if (db.Entry(entry).State == EntityState.Detached) db.Evaluations.Add(entry);
        return Task.FromResult<string?>(null);
    });

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
    public Task<IActionResult> SelectOffer(int id, int bidId, string version) => Change(id, page =>
    {
        if (version != page.SharedVersion) return Task.FromResult<string?>("The evaluation changed. Reload before selecting an offer.");
        if (!page.ConsensusLocked) return Task.FromResult<string?>("All four members must confirm their percentages before selecting an offer.");
        if (!page.Offers.Any(b => b.BidId == bidId)) return Task.FromResult<string?>("Select an eligible offer for this tender.");
        TempData["EvaluationTab"] = "forms";
        if (page.SelectionReady && page.Tender!.EvaluationReport!.RecommendedBidId == bidId) return Task.FromResult<string?>(null);
        page.Tender!.EvaluationReport ??= new EvaluationReport();
        var report = page.Tender.EvaluationReport;
        report.RecommendedBidId = bidId;
        report.CommitteeSummary = null;
        report.PreparedByUserId = UserId;
        report.SubmittedAt = null;
        report.Status = "Draft";
        db.TenderApprovals.RemoveRange(page.Approvals);
        foreach (var approval in page.Approvals.ToList()) page.Tender.TenderApprovals.Remove(approval);
        return Task.FromResult<string?>(null);
    });

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Approve(int id, string version) => Change(id, async page =>
    {
        if (version != page.SharedVersion) return "The evaluation changed. Reload and review the latest weights before approving.";
        if (!page.WeightsReady) return "Four members, eligible offers in one currency, and weights totaling 100% are required.";
        if (!page.SelectionReady) return "Select the committee's offer before approving.";
        if (page.Approvals.Any(a => a.UserId == UserId)) return "You have already approved this evaluation.";
        if (await db.Clarifications.AnyAsync(c => c.Status != "Resolved" &&
            (c.TenderId == id || (c.Bid != null && c.Bid.TenderId == id))))
            return "Both sides must resolve open clarifications before approving the evaluation.";
        var member = page.Members.Single(m => m.UserId == UserId);
        TempData["EvaluationTab"] = page.Approvals.Count == 3 ? "report" : "forms";
        db.TenderApprovals.Add(new TenderApproval { TenderId = id, UserId = UserId, CommitteeAssignmentId = member.CommitteeAssignmentId,
            Stage = "Evaluation", Decision = "Approved", DecidedAt = DateTime.UtcNow });
        return null;
    });

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> RejectOffer(int id, string version, string? reason) => Change(id, page =>
    {
        if (version != page.SharedVersion) return Task.FromResult<string?>("The evaluation changed. Reload before making a decision.");
        if (!page.SelectionReady) return Task.FromResult<string?>("Choose an offer before recording a decision.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 3500)
            return Task.FromResult<string?>("Provide a reason of up to 3,500 characters.");
        var report = page.Tender!.EvaluationReport!;
        var note = $"Rejected offer #{report.RecommendedBidId}: {reason.Trim()}";
        var previous = page.Evaluations.Where(e => e.CommitteeAssignmentId == page.Members.Single(m => m.UserId == UserId).CommitteeAssignmentId && !string.IsNullOrWhiteSpace(e.Notes)).OrderByDescending(e => e.SubmittedAt).FirstOrDefault()?.Notes;
        var combinedNote = string.IsNullOrEmpty(previous) ? note : $"{previous}\n\n{note}";
        if (combinedNote.Length > 4000) return Task.FromResult<string?>("Shorten your existing committee comment or the rejection reason so the combined note fits within 4,000 characters.");
        var entry = UpdateMemberComment(page, combinedNote);
        if (db.Entry(entry).State == EntityState.Detached) db.Evaluations.Add(entry);
        db.TenderApprovals.RemoveRange(page.Approvals);
        foreach (var approval in page.Approvals.ToList()) page.Tender.TenderApprovals.Remove(approval);
        report.RecommendedBidId = null;
        report.CommitteeSummary = null;
        report.SubmittedAt = null;
        report.Status = "Draft";
        TempData["EvaluationTab"] = "forms";
        return Task.FromResult<string?>(null);
    });

    internal static void CompleteWeightApproval(CommitteeEvaluationPage page)
    {
        if (page.WeightsReady && page.ReportReady && page.Approvals.Select(a => a.UserId).Distinct().Count() == 4)
        {
            page.Tender!.EvaluationReport ??= new EvaluationReport { PreparedByUserId = page.UserId };
            var report = page.Tender.EvaluationReport;
            report.Status = "CommitteeApproved";
            report.SubmittedAt = DateTime.UtcNow;
            page.Tender.Status = "AwaitingOfficerSummary";
        }
    }

}
