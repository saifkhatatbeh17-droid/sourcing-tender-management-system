using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Services.Evaluation;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Controllers;

// ───────────────────────────────────────────────────────────────────
// Offer Scoring – Score each offer and settle ties
// ───────────────────────────────────────────────────────────────────
// Each committee member reviews every compliant offer by giving it
// a financial mark and a technical mark (both 0–100).
// If two or more offers tie for the highest score, each member
// votes on which tied offer should win.

public partial class EvaluationCommitteeController
{
    // POST /EvaluationCommittee/ScoreOffer:
    // Saves this member's financial and technical marks for one offer.
    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> ScoreOffer(
        int id, int bidId,
        decimal? financialMark, decimal? technicalMark,
        string? comment, string version)
        => Change(id, async page =>
    {
        // Make sure nobody else changed the reviews while this member was scoring.
        if (page.SharedVersion != version)
            return "The reviews changed. Reload before submitting your marks.";

        // All clarifications must be resolved before scoring.
        if (await db.Clarifications.AnyAsync(c =>
            c.Status != "Resolved"
            && (c.TenderId == id || c.Bid != null && c.Bid.TenderId == id)))
            return "Resolve open clarifications before submitting marks.";

        // Save the marks (returns an error message if validation fails, null if OK).
        var error = AllOfferScoring.Save(
            page, bidId, financialMark, technicalMark, comment, DateTime.UtcNow);
        if (error != null)
            return error;

        // Add any new evaluation entries to the database.
        foreach (var entry in page.Evaluations.Where(e => e.EvaluationId == 0))
            db.Evaluations.Add(entry);

        // Check if all offers are now fully scored and pick the winner.
        AllOfferScoring.SelectWinner(page, DateTime.UtcNow);

        TempData["EvaluationTab"] = page.SelectionReady ? "report" : "forms";
        TempData["EvaluationOffer"] = bidId;
        return null;
    });

    // POST /EvaluationCommittee/ResolveScoreTie:
    // This member votes for one of the tied highest-scoring offers.
    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> ResolveScoreTie(int id, int bidId, string version)
        => Change(id, page =>
    {
        // Make sure nobody else changed the tie decisions while this member was voting.
        if (version != page.SharedVersion)
            return Task.FromResult<string?>(
                "Tie decisions changed. Reload before confirming.");

        // Verify this is a real tie and the chosen bid is one of the leaders.
        var leaders = AllOfferScoring.Leaders(page);
        if (leaders.Count < 2 || !leaders.Any(r => r.BidId == bidId))
            return Task.FromResult<string?>(
                "Choose one of the tied highest-scoring offers after every review is complete.");

        // Record (or update) this member's tie-break vote.
        var member = page.Members.Single(m => m.UserId == UserId);
        var vote = page.Tender!.CommitteeTieVotes
            .SingleOrDefault(v => v.CommitteeAssignmentId == member.CommitteeAssignmentId);

        if (vote == null)
        {
            vote = new CommitteeTieVote
            {
                CommitteeAssignmentId = member.CommitteeAssignmentId
            };
            page.Tender.CommitteeTieVotes.Add(vote);
        }

        vote.BidId = bidId;
        vote.ConfirmedAt = DateTime.UtcNow;

        // Check if all members have voted and a winner can be determined.
        AllOfferScoring.SelectWinner(page, DateTime.UtcNow);

        TempData["EvaluationTab"] = "report";
        return Task.FromResult<string?>(null);
    });
}
