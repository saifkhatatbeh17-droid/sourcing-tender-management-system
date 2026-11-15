using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Services.Evaluation;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Controllers;

public partial class EvaluationCommitteeController
{
    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> ScoreOffer(int id, int bidId, decimal? financialMark, decimal? technicalMark, string? comment, string version) => Change(id, async page =>
    {
        if (page.SharedVersion != version) return "The reviews changed. Reload before submitting your marks.";
        if (await db.Clarifications.AnyAsync(c => c.Status != "Resolved" && (c.TenderId == id || c.Bid != null && c.Bid.TenderId == id))) return "Resolve open clarifications before submitting marks.";
        var error = AllOfferScoring.Save(page, bidId, financialMark, technicalMark, comment, DateTime.UtcNow);
        if (error != null) return error;
        foreach (var entry in page.Evaluations.Where(e => e.EvaluationId == 0)) db.Evaluations.Add(entry);
        AllOfferScoring.SelectWinner(page, DateTime.UtcNow);
        TempData["EvaluationTab"] = page.SelectionReady ? "report" : "forms";
        TempData["EvaluationOffer"] = bidId;
        return null;
    });

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> ResolveScoreTie(int id, int bidId, string version) => Change(id, page =>
    {
        if (version != page.SharedVersion) return Task.FromResult<string?>("Tie decisions changed. Reload before confirming.");
        var leaders = AllOfferScoring.Leaders(page);
        if (leaders.Count < 2 || !leaders.Any(r => r.BidId == bidId)) return Task.FromResult<string?>("Choose one of the tied highest-scoring offers after every review is complete.");
        var member = page.Members.Single(m => m.UserId == UserId);
        var vote = page.Tender!.CommitteeTieVotes.SingleOrDefault(v => v.CommitteeAssignmentId == member.CommitteeAssignmentId);
        if (vote == null) { vote = new CommitteeTieVote { CommitteeAssignmentId = member.CommitteeAssignmentId }; page.Tender.CommitteeTieVotes.Add(vote); }
        vote.BidId = bidId; vote.ConfirmedAt = DateTime.UtcNow;
        AllOfferScoring.SelectWinner(page, DateTime.UtcNow); TempData["EvaluationTab"] = "report";
        return Task.FromResult<string?>(null);
    });
}
