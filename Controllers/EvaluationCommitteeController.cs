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
                        || (page.UsesScoredWorkflow && (!page.ConsensusLocked || saved.FinancialWeight != page.FinancialWeight
                            || saved.RequirementsWeight != page.TechnicalWeight));
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
    public async Task<IActionResult> RecommendBids(int id, decimal? financialWeight, decimal? requirementsWeight, CancellationToken cancellationToken, bool writeReport = false)
    {
        var page = await LoadPage(id);
        if (page.Tender?.TenderId != id) return NotFound();
        if (!await IsActiveMember(page, cancellationToken)) return Forbid();
        try
        {
            if (writeReport) throw new InvalidOperationException("The score report is generated automatically after every offer has four accepted reviews.");
            if (page.Locked || !page.WeightsReady) throw new InvalidOperationException("All four percentages must be confirmed before AI comparison.");
            if (!ModelState.IsValid || financialWeight != page.FinancialWeight || requirementsWeight != page.TechnicalWeight)
                throw new InvalidOperationException("AI comparison must use the locked committee percentages.");
            var version = page.SharedVersion;
            var fingerprint = BidRecommendationService.Fingerprint(page);
            var result = await ai.Recommend(page, page.FinancialWeight, cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @locked int; SELECT @locked = TenderId FROM dbo.Tenders WITH (UPDLOCK, HOLDLOCK) WHERE TenderId = {id};", cancellationToken);
            // LoadPage also reads marks and tie decisions, so an analysis cannot save across a scoring change.
            db.ChangeTracker.Clear();
            var current = await LoadPage(id);
            if (!await IsActiveMember(current, cancellationToken)) return Forbid();
            if (current.Locked || !current.WeightsReady || current.SharedVersion != version
                || BidRecommendationService.Fingerprint(current) != fingerprint)
                throw new InvalidOperationException("The evaluation changed during comparison. Reload and try again.");
            db.AiAnalyses.Add(new AiAnalysis { TenderId = id, RequestedByUserId = UserId,
                FinancialWeight = page.FinancialWeight, RequirementsWeight = page.TechnicalWeight, ModelName = ai.Model, CreatedAt = DateTime.UtcNow,
                ResultJson = JsonSerializer.Serialize(new SavedBidRecommendation(fingerprint, result), BidRecommendationService.Json) });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (InvalidOperationException ex) { TempData["AiError"] = ex.Message; }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or KeyNotFoundException)
        { TempData["AiError"] = "Could not finish the AI comparison. Check that Ollama is running, then try again. Your marks are unchanged."; }
        return RedirectToAction(nameof(Evaluations), new { id });
    }

    private async Task<bool> IsActiveMember(CommitteeEvaluationPage page, CancellationToken cancellationToken = default) =>
        page.Members.Any(m => m.UserId == UserId && m.User.IsActive) && await db.Users.AnyAsync(u => u.Id == UserId && u.IsActive &&
            db.UserRoles.Any(ur => ur.UserId == u.Id && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == RoleNames.EvaluationCommittee)), cancellationToken);

    private IQueryable<Tender> TenderQuery() => db.Tenders.AsSplitQuery()
        .Include(t => t.PurchaseRequest).ThenInclude(r => r.PurchaseRequestItems)
        .Include(t => t.Invitations).ThenInclude(i => i.SupplierProfile)
        .Include(t => t.Invitations).ThenInclude(i => i.Bids.Where(b => b.ComplianceStatus == "Compliant")).ThenInclude(b => b.BidItems)
        .Include(t => t.EvaluationCriteria)
        .Include(t => t.CommitteeWeightProposals)
        .Include(t => t.CommitteeTieVotes)
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
        if (!initialize && !await IsActiveMember(page)) return Forbid();
        var assignedIds = page.Members.Select(m => m.UserId).ToList();
        if (!initialize && await db.Users.CountAsync(u => u.IsActive && assignedIds.Contains(u.Id) &&
            db.UserRoles.Any(ur => ur.UserId == u.Id && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == RoleNames.EvaluationCommittee))) != 4)
        {
            TempData["CommitteeError"] = "Four active assigned committee members are required. Ask the administrator to check the committee accounts.";
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
            "ConfirmPercentage" => page.ConsensusLocked ? "All four percentages are confirmed. The average is locked; review every offer." : "Your percentage is confirmed and locked. Waiting for the other committee members.",
            "AcceptScoredOffer" => page.Locked ? "All reviews are complete. The highest-scoring offer and report were sent to the sourcing officer." : "Your marks are accepted and locked for this offer.",
            "DecideTie" => page.Locked ? "All four members agreed. The tied offer and report were sent to the sourcing officer." : "Your tie decision is recorded. All four members must agree on the same tied offer.",
            "Start" => "Committee evaluation initialized. You can now compare and choose an offer.",
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
        TempData["EvaluationTab"] = "forms";
        return null;
    }, initialize: true);

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult SaveComment(int id) => BadRequest("Add review notes to each offer form before accepting it.");

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult SaveWeights(int id) => BadRequest("Confirm your own percentage in the committee percentage step. Confirmed percentages cannot be edited.");
    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult SelectOffer(int id) => BadRequest("The winning offer is determined automatically after all member score forms are accepted.");
    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Approve(int id) => BadRequest("Accept a financial and technical score form for every offer.");
    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult RejectOffer(int id) => BadRequest("Review each eligible offer in Evaluation forms. Manual offer selection is unavailable.");

}
