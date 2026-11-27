using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
using ClosedXML.Excel;
using PdfSharp.Pdf.IO;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Contracts.TopCommittee;
using SourcingTenderManagement.Models.TopCommittee;
using SourcingTenderManagement.Services.TopCommittee;
namespace SourcingTenderManagement.Checks;
// Part of the --top-committee-foundation-check self-test (called by TopCommitteeFoundationChecks).
public static class TopCommitteePhase5Checks
{
 public static async Task RunAsync(IServiceProvider services)
 {
  int passed=0;void Check(bool ok,string message){if(!ok)throw new Exception(message);passed++;}
  static ClaimsPrincipal Actor(string id,string role=RoleNames.TopCommittee)=>new(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,
      id),new Claim(ClaimTypes.Role,
      role)},"Checks"));
  var db=services.GetRequiredService<ApplicationDbContext>();var id=await db.Users
      .AsNoTracking()
      .Where(u=>u.IsActive
          &&db.UserRoles.Any(ur=>ur.UserId==u.Id&&db.Roles.Any(r=>r.Id==ur.RoleId&&r.Name==RoleNames.TopCommittee)))
      .Select(u=>u.Id)
      .FirstAsync();var reports=services
      .GetRequiredService<TopCommitteeReports>();var live=await reports
      .GetAsync(Actor(id),new());Check(live.Summary.Assigned>=live.Summary.Matching,"Assigned summary");
  try{await reports.GetAsync(new ClaimsPrincipal(),new());throw new Exception("Anonymous accepted");}catch(CommitteeAccessDeniedException){passed++;}
  try{await reports.GetAsync(Actor(id),new(){Status="Fake"});throw new Exception("Invalid accepted");}catch(ValidationException){passed++;}
  var now=DateTime.UtcNow;var rows=Enumerable.Range(1,160)
      .Select(i=>new CommitteeReportRow(i,"Test tender "+i,"Awarded",now,i,i,4,false,"Approved",now))
      .ToList();var fixture=new CommitteeReportPage(new(){Search="Test"},new(160,160,0,160,0,0,160),rows,now);var exporter=services
      .GetRequiredService<TopCommitteeReportExport>();var pdf=exporter
      .Pdf(fixture);using(var document=PdfReader.Open(new MemoryStream(pdf),
          PdfDocumentOpenMode.Import)){Check(document.PageCount>1,
          "PDF pagination");Check(document.Info.Title=="CEO & Chiefs Report",
          "PDF title");}
  var xlsx=exporter.Excel(fixture);using(var book=new XLWorkbook(new MemoryStream(xlsx))){var sheet=book.Worksheet("Tenders");Check(sheet.LastRowUsed()!.RowNumber()==161,"XLSX rows");Check(sheet
      .Cell(2,1)
      .GetValue<int>()==1
      &&sheet
      .Cell(2,6)
      .GetValue<int>()==4,"Numeric cells");Check(sheet
      .Cell(2,9).DataType==XLDataType.DateTime,"Date type");Check(book
      .Worksheet("Summary")
      .Cell(6,2)
      .GetValue<int>()==160,"Summary matches");}
  var empty=fixture with{Rows=[],Summary=new(0,0,0,0,0,0,0)};Check(exporter.Pdf(empty).Length>100,"Empty PDF");using(var book=new XLWorkbook(new MemoryStream(exporter.Excel(empty))))Check(book
      .Worksheet("Tenders")
      .Cell(2,1)
      .GetString()=="No matching tenders.","Empty XLSX");
  var formula=fixture with{Rows=[rows[0] with{Title="=HYPERLINK(\"https://example.invalid\")"}]};using(var book=new XLWorkbook(new MemoryStream(exporter.Excel(formula))))Check(!book.Worksheet("Tenders")
      .Cell(2,2).HasFormula,"Formula injection");
  var html=await TopCommitteePhase2Checks.RenderAsync(services,Actor(id),"Reports",fixture);Check(html.Contains("Download PDF")
      &&html
      .Contains("Download Excel")
      &&html
      .Contains("Test tender 160"),"Reports Razor");Check(!html
      .Contains("Private delivery clarification"),"No private text");
  var output=Environment.GetEnvironmentVariable("TOP_COMMITTEE_PREVIEW_DIR");if(!string.IsNullOrWhiteSpace(output)){await File
      .WriteAllBytesAsync(Path.Combine(output,"report.pdf"),pdf);await File
      .WriteAllBytesAsync(Path.Combine(output,"report.xlsx"),xlsx);await File
      .WriteAllTextAsync(Path.Combine(output,"reports.html"),html);}
  var test=Environment.GetEnvironmentVariable("TOP_COMMITTEE_TEST_CONNECTION");if(!string.IsNullOrEmpty(test)){
   var options=new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(test).Options;await using var isolated=new ApplicationDbContext(options);if(!isolated.Database.GetDbConnection().Database
       .StartsWith("SourcingTender_Phase46_"))throw new Exception("Refuse non-isolated test target");var query=new TopCommitteeReports(isolated,new(isolated));var all=await query
       .GetAsync(Actor("p46-member1"),new());Check(all.Summary.Assigned==3
           &&all.Summary.Awarded==1
           &&all.Summary.Rejected==1
           &&all.Summary.Pending==1,"SQL statistics");Check((await query.GetAsync(Actor("p46-outsider"),new())).Rows.Count==0,"Unassigned scope");Check((await query
               .GetAsync(Actor("p46-member1"),new(){Search="Approval"})).Rows.Count==1,"SQL search");Check((await query
               .GetAsync(Actor("p46-member1"),new(){Status="Awarded"})).Rows.Single().Approvals==4,"SQL award progress");Check((await query
               .GetAsync(Actor("p46-member1"),new(){Decision="Rejected"})).Rows.Count==1,"SQL decision filter");Check((await query
               .GetAsync(Actor("p46-member1"),new(){FromDate=DateOnly.FromDateTime(now.AddYears(1))})).Rows.Count==0,"SQL date filter");Check((await query
               .GetAsync(Actor("p46-member1"),new(){Page=2})).Rows.Count==0,"SQL pagination");Check(!isolated.ChangeTracker
           .HasChanges(),"Read-only reports");
  }
  Console.WriteLine($"PASS: {passed} Phase 5 report/export checks. Optional SQL checks use an explicitly supplied isolated database.");
 }
}
