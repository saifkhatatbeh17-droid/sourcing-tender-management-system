using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Services.Evaluation;

namespace SourcingTenderManagement.Controllers;

public partial class EvaluationCommitteeController
{
    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> ConfirmPercentage(int id, decimal? financialWeight, string version) => Change(id, page =>
    {
        if (version != page.SharedVersion) return Task.FromResult<string?>("The committee changed. Reload before confirming your percentage.");
        var error = CommitteeScoring.ConfirmWeight(page, financialWeight, DateTime.UtcNow);
        TempData["EvaluationTab"] = "forms";
        return Task.FromResult(error);
    });

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> AcceptScoredOffer(int id, int bidId, decimal? financialMark, decimal? technicalMark, string? notes, string version) => Change(id, async page =>
    {
        if (version != page.SharedVersion) return "The evaluation changed. Reload before accepting your marks.";
        if (await HasOpenQuestions(id)) return "Resolve open tender and offer clarifications before accepting marks.";
        var error = CommitteeScoring.AcceptOffer(page, bidId, financialMark, technicalMark, notes, DateTime.UtcNow);
        if (error != null) return error;
        foreach (var entry in page.Evaluations.Where(e => e.EvaluationId == 0)) db.Evaluations.Add(entry);
        if (CommitteeScoring.FinalizeIfReady(page, DateTime.UtcNow)) NotifyScoredResult(page);
        TempData["EvaluationTab"] = page.Locked ? "report" : "forms";
        return null;
    });

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> DecideTie(int id, int bidId, string version) => Change(id, async page =>
    {
        if (version != page.SharedVersion) return "The tie decisions changed. Reload before confirming your choice.";
        if (await HasOpenQuestions(id)) return "Resolve open clarifications before completing the tie decision.";
        var error = CommitteeScoring.ResolveTie(page, bidId, DateTime.UtcNow);
        if (error != null) return error;
        if (CommitteeScoring.FinalizeIfReady(page, DateTime.UtcNow)) NotifyScoredResult(page);
        TempData["EvaluationTab"] = "report";
        return null;
    });

    private Task<bool> HasOpenQuestions(int id) => db.Clarifications.AnyAsync(c => c.Status != "Resolved" &&
        (c.TenderId == id || (c.Bid != null && c.Bid.TenderId == id)));

    private void NotifyScoredResult(CommitteeEvaluationPage page)
    {
        var tender = page.Tender!;
        db.Notifications.Add(new Notification { UserId = tender.CreatedByUserId, Title = "Committee scoring complete",
            Message = $"{tender.Title}: every offer has four accepted reviews. The selected offer and score report are ready for your review.",
            RelativeUrl = $"/SourcingOfficer/Offers?tenderId={tender.TenderId}", CreatedAt = DateTime.UtcNow });
    }
}
