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

// ═══════════════════════════════════════════════════════════════════════════
// Evaluation Committee Controller
// ═══════════════════════════════════════════════════════════════════════════
// This controller handles everything the Evaluation Committee does:
//   • View tenders assigned to them (Dashboard, Evaluations, Reports)
//   • Start an evaluation (assign the four committee members)
//   • Confirm percentage weights (financial vs technical)
//   • Score each offer (financial and technical marks)
//   • Run AI comparison / generate final report
//   • Approve the report (all four members must approve)
//
// The class is split across several files:
//   - EvaluationCommitteeController.cs           ← this file (main logic)
//   - EvaluationCommitteeController.Consensus.cs  ← confirm percentage action
//   - EvaluationCommitteeController.OfferScoring.cs ← score offer + tie-break
//   - EvaluationCommitteeController.Pdf.cs        ← PDF downloads
// ═══════════════════════════════════════════════════════════════════════════
[Authorize(Roles = RoleNames.EvaluationCommittee)]
public partial class EvaluationCommitteeController(
    ApplicationDbContext db,
    UserManager<ApplicationUser> users,
    BidRecommendationService ai,
    ILogger<EvaluationCommitteeController> logger) : Controller
{
    // The currently logged-in user's ID (read from the authentication cookie).
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    // ───────────────────────────────────────────────────────────────────
    // Page Actions (GET requests that show pages)
    // ───────────────────────────────────────────────────────────────────

    // GET /EvaluationCommittee/Index: Redirects to the dashboard.
    public IActionResult Index() => RedirectToAction(nameof(Dashboard));

    // GET /EvaluationCommittee/Dashboard: Shows the committee dashboard page.
    public async Task<IActionResult> Dashboard() => View(await LoadPage(null));

    // GET /EvaluationCommittee/Evaluations: Shows the tender evaluations page.
    // If offerId is provided, opens the page focused on that specific offer.
    public async Task<IActionResult> Evaluations(int? id, int? offerId = null)
    {
        var page = await LoadPage(id, selectFirst: false);

        // Make sure the requested offer actually exists in this tender.
        if (offerId.HasValue && !page.Offers.Any(b => b.BidId == offerId.Value))
            return NotFound();

        page.ReviewOfferId = offerId;
        return View(page);
    }

    // GET /EvaluationCommittee/Reports: Shows the committee report page.
    public async Task<IActionResult> Reports(int? id)
    {
        var page = await LoadPage(id, selectFirst: false);
        return View(page);
    }

    // GET /EvaluationCommittee/Clarifications: Opens the shared clarifications page.
    public IActionResult Clarifications() => RedirectToAction("Index", "Clarification");

    // ───────────────────────────────────────────────────────────────────
    // LoadPage – Builds the view model for every page in this area
    // ───────────────────────────────────────────────────────────────────

    private async Task<CommitteeEvaluationPage> LoadPage(int? id, bool selectFirst = true)
    {
        var page = new CommitteeEvaluationPage { UserId = UserId };

        // Step 1: Load every tender under evaluation, plus any tender
        // (in any status) where this user is on the evaluation committee.
        page.Tenders = await db.Tenders.AsNoTracking()
            .AsSplitQuery()
            .Include(t => t.EvaluationReport)
            .Include(t => t.CommitteeAssignments)
            .Include(t => t.TenderApprovals)
            .InEvaluationWorkspace(UserId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        // Step 2: Pick which tender to show in detail.
        //   - If the URL has ?id=X, use that tender.
        //   - If selectFirst is true and no id given, pick the most recent one.
        var selected = id ?? (selectFirst ? page.Tenders.FirstOrDefault()?.TenderId : null);

        // Step 3: Load the full details of the selected tender.
        if (selected != null && page.Tenders.Any(t => t.TenderId == selected))
        {
            page.Tender = await TenderQuery().SingleAsync(t => t.TenderId == selected);

            page.Evaluations = await db.Evaluations.AsNoTracking()
                .Where(e => e.TenderId == selected)
                .Include(e => e.EvaluationScores)
                .ToListAsync();
        }

        // Step 4: If a tender is selected, try to load any saved AI recommendation.
        if (page.Tender != null)
            await TryLoadSavedAiRecommendation(page);

        return page;
    }

    /// <summary>
    /// Loads the most recent AI analysis for this tender (if one exists)
    /// and checks whether it is still up to date or needs to be re-run.
    /// </summary>
    private async Task TryLoadSavedAiRecommendation(CommitteeEvaluationPage page)
    {
        // Find the most recent AI analysis for this tender.
        var saved = await db.AiAnalyses.AsNoTracking()
            .Where(a => a.TenderId == page.Tender!.TenderId)
            .OrderByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.AiAnalysisId)
            .FirstOrDefaultAsync();

        if (saved == null)
            return; // No previous AI analysis exists.

        try
        {
            var result = JsonSerializer.Deserialize<SavedBidRecommendation>(
                saved.ResultJson, BidRecommendationService.Json);

            // Check whether the saved analysis is outdated
            // (tender details, requirements, offers or weights changed since the analysis ran,
            // or the percentages are not ready).
            bool isOutdated =
                result?.Fingerprint != BidRecommendationService.Fingerprint(page)
                || !page.PercentageReady
                || saved.FinancialWeight != page.FinancialWeight
                || saved.RequirementsWeight != page.TechnicalWeight;

            page.AiOutdated = isOutdated;

            // Only use the saved recommendation if it is still current.
            if (!isOutdated && result != null)
            {
                BidRecommendationService.Validate(
                    result.Recommendation, page,
                    result.Recommendation.Scenarios?.Length == 1 ? saved.FinancialWeight : null);

                page.AiRecommendation = result.Recommendation;
                page.AiCreatedAt = saved.CreatedAt;
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // The saved data is corrupt or incompatible — mark it as outdated.
            page.AiOutdated = true;
        }
    }

    // ───────────────────────────────────────────────────────────────────
    // RecommendBids – Run AI comparison or generate the final report
    // ───────────────────────────────────────────────────────────────────

    // POST /EvaluationCommittee/RecommendBids: Runs the AI comparison or writes the final report.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RecommendBids(
        int id,
        decimal financialWeight,
        decimal requirementsWeight,
        CancellationToken cancellationToken,
        bool writeReport = false)
    {
        // --- Load the tender and check basic permissions ---

        var tender = await TenderQuery().AsNoTracking()
            .SingleOrDefaultAsync(t => t.TenderId == id, cancellationToken);
        if (tender == null)
            return NotFound();

        var page = new CommitteeEvaluationPage
        {
            Tender = tender,
            UserId = UserId,
            Evaluations = await db.Evaluations.AsNoTracking()
                .Where(e => e.TenderId == id)
                .ToListAsync(cancellationToken)
        };

        // Only committee members can run the AI comparison.
        if (!page.Members.Any(m => m.UserId == UserId))
            return Forbid();

        // Only the department manager (position 1) can generate the final report.
        if (writeReport && !page.IsDepartmentManager)
            return Forbid();

        try
        {
            // --- Validate that the committee is ready ---

            ValidateForAiComparison(page, ModelState.IsValid,
                financialWeight, requirementsWeight, writeReport);

            // --- Call the AI service ---

            var fingerprint = BidRecommendationService.Fingerprint(page);
            var version = page.SharedVersion;

            var result = writeReport
                ? await ai.FinalReport(page, financialWeight, cancellationToken)
                : await ai.Recommend(page, financialWeight, cancellationToken);

            // --- Lock the tender row and re-verify nothing changed ---

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await LockTenderRow(id, cancellationToken);

            var currentTender = await TenderQuery().AsNoTracking()
                .SingleAsync(t => t.TenderId == id, cancellationToken);

            // Re-check membership after acquiring the lock.
            if (!currentTender.CommitteeAssignments.Any(m =>
                m.CommitteeType == "Evaluation" && m.UserId == UserId))
                return Forbid();

            if (writeReport && !currentTender.CommitteeAssignments.Any(m =>
                m.CommitteeType == "Evaluation" && m.Position == 1 && m.UserId == UserId))
                return Forbid();

            // Re-check that the bids have not changed since we started the AI call.
            var currentReviews = await db.Evaluations.AsNoTracking()
                .Where(e => e.TenderId == id)
                .ToListAsync(cancellationToken);

            if (new CommitteeEvaluationPage { Tender = currentTender, Evaluations = currentReviews }
                    .SharedVersion != version
                || fingerprint != BidRecommendationService.Fingerprint(
                    new CommitteeEvaluationPage { Tender = currentTender }))
                throw new InvalidOperationException(
                    "The bids changed during analysis. Run the comparison again.");

            // --- Save the result ---

            if (writeReport)
                await SaveFinalReport(page, result, id, version, financialWeight,
                    currentReviews, cancellationToken);
            else
                SaveAiAnalysis(id, financialWeight, requirementsWeight, fingerprint, result);

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            // The user-facing text hides which check failed; keep the real reason in the server log.
            logger.LogWarning("AI comparison for tender {TenderId} rejected: {Reason}",
                id, ex.InnerException?.Message ?? ex.Message);
            TempData["AiError"] = ex.Message;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
            or TaskCanceledException or KeyNotFoundException)
        {
            logger.LogWarning(ex,
                "AI comparison for tender {TenderId} failed to reach the model or read its answer", id);
            TempData["AiError"] =
                "Could not reach the vLLM / AI endpoint or finish the comparison. "
                + "Check your Vllm configuration or API key, then try again.";
        }

        return RedirectToAction(nameof(Evaluations), new { id });
    }

    /// <summary>
    /// Checks all the preconditions before running the AI comparison or final report.
    /// Throws InvalidOperationException with a user-facing message if anything fails.
    /// </summary>
    private static void ValidateForAiComparison(
        CommitteeEvaluationPage page,
        bool formIsValid,
        decimal financialWeight,
        decimal requirementsWeight,
        bool writeReport)
    {
        // All four members must lock their percentage before any AI work.
        if (page.Locked || !page.ConsensusLocked || !page.WeightsReady)
            throw new InvalidOperationException(
                "All four members must confirm their percentages before AI comparison.");

        // The submitted weights must match the locked committee average exactly.
        if (!formIsValid
            || financialWeight != page.FinancialWeight
            || requirementsWeight != page.TechnicalWeight)
            throw new InvalidOperationException(
                "AI percentages must match the locked committee average.");

        if (!writeReport)
            return; // The checks below only apply when generating the final report.

        if (page.Locked || !page.WeightsReady || financialWeight != page.FinancialWeight)
            throw new InvalidOperationException(
                "Generate the report with the agreed weights while the tender is under evaluation.");

        if (page.Approvals.Select(a => a.UserId).Distinct().Count() != 4)
            throw new InvalidOperationException(
                "All four committee members must approve before generating the final report.");

        if (!page.SelectionReady)
            throw new InvalidOperationException(
                "Complete every offer review and resolve any score tie before generating the report.");

        if (!AllOfferScoring.Complete(page))
            throw new InvalidOperationException(
                "All four members must submit financial and technical marks before generating the report.");
    }

    /// <summary>
    /// Writes the final committee report after the AI generates it.
    /// Called inside the transaction, after the tender row is locked.
    /// </summary>
    private async Task SaveFinalReport(
        CommitteeEvaluationPage page,
        BidRecommendation result,
        int id,
        string version,
        decimal financialWeight,
        List<Evaluation> currentReviews,
        CancellationToken cancellationToken)
    {
        var current = new CommitteeEvaluationPage
        {
            Tender = await TenderQuery().SingleAsync(t => t.TenderId == id, cancellationToken),
            UserId = UserId,
            Evaluations = currentReviews
        };

        // Re-verify nothing changed during AI generation.
        if (current.Locked || current.SharedVersion != version)
            throw new InvalidOperationException(
                "The evaluation changed during generation. Reload and try again.");

        // All four approvals and no open clarifications.
        if (current.Approvals.Select(a => a.UserId).Distinct().Count() != 4
            || await db.Clarifications.AnyAsync(c =>
                c.Status != "Resolved"
                && (c.TenderId == id || (c.Bid != null && c.Bid.TenderId == id)),
                cancellationToken))
            throw new InvalidOperationException(
                "Four approvals and resolved clarifications are required "
                + "before sending the final report.");

        // The AI must produce a valid report with a clear winner.
        if (string.IsNullOrWhiteSpace(result.Report)
            || result.Scenarios[0].RecommendedBidId == null)
            throw new InvalidOperationException(
                "The AI could not produce a report with a clear recommended offer. "
                + "Review the bids and try again.");

        // The report must describe the committee-selected offer (not a different one).
        if (!current.SelectionReady
            || result.Scenarios[0].RecommendedBidId
                != current.Tender!.EvaluationReport!.RecommendedBidId)
            throw new InvalidOperationException(
                "The report must describe the committee-selected offer. No changes were saved.");

        // All four members must have submitted their marks.
        current.Tender!.EvaluationReport ??= new EvaluationReport { Status = "Draft" };
        current.Evaluations = await db.Evaluations.AsNoTracking()
            .Where(e => e.TenderId == id)
            .ToListAsync(cancellationToken);

        if (!AllOfferScoring.Complete(current))
            throw new InvalidOperationException("All four member marks are required.");

        // Write the report: AI narrative + scoring table.
        current.Tender.EvaluationReport.CommitteeSummary =
            result.Report.Trim() + "\n\n" + AllOfferScoring.Report(current);
        current.Tender.EvaluationReport.PreparedByUserId = UserId;
        current.Tender.EvaluationReport.Status = "Draft";
        current.Tender.EvaluationReport.SubmittedAt = null;

        // If all conditions are met, mark the report as committee-approved.
        CompleteWeightApproval(current);

        TempData["CommitteeSuccess"] = result.MissingInformation.Length > 0
            ? "Top-three report sent using saved scores and quotation facts. "
              + "The AI narrative could not be verified and was omitted."
            : "Top-three committee report generated and sent to the sourcing officer. "
              + "The manager summary will cover the winner only.";
    }

    /// <summary>
    /// Saves an AI comparison result (not the final report) for later viewing.
    /// </summary>
    private void SaveAiAnalysis(
        int tenderId,
        decimal financialWeight,
        decimal requirementsWeight,
        string fingerprint,
        BidRecommendation result)
    {
        db.AiAnalyses.Add(new AiAnalysis
        {
            TenderId = tenderId,
            RequestedByUserId = UserId,
            FinancialWeight = financialWeight,
            RequirementsWeight = requirementsWeight,
            ModelName = result.ModelName
                ?? throw new InvalidOperationException(
                    "The comparison did not identify its AI model."),
            CreatedAt = DateTime.UtcNow,
            ResultJson = JsonSerializer.Serialize(
                new SavedBidRecommendation(fingerprint, result),
                BidRecommendationService.Json)
        });
    }

    // ───────────────────────────────────────────────────────────────────
    // TenderQuery – Loads a tender with all related data in one go
    // ───────────────────────────────────────────────────────────────────
    // This is the "full" query used whenever we need a tender's complete
    // details: purchase request items, supplier profiles, compliant bids,
    // committee members, criteria, votes, approvals, and report.

    private IQueryable<Tender> TenderQuery() => db.Tenders.AsSplitQuery()
        .Include(t => t.PurchaseRequest).ThenInclude(r => r.PurchaseRequestItems)
        .Include(t => t.Invitations).ThenInclude(i => i.SupplierProfile)
        .Include(t => t.Invitations)
            .ThenInclude(i => i.Bids.Where(b => b.ComplianceStatus == "Compliant"))
            .ThenInclude(b => b.BidItems)
        .Include(t => t.CommitteeWeightProposals)
        .Include(t => t.CommitteeTieVotes)
        .Include(t => t.EvaluationCriteria)
        .Include(t => t.CommitteeAssignments).ThenInclude(a => a.User)
        .Include(t => t.TenderApprovals)
        .Include(t => t.OfferExtensions)
        .Include(t => t.EvaluationReport);

    // ───────────────────────────────────────────────────────────────────
    // LockTenderRow – Prevents two saves at the same time
    // ───────────────────────────────────────────────────────────────────
    // Uses SQL Server's UPDLOCK + HOLDLOCK hints so that other saves for this
    // tender wait until our transaction finishes.

    private async Task LockTenderRow(int tenderId, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DECLARE @locked int; SELECT @locked = TenderId FROM dbo.Tenders WITH (UPDLOCK, HOLDLOCK) WHERE TenderId = {tenderId};",
            ct);
    }

    // ───────────────────────────────────────────────────────────────────
    // Change – Shared save-and-redirect for all committee writes
    // ───────────────────────────────────────────────────────────────────
    // Every committee write (score, approve, confirm percentage, etc.)
    // goes through this method. It:
    //   1. Validates the form data
    //   2. Locks the tender row (prevents two people saving at once)
    //   3. Checks that the user is a committee member
    //   4. Runs the specific action (passed as the 'change' function)
    //   5. Saves to the database and shows a success message

    private async Task<IActionResult> Change(
        int id,
        Func<CommitteeEvaluationPage, Task<string?>> change,
        bool initialize = false)
    {
        // Clear navigation hints from any previous save.
        TempData.Remove("EvaluationOffer");
        TempData.Remove("EvaluationFocus");
        TempData.Remove("EvaluationTab");

        // Check form validation (scores must be 0–100, weights must be positive).
        if (!ModelState.IsValid)
        {
            TempData["CommitteeError"] =
                "Check the entered values. Scores must be 0–100 and weights must be positive.";
            return RedirectToAction(nameof(Evaluations), new { id });
        }

        // Lock the tender row so no one else can save at the same time.
        await using var transaction = await db.Database.BeginTransactionAsync();
        await LockTenderRow(id);

        // Load the tender with all its related data.
        var tender = await TenderQuery().SingleOrDefaultAsync(t => t.TenderId == id);
        if (tender == null)
            return NotFound();

        if (tender.Status != "UnderEvaluation")
            return BadRequest("This tender is no longer under evaluation.");

        // Build the page model with the current user's perspective.
        var page = new CommitteeEvaluationPage
        {
            Tender = tender,
            UserId = UserId,
            Evaluations = await db.Evaluations
                .Include(e => e.EvaluationScores)
                .Where(e => e.TenderId == id)
                .ToListAsync()
        };

        // Only committee members can make changes (unless initializing the evaluation).
        if (!initialize && !page.Members.Any(m => m.UserId == UserId))
            return Forbid();

        // Most actions require all four members to have confirmed their percentage first.
        // The only exception is ConfirmPercentage itself.
        if (!initialize
            && ControllerContext.ActionDescriptor.ActionName != nameof(ConfirmPercentage)
            && !page.ConsensusLocked)
        {
            TempData["CommitteeError"] =
                "Evaluation is locked until all four members confirm their percentages.";
            return RedirectToAction(nameof(Evaluations), new { id });
        }

        // Run the specific action (scoring, approving, etc.).
        var error = await change(page);
        if (error != null)
        {
            TempData["CommitteeError"] = error;
            return RedirectToAction(nameof(Evaluations), new { id });
        }

        // Save and commit.
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        // Show a success message that matches the action taken.
        TempData["CommitteeSuccess"] = ControllerContext.ActionDescriptor.ActionName switch
        {
            "Approve" =>
                "Your approval was recorded. The final report is available after all four members approve.",
            "SelectOffer" =>
                "The committee-selected offer was saved. Review it with the shared percentages before approving.",
            "ConfirmPercentage" =>
                "Your percentage is confirmed and locked. The average locks after all four members confirm.",
            "RejectOffer" =>
                "Your rejection was recorded and the selected offer was returned for review.",
            "Start" =>
                "Committee evaluation initialized. Confirm your percentage, then review every offer.",
            "ScoreOffer" => page.SelectionReady
                ? "All reviews are complete. The highest-scoring offer is ready for its report."
                : "Your marks are saved and locked for this offer.",
            "ResolveScoreTie" => page.SelectionReady
                ? "The committee resolved the tie. The winning offer is ready for its report."
                : "Tie decision recorded. All four members must agree.",
            "SaveComment" =>
                "Your comment was saved and is visible to the committee.",
            _ => "Saved successfully."
        };

        // Redirect back to the evaluations page, preserving navigation hints.
        return RedirectToAction(nameof(Evaluations), new
        {
            id,
            tab = TempData["EvaluationTab"]?.ToString(),
            focus = TempData["EvaluationFocus"]?.ToString(),
            offerId = TempData["EvaluationOffer"]
        });
    }

    // ───────────────────────────────────────────────────────────────────
    // Start – Initialize a new committee evaluation
    // ───────────────────────────────────────────────────────────────────

    // POST /EvaluationCommittee/Start: Assigns the four committee members and sets up the evaluation.
    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Start(int id) => Change(id, async page =>
    {
        if (page.Members.Count != 0)
            return "This evaluation has already been started.";

        if (page.Offers.Count == 0)
            return "At least one compliant offer is required.";

        // Find the four committee members (one per position).
        var members = new List<ApplicationUser>();
        foreach (var role in RoleNames.EvaluationPositions)
        {
            var candidates = (await users.GetUsersInRoleAsync(role))
                .Where(u => u.IsActive)
                .ToList();

            if (candidates.Count != 1
                || !await users.IsInRoleAsync(candidates[0], RoleNames.EvaluationCommittee))
                return $"Admin must provide exactly one active committee account for {role}.";

            members.Add(candidates[0]);
        }

        // All four positions must be held by different people.
        if (members.Select(m => m.Id).Distinct().Count() != 4)
            return "Four different committee accounts are required.";

        // The person starting the evaluation must be one of the four.
        if (!members.Any(m => m.Id == UserId))
            return "Your account is not one of the four committee members.";

        // Assign each member to their position (1 through 4).
        for (var i = 0; i < members.Count; i++)
        {
            page.Tender!.CommitteeAssignments.Add(new CommitteeAssignment
            {
                UserId = members[i].Id,
                CommitteeType = "Evaluation",
                Position = i + 1
            });
        }

        // Add default evaluation criteria if none exist yet.
        if (page.Criteria.Count == 0)
        {
            page.Tender!.EvaluationCriteria.Add(new EvaluationCriterion
            {
                Name = "Price", Category = "Financial", Weight = 40
            });
            page.Tender.EvaluationCriteria.Add(new EvaluationCriterion
            {
                Name = "Technical compliance", Category = "Requirements", Weight = 60
            });
        }

        // Create the evaluation report shell.
        page.Tender!.EvaluationReport ??= new EvaluationReport
        {
            Status = "Draft",
            PreparedByUserId = UserId
        };

        TempData["EvaluationTab"] = "offers";
        TempData["EvaluationFocus"] = "ai";
        return null;
    }, initialize: true);

    // ───────────────────────────────────────────────────────────────────
    // Disabled Actions (kept for route compatibility, return errors)
    // ───────────────────────────────────────────────────────────────────
    // These actions were part of an earlier workflow and now return
    // helpful error messages explaining the current process instead.

    // POST /EvaluationCommittee/SaveComment
    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult SaveComment(int id) =>
        BadRequest("Add your comment to the review for each offer. Submitted reviews are locked.");

    // POST /EvaluationCommittee/SaveWeights
    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult SaveWeights(int id) =>
        BadRequest("Each member must confirm their own percentage. The calculated average cannot be edited.");

    // POST /EvaluationCommittee/SelectOffer
    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult SelectOffer(int id) =>
        BadRequest("Every offer must be reviewed. The highest average score determines the recommended offer.");

    // POST /EvaluationCommittee/Approve
    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Approve(int id) =>
        BadRequest("Submit a financial and technical review for each offer using its scoring form.");

    // POST /EvaluationCommittee/RejectOffer
    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult RejectOffer(int id) =>
        BadRequest("Evaluate every offer using its financial and technical marks. "
            + "Manual selection and rejection are unavailable in the scoring cycle.");

    // ───────────────────────────────────────────────────────────────────
    // Helper Methods (used by other files and services)
    // ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Adds or updates a committee member's comment on the first offer.
    /// </summary>
    internal static Evaluation UpdateMemberComment(CommitteeEvaluationPage page, string comment)
    {
        var member = page.Members.Single(m => m.UserId == page.UserId);
        var bid = page.Offers.First();

        // Find the existing evaluation entry, or create a new one.
        var entry = page.Evaluation(member, bid);
        if (entry == null)
        {
            entry = new Evaluation
            {
                TenderId = page.Tender!.TenderId,
                BidId = bid.BidId,
                CommitteeAssignmentId = member.CommitteeAssignmentId,
                Status = "Draft"
            };
            page.Evaluations.Add(entry);
        }

        entry.Notes = comment;
        entry.SubmittedAt = DateTime.UtcNow;
        return entry;
    }

    /// <summary>
    /// Checks if the committee report is fully complete and, if so,
    /// marks the tender as "CommitteeApproved" and sends it to the sourcing officer.
    ///
    /// All five conditions must be true:
    ///   1. All four members confirmed their weights
    ///   2. The report text is written
    ///   3. A winning offer is selected
    ///   4. All offers have been scored by all four members
    ///   5. All four members approved
    ///
    /// Called after the final report is generated and from the chat service.
    /// </summary>
    internal static void CompleteWeightApproval(CommitteeEvaluationPage page)
    {
        bool allWeightsConfirmed = page.WeightsReady;
        bool reportWritten = page.ReportReady;
        bool winnerSelected = page.SelectionReady;
        bool allOffersScored = AllOfferScoring.Complete(page);
        bool allFourApproved = page.Approvals
            .Select(a => a.UserId)
            .Distinct()
            .Count() == 4;

        if (!allWeightsConfirmed || !reportWritten || !winnerSelected
            || !allOffersScored || !allFourApproved)
            return; // Not ready yet — at least one condition is not met.

        // Everything is complete — mark the report as approved and send it forward.
        page.Tender!.EvaluationReport ??= new EvaluationReport
        {
            PreparedByUserId = page.UserId
        };

        var report = page.Tender.EvaluationReport;
        report.Status = "CommitteeApproved";
        report.SubmittedAt = DateTime.UtcNow;
        page.Tender.Status = "AwaitingOfficerSummary";
    }
}
