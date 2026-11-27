using Microsoft.AspNetCore.Mvc;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Services.Reports;
namespace SourcingTenderManagement.Controllers;
// Evaluation Committee PDF downloads: the committee report, the evaluation report and the AI recommendation.
public partial class EvaluationCommitteeController
{
    // GET /EvaluationCommittee/ReportPdf: Opens the report pdf page.
    [HttpGet]
    public async Task<IActionResult> ReportPdf(int id)=>await CommitteePdf(id);
    // GET /EvaluationCommittee/EvaluationReportPdf: Opens the evaluation report pdf page.
    [HttpGet]
    public async Task<IActionResult> EvaluationReportPdf(int id)=>await CommitteePdf(id);
    private async Task<IActionResult> CommitteePdf(int id)
    {
        var result=await Reports(id);
        if(result is not ViewResult { Model:CommitteeEvaluationPage page })return result;
        if(page.Tender?.TenderId!=id
            ||string.IsNullOrWhiteSpace(page.Tender.EvaluationReport?.CommitteeSummary))return NotFound();
        return ReportPdfDownload.Create(this,()=>ReportPdfExport.CommitteeReport(page),$"committee-report-{id}.pdf");
    }
    // GET /EvaluationCommittee/RecommendationPdf: Opens the recommendation pdf page.
    [HttpGet]
    public async Task<IActionResult> RecommendationPdf(int id)
    {
        var result=await Evaluations(id);
        if(result is not ViewResult { Model:CommitteeEvaluationPage page })return result;
        if(page.Tender?.TenderId!=id||page.AiRecommendation?.Scenarios.FirstOrDefault()==null)return NotFound();
        return ReportPdfDownload.Create(this,()=>ReportPdfExport.SavedRecommendation(page),$"saved-recommendation-{id}.pdf");
    }
}
