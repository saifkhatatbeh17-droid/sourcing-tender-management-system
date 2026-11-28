using Microsoft.AspNetCore.Mvc;
using SourcingTenderManagement.Services.Evaluation;

namespace SourcingTenderManagement.Controllers;

// ───────────────────────────────────────────────────────────────────
// Consensus – Confirm the agreed financial/technical percentage
// ───────────────────────────────────────────────────────────────────
// Each of the four committee members submits their proposed
// financial weight (e.g. 60%). Once all four confirm, the system
// locks the committee average and scoring can begin.

public partial class EvaluationCommitteeController
{
    // POST /EvaluationCommittee/ConfirmPercentage:
    // Locks this member's proposed financial/technical split.
    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> ConfirmPercentage(int id, decimal? financialWeight, string version)
        => Change(id, page =>
    {
        // Make sure nobody else changed the evaluation while this member was looking at it.
        if (version != page.SharedVersion)
            return Task.FromResult<string?>(
                "The committee changed. Reload before confirming your percentage.");

        // Record this member's weight and check if all four are now locked.
        var error = CommitteeConsensus.ConfirmWeight(page, financialWeight, DateTime.UtcNow);

        TempData["EvaluationTab"] = "offers";
        return Task.FromResult(error);
    });
}
