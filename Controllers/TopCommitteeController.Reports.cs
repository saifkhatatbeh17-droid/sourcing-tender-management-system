using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using SourcingTenderManagement.Models.TopCommittee;
using SourcingTenderManagement.Services.TopCommittee;
namespace SourcingTenderManagement.Controllers;
// CEO & Chiefs reports page and its export.
public partial class TopCommitteeController
{
 [HttpGet]
 public async Task<IActionResult> Reports([FromServices]TopCommitteeReports reports,
     [FromQuery]CommitteeReportFilter filter,
     CancellationToken ct)=>await ReadAsync(async()=>{try{if(!ModelState.IsValid)throw new ValidationException("Check the report filter values.");return View(await reports.GetAsync(User,filter,false,ct));}catch(ValidationException e){Response.StatusCode=400;ModelState.AddModelError("",e.Message);return View(new CommitteeReportPage(filter,new(0,0,0,0,0,0,0),[],DateTime.UtcNow));}});
 [HttpGet]
 public async Task<IActionResult> ReportExport([FromServices]TopCommitteeReports reports,[FromServices]TopCommitteeReportExport exporter,[FromQuery]CommitteeReportFilter filter,string format,CancellationToken ct)=>await ReadAsync(async()=>{try{if(!ModelState.IsValid
     ||format is not("pdf" or "xlsx"))throw new ValidationException("Choose PDF or Excel and valid filters.");var p=await reports.GetAsync(User,filter,true,ct);return format=="pdf"?File(exporter.Pdf(p),"application/pdf","top-committee-report.pdf"):File(exporter
     .Excel(p),"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet","top-committee-report.xlsx");}catch(ValidationException e){return BadRequest(e.Message);}});
}
