using Microsoft.AspNetCore.Mvc;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Services.Reports;

namespace SourcingTenderManagement.Controllers;

// ───────────────────────────────────────────────────────────────────
// PDF Downloads – Committee report and AI recommendation exports
// ───────────────────────────────────────────────────────────────────

public partial class EvaluationCommitteeController
{
    // GET /EvaluationCommittee/ReportPdf: Downloads the committee report as PDF.
    [HttpGet]
    public async Task<IActionResult> ReportPdf(int id) => await CommitteePdf(id);

    // GET /EvaluationCommittee/EvaluationReportPdf: Downloads the evaluation report as PDF.
    [HttpGet]
    public async Task<IActionResult> EvaluationReportPdf(int id) => await CommitteePdf(id);

    /// <summary>
    /// Shared helper for both report PDF downloads.
    /// Loads the Reports page, checks that a report exists, then generates the PDF.
    /// </summary>
    private async Task<IActionResult> CommitteePdf(int id)
    {
        var result = await Reports(id);

        // If Reports() returned something other than a view (e.g. NotFound), pass it through.
        if (result is not ViewResult { Model: CommitteeEvaluationPage page })
            return result;

        // Make sure the requested tender has a written committee report.
        if (page.Tender?.TenderId != id
            || string.IsNullOrWhiteSpace(page.Tender.EvaluationReport?.CommitteeSummary))
            return NotFound();

        return ReportPdfDownload.Create(this,
            () => ReportPdfExport.CommitteeReport(page),
            $"committee-report-{id}.pdf");
    }

    // GET /EvaluationCommittee/RecommendationPdf: Downloads the saved AI recommendation as PDF.
    [HttpGet]
    public async Task<IActionResult> RecommendationPdf(int id)
    {
        var result = await Evaluations(id);

        // If Evaluations() returned something other than a view, pass it through.
        if (result is not ViewResult { Model: CommitteeEvaluationPage page })
            return result;

        // Make sure the tender has a saved AI recommendation with at least one scenario.
        if (page.Tender?.TenderId != id
            || page.AiRecommendation?.Scenarios.FirstOrDefault() == null)
            return NotFound();

        return ReportPdfDownload.Create(this,
            () => ReportPdfExport.SavedRecommendation(page),
            $"saved-recommendation-{id}.pdf");
    }
}
