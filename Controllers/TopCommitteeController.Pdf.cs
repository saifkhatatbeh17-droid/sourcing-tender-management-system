using Microsoft.AspNetCore.Mvc;
using SourcingTenderManagement.Models.TopCommittee;
using SourcingTenderManagement.Services.Reports;
namespace SourcingTenderManagement.Controllers;
// CEO & Chiefs PDF download of a tender's unified report.
public partial class TopCommitteeController
{
    [HttpGet]
    public async Task<IActionResult> UnifiedReportPdf(int id,int? bidId,CancellationToken cancellationToken)
    {
        var result=await TenderDetails(id,bidId,cancellationToken);
        if(result is not ViewResult {Model:CommitteeTenderPage page})return result;
        if(page.Tender.Report==null)return NotFound();
        return ReportPdfDownload.Create(this,()=>ReportPdfExport.UnifiedReport(page),$"unified-report-{id}.pdf");
    }
}
