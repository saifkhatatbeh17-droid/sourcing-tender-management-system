using Microsoft.AspNetCore.Mvc;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Services.Reports;
namespace SourcingTenderManagement.Controllers;
// Sourcing Officer PDF downloads: the tender report and the evaluation report.
public partial class SourcingOfficerController
{
    // GET /SourcingOfficer/ReportPdf: Opens the report pdf page.
    [HttpGet]
    public async Task<IActionResult> ReportPdf(int? id)
    {
        var result=await Reports(id);
        if(result is not ViewResult {Model:List<EvaluationReport> reports} view)return result;
        if(id.HasValue)
        {
            var report=(EvaluationReport)view.ViewData["SelectedReport"]!;
            return ReportPdfDownload.Create(this,()=>ReportPdfExport.OfficerReport(report),$"officer-report-{id}.pdf");
        }
        return ReportPdfDownload.Create(this,()=>ReportPdfExport.OfficerReports(reports),"officer-reports.pdf");
    }
    // GET /SourcingOfficer/EvaluationReportPdf: Opens the evaluation report pdf page.
    [HttpGet]
    public async Task<IActionResult> EvaluationReportPdf(int tenderId)
    {
        var result=await Evaluations(tenderId);
        if(result is not ViewResult view)return result;
        if(view.ViewData["EvaluationReport"] is not EvaluationReport report
            ||report.TenderId!=tenderId)return NotFound();
        return ReportPdfDownload.Create(this,()=>ReportPdfExport.OfficerEvaluation(view.ViewData),$"officer-evaluation-{tenderId}.pdf");
    }
}
