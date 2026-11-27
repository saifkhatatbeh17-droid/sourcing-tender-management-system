using Microsoft.AspNetCore.Mvc;
using SourcingTenderManagement.Services.Evaluation;

namespace SourcingTenderManagement.Controllers;

public partial class EvaluationCommitteeController
{
    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> ConfirmPercentage(int id, decimal? financialWeight, string version) => Change(id, page =>
    {
        if (version != page.SharedVersion) return Task.FromResult<string?>("The committee changed. Reload before confirming your percentage.");
        var error = CommitteeConsensus.ConfirmWeight(page, financialWeight, DateTime.UtcNow);
        TempData["EvaluationTab"] = "offers";
        return Task.FromResult(error);
    });

}
