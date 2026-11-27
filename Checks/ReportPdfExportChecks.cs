using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging.Abstractions;
using PdfSharp.Pdf.IO;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.TopCommittee;
using SourcingTenderManagement.Services.PdfWatermark;
using SourcingTenderManagement.Services.Reports;
using SourcingTenderManagement.Services.TopCommittee;

namespace SourcingTenderManagement.Checks;
// Self-test, not part of the website.

public static class ReportPdfExportChecks
{
    // No database, application startup, HTTP server, or browser PDF generation.
    public static async Task RunAsync(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var doc=TopCommitteeReportExport.CreatePdfDocument("Pagination stress");
        var section=doc.LastSection;section.AddParagraph("Pagination stress: complete table and long token");
        TopCommitteeReportExport.Heading(section,new string('H',200));
        var table=TopCommitteeReportExport.Table(section,["Record","Full report content","Notes"],[3,16,7.7]);
        var full=string.Join("\n",Enumerable.Range(1,160).Select(i=>$"FULL-LINE-{i:D3}: Complete persisted report observations and recommendations."));
        var token="LONGTOKENSTART"+new string('X',1800)+"LONGTOKENEND";
        TopCommitteeReportExport.AddRow(table,["RECORD-ONE",full,token]);
        for(var i=1;i<=180;i++)TopCommitteeReportExport.AddRow(table,[$"ROW-{i:D3}","All recorded values retained",$"NOTES-{i:D3}"]);
        await Save("pagination-stress.pdf",TopCommitteeReportExport.RenderPdf(doc));
        await Save("empty-officer.pdf",ReportPdfExport.OfficerReports([]));
        await Save("empty-manager.pdf",ReportPdfExport.ManagerDecisions(new ManagerReportPage([],"No results","Rejected")));
        var longTokens=new CommitteeReportPage(new(){Search=new string('W',200)},new(1,1,1,0,0,0,0),
            [new(7,new string('T',200),"UnderEvaluation",DateTime.UtcNow,null,null,0,false,null,null)],DateTime.UtcNow)
            {SystemStatistics=new(1,1,0),Suppliers=[new(1,new string('S',200),0)]};
        await Save("top-long-tokens.pdf",new TopCommitteeReportExport().Pdf(longTokens));
        var controller=new TestController {ControllerContext=new ControllerContext{HttpContext=new DefaultHttpContext()}};
        var services=new ServiceCollection().AddLogging().BuildServiceProvider();controller.HttpContext.RequestServices=services;
        var failure=ReportPdfDownload.Create(controller,()=>throw new InvalidOperationException("Synthetic generation failure"),"never.pdf");
        if(failure is not ObjectResult {StatusCode:503})throw new InvalidOperationException("Partial PDF returned on generation failure.");
        Console.WriteLine("PASS PDF EXPORT: continuation rows, full Unicode text path, empty report documents, fail-closed rendering fixture generated.");

        async Task Save(string name,byte[] source)
        {
            var actor=new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,"export-qa"),new Claim(ClaimTypes.Name,"pdf-qa-core@example.invalid")],"Synthetic"));
            var http=new DefaultHttpContext{User=actor};var action=new ActionContext(http,new RouteData(),new ActionDescriptor());
            var context=new ResultExecutingContext(action,[],new FileContentResult(source,"application/pdf"),new object());
            var filter=new PdfWatermarkResultFilter(new PdfWatermarkService(),new FixedClock(),NullLogger<PdfWatermarkResultFilter>.Instance);
            await filter.OnResultExecutionAsync(context,()=>Task.FromResult(new ResultExecutedContext(action,[],context.Result,context.Controller)));
            if(context.Result is not FileContentResult file)throw new InvalidOperationException("Watermark failed; no fixture PDF saved.");
            using var pdf=PdfReader.Open(new MemoryStream(file.FileContents),PdfDocumentOpenMode.Import);
            if(pdf.PageCount<1)throw new InvalidOperationException("Empty PDF.");
            await File.WriteAllBytesAsync(Path.Combine(outputDirectory,name),file.FileContents);
        }
    }
    private sealed class TestController:Controller { }
    private sealed class FixedClock:TimeProvider { public override DateTimeOffset GetUtcNow()=>new(2026,11,14,12,34,56,TimeSpan.Zero); }
}
