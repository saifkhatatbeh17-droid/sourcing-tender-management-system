using Microsoft.AspNetCore.Mvc;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Services.Reports;
namespace SourcingTenderManagement.Controllers;
public partial class SourcingOfficerController
{
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
    [HttpGet]
    public async Task<IActionResult> EvaluationReportPdf(int tenderId)
    {
        var result=await Evaluations(tenderId);
        if(result is not ViewResult view)return result;
        if(view.ViewData["EvaluationReport"] is not EvaluationReport report||report.TenderId!=tenderId)return NotFound();
        return ReportPdfDownload.Create(this,()=>ReportPdfExport.OfficerEvaluation(view.ViewData),$"officer-evaluation-{tenderId}.pdf");
    }
}
