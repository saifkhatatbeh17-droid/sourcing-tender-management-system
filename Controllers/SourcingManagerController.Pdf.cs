using Microsoft.AspNetCore.Mvc;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Services.Reports;
namespace SourcingTenderManagement.Controllers;
public partial class SourcingManagerController
{
    [HttpGet]
    public IActionResult ReportsPdf(string? search,string? decision)
    {
        var result=Reports(search,decision);
        if(result is not ViewResult {Model:ManagerReportPage page})return result;
        return ReportPdfDownload.Create(this,()=>ReportPdfExport.ManagerDecisions(page),"manager-decisions.pdf");
    }
    [HttpGet]
    public IActionResult SubmissionPdf(int id)
    {
        var result=Submissions(id);
        if(result is not ViewResult {Model:ManagerReviewPage page})return result;
        if(page.Selected?.EvaluationReportId!=id)return NotFound();
        return ReportPdfDownload.Create(this,()=>ReportPdfExport.ManagerSubmission(page),$"manager-submission-{id}.pdf");
    }
}
