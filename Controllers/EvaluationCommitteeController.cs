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
public class EvaluationCommitteeController(ApplicationDbContext db, UserManager<ApplicationUser> users, BidRecommendationService ai) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    public IActionResult Index() => RedirectToAction(nameof(Dashboard));
    public async Task<IActionResult> Dashboard() => View(await LoadPage(null));
    public async Task<IActionResult> Evaluations(int? id) => View(await LoadPage(id));
    public async Task<IActionResult> Reports(int? id) => View(await LoadPage(id));
    public IActionResult Clarifications() => View();

    private async Task<CommitteeEvaluationPage> LoadPage(int? id)
    {
        var page = new CommitteeEvaluationPage { UserId = UserId };
        page.Tenders = await db.Tenders.AsNoTracking()
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
                    page.AiOutdated = result?.Fingerprint != BidRecommendationService.Fingerprint(page);
                    if (!page.AiOutdated && result != null)
                    {
                        BidRecommendationService.Validate(result.Recommendation, page,
                            result.Recommendation.Scenarios?.Length == 1 ? (int)saved.FinancialWeight : null);
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
    public async Task<IActionResult> RecommendBids(int id, int financialWeight, int requirementsWeight, CancellationToken cancellationToken)
    {
        var tender = await TenderQuery().AsNoTracking().SingleOrDefaultAsync(t => t.TenderId == id, cancellationToken);
        if (tender == null) return NotFound();
        var page = new CommitteeEvaluationPage { Tender = tender, UserId = UserId };
        if (!page.Members.Any(m => m.UserId == UserId)) return Forbid();
        try
        {
            if (!ModelState.IsValid || financialWeight is < 0 or > 100 || requirementsWeight is < 0 or > 100
                || financialWeight + requirementsWeight != 100)
                throw new InvalidOperationException("Financial and technical percentages must total 100%.");
            var fingerprint = BidRecommendationService.Fingerprint(page);
            var result = await ai.Recommend(page, financialWeight, cancellationToken);
            var currentTender = await TenderQuery().AsNoTracking().SingleAsync(t => t.TenderId == id, cancellationToken);
            if (!currentTender.CommitteeAssignments.Any(m => m.CommitteeType == "Evaluation" && m.UserId == UserId)) return Forbid();
            if (fingerprint != BidRecommendationService.Fingerprint(new CommitteeEvaluationPage { Tender = currentTender }))
                throw new InvalidOperationException("The bids changed during analysis. Run the comparison again.");
            db.AiAnalyses.Add(new AiAnalysis { TenderId = id, RequestedByUserId = UserId,
                FinancialWeight = financialWeight, RequirementsWeight = requirementsWeight, ModelName = ai.Model, CreatedAt = DateTime.UtcNow,
                ResultJson = JsonSerializer.Serialize(new SavedBidRecommendation(fingerprint, result), BidRecommendationService.Json) });
            await db.SaveChangesAsync(cancellationToken);
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
        TempData["CommitteeSuccess"] = "Saved successfully.";
        return RedirectToAction(nameof(Evaluations), new { id });
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
    public Task<IActionResult> SaveWeights(int id, decimal financialWeight, decimal requirementsWeight, string version) => Change(id, page =>
    {
        if (version != page.SharedVersion) return Task.FromResult<string?>("The evaluation changed. Reload before saving the weights.");
        if (financialWeight <= 0 || requirementsWeight <= 0 || financialWeight + requirementsWeight != 100
            || decimal.Round(financialWeight, 2) != financialWeight || decimal.Round(requirementsWeight, 2) != requirementsWeight)
            return Task.FromResult<string?>("Price and technical weights must be positive and total 100%.");
        if (page.Criteria.Any(c => c.Weight <= 0) || page.Members.Count != 4 || !page.Criteria.Any(c => c.Category == "Financial") || !page.Criteria.Any(c => c.Category == "Requirements"))
            return Task.FromResult<string?>("Start the committee evaluation before setting weights.");
        if (page.Criteria.Where(c => c.Category == "Financial").Sum(c => c.Weight) == financialWeight
            && page.Criteria.Where(c => c.Category == "Requirements").Sum(c => c.Weight) == requirementsWeight)
            return Task.FromResult<string?>(null);
        foreach (var category in new[] { "Financial", "Requirements" })
        {
            var criteria = page.Criteria.Where(c => c.Category == category).ToList();
            var total = criteria.Sum(c => c.Weight);
            var target = category == "Financial" ? financialWeight : requirementsWeight;
            decimal assigned = 0;
            for (var i = 0; i < criteria.Count; i++)
            {
                var value = i == criteria.Count - 1 ? target - assigned : decimal.Round(target * criteria[i].Weight / total, 2);
                if (value <= 0) return Task.FromResult<string?>("These weights are too small for the existing criteria.");
                criteria[i].Weight = value;
                assigned += value;
            }
        }
        db.TenderApprovals.RemoveRange(page.Approvals);
        foreach (var approval in page.Approvals.ToList()) page.Tender!.TenderApprovals.Remove(approval);
        foreach (var evaluation in page.Evaluations) { evaluation.Status = "Submitted"; evaluation.ApprovedAt = null; }
        return Task.FromResult<string?>(null);
    });

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> SaveReport(int id, int recommendedBidId, string? summary) => Change(id, page =>
    {
        if (!page.Members.Any(m => m.UserId == UserId && m.Position == 1)) return Task.FromResult<string?>("Only the committee manager can prepare the combined report.");
        if (!page.Offers.Any(b => b.BidId == recommendedBidId) || string.IsNullOrWhiteSpace(summary) || summary.Length > 4000)
            return Task.FromResult<string?>("Choose a compliant offer and enter a summary of up to 4,000 characters.");
        page.Tender!.EvaluationReport ??= new EvaluationReport { Status = "Draft" };
        page.Tender.EvaluationReport.RecommendedBidId = recommendedBidId;
        page.Tender.EvaluationReport.CommitteeSummary = summary.Trim();
        page.Tender.EvaluationReport.PreparedByUserId = UserId;
        return Task.FromResult<string?>(null);
    });

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Approve(int id, string version) => Change(id, page =>
    {
        if (version != page.SharedVersion) return Task.FromResult<string?>("The evaluation changed. Reload and review the latest weights before approving.");
        if (!page.WeightsReady) return Task.FromResult<string?>("Four members, eligible offers in one currency, and weights totaling 100% are required.");
        if (page.Approvals.Any(a => a.UserId == UserId)) return Task.FromResult<string?>("You have already approved this evaluation.");
        var member = page.Members.Single(m => m.UserId == UserId);
        db.TenderApprovals.Add(new TenderApproval { TenderId = id, UserId = UserId, CommitteeAssignmentId = member.CommitteeAssignmentId,
            Stage = "Evaluation", Decision = "Approved", DecidedAt = DateTime.UtcNow });
        CompleteWeightApproval(page);
        return Task.FromResult<string?>(null);
    });

    internal static void CompleteWeightApproval(CommitteeEvaluationPage page)
    {
        if (page.WeightsReady && page.Approvals.Select(a => a.UserId).Append(page.UserId).Distinct().Count() == 4)
        {
            page.Tender!.EvaluationReport ??= new EvaluationReport { PreparedByUserId = page.UserId };
            var report = page.Tender.EvaluationReport;
            if (string.IsNullOrWhiteSpace(report.CommitteeSummary))
                report.CommitteeSummary = "All four members approved the evaluation basis: "
                    + page.Criteria.Where(c => c.Category == "Financial").Sum(c => c.Weight).ToString("0.##")
                    + "% price and " + page.Criteria.Where(c => c.Category == "Requirements").Sum(c => c.Weight).ToString("0.##")
                    + "% technical requirements, applied to all eligible offers. No individual supplier scores were entered.";
            report.Status = "CommitteeApproved";
            report.SubmittedAt = DateTime.UtcNow;
            page.Tender.Status = "AwaitingOfficerSummary";
        }
    }

}
