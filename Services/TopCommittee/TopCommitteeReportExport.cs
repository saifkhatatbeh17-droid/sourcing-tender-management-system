using ClosedXML.Excel;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Fonts;
using SourcingTenderManagement.Models.TopCommittee;
namespace SourcingTenderManagement.Services.TopCommittee;
public sealed class TopCommitteeReportExport
{
 private static readonly object FontLock=new();
 public static string Filters(CommitteeReportFilter f)=>$"Title: {f.Search??"All"}; Status: {f.Status??"All"}; My decision: {f.Decision??"All"}; Created UTC: {f.FromDate?.ToString("yyyy-MM-dd")??"Any"} to {f.ToDate?.ToString("yyyy-MM-dd")??"Any"}";
 public static string Outcome(CommitteeReportRow r)=>r.Rejected?"Rejected":r.Status=="Awarded"?"Awarded":r.Status=="AwaitingManagerReview"?"Manager review":"Not final";
 private static readonly string[] Headers=["Tender ID","Tender title","Status","Nominated bid ID","Awarded bid ID","Approvals / 4","Final outcome","My decision","Decision date (UTC)"];
 public byte[] Excel(CommitteeReportPage p)
 {
  using var book=new XLWorkbook();var summary=book.AddWorksheet("Summary");summary.Cell(1,1).Value="Top Committee Report";summary.Cell(2,1).Value="Generated UTC";summary.Cell(2,2).Value=p.GeneratedAt;summary.Cell(2,2).Style.DateFormat.Format="yyyy-mm-dd hh:mm";summary.Cell(3,1).Value=Filters(p.Filter);summary.Range(3,1,3,6).Merge().Style.Alignment.WrapText=true;summary.Row(3).Height=48;
  var labels=new[]{"Total assigned","Matching records","Pending decisions","Awarded","Rejected","Returned to Manager","My recorded decisions"};var values=new[]{p.Summary.Assigned,p.Summary.Matching,p.Summary.Pending,p.Summary.Awarded,p.Summary.Rejected,p.Summary.Returned,p.Summary.MyDecisions};for(int i=0;i<labels.Length;i++){summary.Cell(i+5,1).Value=labels[i];summary.Cell(i+5,2).Value=values[i];}summary.Column(1).Width=28;summary.Column(2).Width=24;
  var sheet=book.AddWorksheet("Tenders");for(int c=0;c<Headers.Length;c++)sheet.Cell(1,c+1).Value=Headers[c];int row=2;foreach(var r in p.Rows){sheet.Cell(row,1).Value=r.TenderId;sheet.Cell(row,2).Value=r.Title;sheet.Cell(row,3).Value=r.Status;if(r.NominatedBidId.HasValue)sheet.Cell(row,4).Value=r.NominatedBidId.Value;if(r.AwardedBidId.HasValue)sheet.Cell(row,5).Value=r.AwardedBidId.Value;sheet.Cell(row,6).Value=r.Approvals;sheet.Cell(row,7).Value=Outcome(r);sheet.Cell(row,8).Value=r.MyDecision??"Not voted";if(r.DecisionAt.HasValue)sheet.Cell(row,9).Value=r.DecisionAt.Value;row++;}
  sheet.Row(1).Style.Font.Bold=true;sheet.SheetView.FreezeRows(1);sheet.Columns(1,9).Width=22;sheet.Column(2).Width=48;sheet.Column(3).Width=32;sheet.Column(9).Style.DateFormat.Format="yyyy-mm-dd hh:mm";sheet.Column(6).Style.NumberFormat.Format="0";sheet.Range(1,1,Math.Max(1,row-1),9).SetAutoFilter();sheet.Column(2).Style.Alignment.WrapText=true;if(p.Rows.Count==0)sheet.Cell(2,1).Value="No matching tenders.";using var stream=new MemoryStream();book.SaveAs(stream);return stream.ToArray();
 }
 public byte[] Pdf(CommitteeReportPage p)
 {
  EnsureReportFont();
  var doc=new Document();doc.Info.Title="Top Committee Report";doc.Styles["Normal"]!.Font.Name="CommitteeReport";doc.Styles["Normal"]!.Font.Size=9;var section=doc.AddSection();section.PageSetup.Orientation=Orientation.Landscape;section.PageSetup.PageFormat=PageFormat.A4;section.PageSetup.LeftMargin=Unit.FromCentimeter(1.5);section.PageSetup.RightMargin=Unit.FromCentimeter(1.5);
  var title=section.AddParagraph("Top Committee Report");title.Format.Font.Size=18;title.Format.Font.Bold=true;section.AddParagraph("Generated "+CommitteeDisplay.Utc(p.GeneratedAt));section.AddParagraph(Filters(p.Filter));section.AddParagraph($"Assigned: {p.Summary.Assigned} | Matching: {p.Summary.Matching} | Pending: {p.Summary.Pending} | Awarded: {p.Summary.Awarded} | Rejected: {p.Summary.Rejected} | Manager review: {p.Summary.Returned} | My decisions: {p.Summary.MyDecisions}").Format.SpaceAfter=12;
  var footer=section.Footers.Primary.AddParagraph();footer.AddText("Top Committee | Page ");footer.AddPageField();footer.AddText(" of ");footer.AddNumPagesField();
  if(p.Rows.Count==0)section.AddParagraph("No matching tenders.");else{var table=section.AddTable();table.Borders.Width=0.5;foreach(var w in new[]{1.3,5.1,3.4,2.0,2.0,1.6,2.3,2.3,3.0})table.AddColumn(Unit.FromCentimeter(w));var head=table.AddRow();head.HeadingFormat=true;head.Format.Font.Bold=true;head.Shading.Color=Colors.LightGray;for(int i=0;i<Headers.Length;i++)head.Cells[i].AddParagraph(Headers[i]);foreach(var r in p.Rows){var row=table.AddRow();var cells=new[]{r.TenderId.ToString(),r.Title,CommitteeDisplay.Status(r.Status),r.NominatedBidId?.ToString()??"None",r.AwardedBidId?.ToString()??"None",r.Approvals+" / 4",Outcome(r),r.MyDecision??"Not voted",CommitteeDisplay.Utc(r.DecisionAt)};for(int i=0;i<cells.Length;i++)row.Cells[i].AddParagraph(cells[i]);}}
  var renderer=new PdfDocumentRenderer{Document=doc};renderer.RenderDocument();using var stream=new MemoryStream();renderer.PdfDocument.Save(stream,false);return stream.ToArray();
 }
 internal static void EnsureReportFont() { lock(FontLock){if(GlobalFontSettings.FontResolver==null)GlobalFontSettings.FontResolver=new ReportFontResolver();} }
 private sealed class ReportFontResolver:IFontResolver
 {
  public FontResolverInfo ResolveTypeface(string familyName,bool bold,bool italic)=>new("CommitteeReport",bold,italic);
  public byte[] GetFont(string faceName)=>File.ReadAllBytes(Environment.GetEnvironmentVariable("TOP_COMMITTEE_REPORT_FONT")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts),"arial.ttf"));
 }
}
