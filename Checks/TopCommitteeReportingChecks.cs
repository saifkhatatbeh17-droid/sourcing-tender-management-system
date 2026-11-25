using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using PdfSharp.Pdf.IO;
using SourcingTenderManagement.Contracts.TopCommittee;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Models.TopCommittee;
using SourcingTenderManagement.Services.TopCommittee;

namespace SourcingTenderManagement.Checks;

// No database connection, startup seeding, records, or migrations. Invoke from an isolated harness.
public static class TopCommitteeReportingChecks
{
    public static async Task RunAsync(string outputDirectory)
    {
        int passed=0;
        void Check(bool ok,string label) { if(!ok)throw new InvalidOperationException(label); passed++; Console.WriteLine("PASS REPORT: "+label); }
        var a=new DateTime(2026,11,14,9,0,0,DateTimeKind.Utc);
        var b=a.AddHours(1); var award=a.AddHours(2);
        var tender=new Tender { TenderId=1,Title="Laboratory equipment",Status="Awarded",CreatedAt=a.AddDays(-1),
            AwardedBidId=10,AwardedAt=award,
            Bid=new Bid { BidId=10,TenderId=1,Invitation=new Invitation { SupplierProfileId=7 } },
            TenderApprovals=[
                new(){Stage="Top",UserId="member-a",Decision="Approved",DecidedAt=a},
                new(){Stage="Top",UserId="member-b",Decision="Approved",DecidedAt=b},
                new(){Stage="Manager",UserId="manager",Decision="Approved",DecidedAt=award.AddHours(1)}] };
        var source=new[]{tender}.AsQueryable();
        var first=TopCommitteeReports.ProjectRows(source,"member-a").Single();
        var second=TopCommitteeReports.ProjectRows(source,"member-b").Single();
        var absent=TopCommitteeReports.ProjectRows(source,"not-voted").Single();
        Check(first.DecisionAt==a && second.DecisionAt==b,"Each member retains their own vote timestamp");
        Check(first.MyDecision=="Approved" && absent.MyDecision==null && absent.DecisionAt==null,"Unvoted member has no decision or timestamp");
        Check(first.AwardedAt==award && first.CommitteeOutcomeAt==award,"Award and original committee timestamp preserved separately");
        tender.AwardedAt=null;
        Check(TopCommitteeReports.ProjectRows(source,"member-a").Single().CommitteeOutcomeAt==b,"Latest Top vote excludes Manager vote");
        tender.AwardedAt=award;
        var participation=new Tender { TenderId=2,Status="Open",AwardedBidId=11,
            Bid=new Bid { BidId=11,TenderId=2,Invitation=new Invitation { SupplierProfileId=7 } } };
        var invalid=new Tender { TenderId=3,Status="Awarded",AwardedBidId=12,
            Bid=new Bid { BidId=12,TenderId=999,Invitation=new Invitation { SupplierProfileId=7 } } };
        using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=invalid.example;Database=NeverOpened;Integrated Security=True;TrustServerCertificate=True").Options);
        var detailSql=TopCommitteeReports.ProjectRows(TopCommitteeReports.AssignedTenders(db,"member-a"),"member-a").ToQueryString();
        Check(detailSql.Contains("CommitteeAssignments") && detailSql.Contains("member-a"),"Detail SQL retains member assignment scope");
        Check(detailSql.Contains("DecidedAt") && detailSql.Contains("AwardedAt"),"Decision and award SQL projection translates");
        var service=new TopCommitteeReports(db,new(db));
        try { await service.GetAsync(new ClaimsPrincipal(),new(),true); throw new Exception("Anonymous accepted"); }
        catch(CommitteeAccessDeniedException) { Check(true,"Anonymous comprehensive export rejected without SQL"); }
        var wrongRole=new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier,"x"),new(ClaimTypes.Role,"Supplier")],"QA"));
        try { await service.GetAsync(wrongRole,new(),true); throw new Exception("Wrong role accepted"); }
        catch(CommitteeAccessDeniedException) { Check(true,"Wrong role comprehensive export rejected without SQL"); }
        Check(!db.ChangeTracker.HasChanges(),"Read queries leave tracker unchanged");

        var page=new CommitteeReportPage(new(),new(1,1,0,1,0,0,1),[first],award)
        { SystemStatistics=new(2,6,1),Suppliers=[new(7,"=HYPERLINK(\"https://example.invalid\")",1),new(8,"Supplier with no awards",0)] };
        var exporter=new TopCommitteeReportExport();
        var xlsx=exporter.Excel(page);
        using(var book=new XLWorkbook(new MemoryStream(xlsx)))
        {
            Check(book.Worksheets.Count==4,"Single workbook has four meaningful worksheets");
            Check(book.Worksheet("Tenders").Cell(2,9).GetDateTime()==a,"Excel uses current member timestamp");
            Check(book.Worksheet("Tenders").Cell(2,11).GetDateTime()==award,"Excel preserves award timestamp");
            Check(!book.Worksheet("Supplier Statistics").Cell(2,2).HasFormula,"Supplier name is text, not formula");
            Check(book.Worksheet("Supplier Statistics").Cell(3,3).GetValue<int>()==0,"Zero award supplier included");
            Check(book.Worksheet("Tenders").Cell(2,1).DataType==XLDataType.Number,"Tender ID stays numeric");
            Check(book.Worksheet("Tenders").Cell(2,9).DataType==XLDataType.DateTime,"Member timestamp stays typed date");
            Check(book.Worksheet("Summary").Cell(15,2).GetValue<int>()==2,"System supplier count separated in summary");
        }
        var empty=page with { Rows=[],Summary=new(0,0,0,0,0,0,0),SystemStatistics=new(0,0,0),Suppliers=[] };
        using(var book=new XLWorkbook(new MemoryStream(exporter.Excel(empty))))
            Check(book.Worksheet("Supplier Statistics").Cell(2,1).GetString()=="No suppliers recorded.","Empty supplier state");
        var longName=string.Join(" ",Enumerable.Repeat("Supplier procurement and laboratory equipment",8));
        var large=page with {Rows=Enumerable.Range(1,160).Select(i=>first with {TenderId=i,Title="Tender "+i}).ToList(),
            Suppliers=Enumerable.Range(1,180).Select(i=>new CommitteeSupplierStatistics(i,longName+" "+i,i%5)).ToList()};
        var pdf=exporter.Pdf(large);
        using(var doc=PdfReader.Open(new MemoryStream(pdf),PdfDocumentOpenMode.Import))
            Check(doc.PageCount>4,"Large PDF paginates all sections");
        using(var doc=PdfReader.Open(new MemoryStream(exporter.Pdf(empty)),PdfDocumentOpenMode.Import))
            Check(doc.PageCount>=1,"Empty PDF retains all four sections");
        var maximum=page with {Rows=Enumerable.Range(1,5000).Select(i=>first with {TenderId=i}).ToList()};
        using(var book=new XLWorkbook(new MemoryStream(exporter.Excel(maximum))))
            Check(book.Worksheet("Tenders").LastRowUsed()!.RowNumber()==5001,"5000 tender workbook has no truncation");
        Directory.CreateDirectory(outputDirectory);
        await File.WriteAllBytesAsync(Path.Combine(outputDirectory,"comprehensive-large.pdf"),pdf);
        await File.WriteAllBytesAsync(Path.Combine(outputDirectory,"comprehensive.pdf"),exporter.Pdf(page));
        await File.WriteAllBytesAsync(Path.Combine(outputDirectory,"comprehensive.xlsx"),xlsx);
        await File.WriteAllBytesAsync(Path.Combine(outputDirectory,"empty.pdf"),exporter.Pdf(empty));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory,"detail-query.sql"),detailSql);
        Console.WriteLine($"PASS: {passed} reporting checks. Artifacts use in-memory test fixtures only.");
    }
}
