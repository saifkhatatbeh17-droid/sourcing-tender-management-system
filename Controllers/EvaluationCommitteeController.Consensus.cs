using Microsoft.AspNetCore.Mvc;
using SourcingTenderManagement.Services.Evaluation;

namespace SourcingTenderManagement.Controllers;

// Evaluation Committee action to confirm the agreed financial/technical percentage for a tender.
public partial class EvaluationCommitteeController
{
    // POST /EvaluationCommittee/ConfirmPercentage: Confirm percentage. Runs when its form or button is submitted.
    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> ConfirmPercentage(int id, decimal? financialWeight, string version) => Change(id, page =>
    {
        if (version != page.SharedVersion) return Task.FromResult<string?>("The committee changed. Reload before confirming your percentage.");
        var error = CommitteeConsensus.ConfirmWeight(page, financialWeight, DateTime.UtcNow);
        TempData["EvaluationTab"] = "offers";
        return Task.FromResult(error);
    });

}
