using ClosedXML.Excel;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;
using PdfSharp.Drawing;
using System.Globalization;
using SourcingTenderManagement.Models.TopCommittee;

namespace SourcingTenderManagement.Services.TopCommittee;

public sealed class TopCommitteeReportExport
{
    private static readonly object FontLock = new();
    public const string AwardDefinition = "Awarded Tenders counts distinct tenders with Awarded status and a valid winning bid linked to the supplier. These are procurement awards, not confirmed completed purchases; delivery and payment are not tracked.";
    public const string Scope = "System-wide totals cover all records. Tender details, supplier activity and decision summaries cover assigned tenders matching the active filters. Dates are UTC.";
    public static string Filters(CommitteeReportFilter f) => $"Title: {f.Search ?? "All"}; Status: {f.Status ?? "All"}; My decision: {f.Decision ?? "All"}; Created UTC: {f.FromDate?.ToString("yyyy-MM-dd") ?? "Any"} to {f.ToDate?.ToString("yyyy-MM-dd") ?? "Any"}";
    public static string Outcome(CommitteeReportRow r) => r.Rejected ? "Rejected" : r.Status == "Awarded" ? "Awarded" : r.Status == "AwaitingManagerReview" ? "Manager review" : "Not final";
    private static readonly string[] Headers = ["Tender ID","Tender title","Status","Nominated bid ID","Awarded bid ID","Approvals / 4","Final outcome","My decision","My decision date (UTC)"];

    private static IEnumerable<(string Label, int Count)> MemberMetrics(CommitteeReportPage p) =>
        new (string,int)[] { ("Total assigned (unfiltered)",p.Summary.Assigned),("Matching records",p.Summary.Matching),
        ("Pending decisions",p.Summary.Pending),("Awarded",p.Summary.Awarded),("Rejected",p.Summary.Rejected),
        ("Returned to Manager",p.Summary.Returned),("My recorded decisions",p.Summary.MyDecisions) };
    private static IEnumerable<(string Label, int Count)> DecisionMetrics(CommitteeReportPage p) =>
        new (string,int)[] { ("My approvals",p.Rows.Count(r=>r.MyDecision=="Approved")),
        ("My historical rejections",p.Rows.Count(r=>r.MyDecision=="Rejected")),
        ("Not voted",p.Rows.Count(r=>r.MyDecision==null)),
        ("Recorded approvals across matching tenders",p.Rows.Sum(r=>r.Approvals)) };

    public byte[] Excel(CommitteeReportPage p)
    {
        using var book = new XLWorkbook();
        var summary = book.AddWorksheet("Summary");
        summary.Cell(1,1).Value = "CEO & Chiefs Report";
        summary.Cell(2,1).Value = "Generated UTC"; summary.Cell(2,2).Value = p.GeneratedAt;
        summary.Cell(2,2).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
        summary.Cell(3,1).Value = Filters(p.Filter);
        summary.Range(3,1,3,6).Merge().Style.Alignment.WrapText = true; summary.Row(3).Height = 48;
        // Preserve the original member-summary cell positions for existing consumers.
        int row = 5;
        foreach (var m in MemberMetrics(p)) { summary.Cell(row,1).Value=m.Label; summary.Cell(row++,2).Value=m.Count; }
        summary.Cell(12,1).Value = "Member metrics above follow filters; total assigned is unfiltered. Rejected and Manager-review counts can overlap.";
        summary.Range(12,1,12,6).Merge().Style.Alignment.WrapText=true; summary.Row(12).Height=32;
        summary.Cell(14,1).Value = "System-wide statistics (all records, unfiltered)";
        if (p.SystemStatistics is {} system)
        {
            summary.Cell(15,1).Value="Total suppliers"; summary.Cell(15,2).Value=system.Suppliers;
            summary.Cell(16,1).Value="Total tenders"; summary.Cell(16,2).Value=system.Tenders;
            summary.Cell(17,1).Value="Awarded tenders (status)"; summary.Cell(17,2).Value=system.AwardedTenders;
        }
        else summary.Cell(15,1).Value="System-wide statistics unavailable.";
        summary.Cell(19,1).Value=Scope; summary.Range(19,1,19,6).Merge().Style.Alignment.WrapText=true; summary.Row(19).Height=48;
        if (p.SystemStatistics is { } registered)
        {
            var statusRow = 21;
            foreach (var pair in registered.SuppliersByStatus.OrderBy(x => x.Key))
            {
                summary.Cell(statusRow, 1).Value = "Suppliers · " + pair.Key;
                summary.Cell(statusRow++, 2).Value = pair.Value;
            }
        }
        summary.Columns(1,6).Width=22; summary.Column(1).Width=44; summary.Column(2).Width=24;
        foreach(var n in new[]{1,14}) summary.Row(n).Style.Font.Bold=true;

        // Keep the existing Tenders worksheet name and its first nine columns.
        var tenders=book.AddWorksheet("Tenders");
        WriteHeader(tenders,Headers.Concat(["Latest committee activity (UTC)","Awarded at (UTC)"]).ToArray());
        row=2;
        foreach(var r in p.Rows)
        {
            tenders.Cell(row,1).Value=r.TenderId; tenders.Cell(row,2).Value=r.Title;
            tenders.Cell(row,3).Value=CommitteeDisplay.Status(r.Status);
            if(r.NominatedBidId.HasValue)tenders.Cell(row,4).Value=r.NominatedBidId.Value;
            if(r.AwardedBidId.HasValue)tenders.Cell(row,5).Value=r.AwardedBidId.Value;
            tenders.Cell(row,6).Value=r.Approvals; tenders.Cell(row,7).Value=Outcome(r);
            tenders.Cell(row,8).Value=r.MyDecision??"Not voted";
            if(r.DecisionAt.HasValue)tenders.Cell(row,9).Value=r.DecisionAt.Value;
            if(r.CommitteeOutcomeAt.HasValue)tenders.Cell(row,10).Value=r.CommitteeOutcomeAt.Value;
            if(r.AwardedAt.HasValue)tenders.Cell(row,11).Value=r.AwardedAt.Value;
            row++;
        }
        if(p.Rows.Count==0)tenders.Cell(2,1).Value="No matching tenders.";
        FormatTable(tenders,Math.Max(row-1,2),11);
        tenders.Column(2).Width=48; tenders.Column(3).Width=32;
        tenders.Column(2).Style.Alignment.WrapText=true;
        tenders.Columns(9,11).Style.DateFormat.Format="yyyy-mm-dd hh:mm";

        var suppliers=book.AddWorksheet("Supplier Statistics");
        WriteHeader(suppliers,["Supplier ID","Supplier name","Awarded Tenders","Status","Invited","Dealings","Win rate","Dealings per month"]);
        row=2;
        foreach(var s in p.Suppliers)
        {
            suppliers.Cell(row,1).Value=s.SupplierId; suppliers.Cell(row,2).Value=s.SupplierName;
            suppliers.Cell(row,3).Value=s.AwardedTenders;
            suppliers.Cell(row,4).Value=s.Status;
            suppliers.Cell(row,5).Value=s.Invited;
            suppliers.Cell(row,6).Value=s.Dealings;
            suppliers.Cell(row,7).Value=s.WinRate;
            suppliers.Cell(row,7).Style.NumberFormat.Format="0.0%";
            suppliers.Cell(row,8).Value=s.Frequency;
            suppliers.Cell(row++,8).Style.NumberFormat.Format="0.0";
        }
        if(p.Suppliers.Count==0)suppliers.Cell(2,1).Value="No suppliers recorded.";
        FormatTable(suppliers,Math.Max(row-1,2),8); suppliers.Column(2).Width=60;
        suppliers.Column(2).Style.Alignment.WrapText=true;
        var noteRow=Math.Max(row,3)+1;
        suppliers.Cell(noteRow,1).Value=AwardDefinition;
        suppliers.Range(noteRow,1,noteRow,3).Merge().Style.Alignment.WrapText=true;
        suppliers.Row(noteRow).Height=64;

        var decisions=book.AddWorksheet("Decision Summary");
        WriteHeader(decisions,["Metric — matching assigned tenders","Count"]);
        row=2;
        foreach(var m in DecisionMetrics(p)) { decisions.Cell(row,1).Value=m.Label; decisions.Cell(row++,2).Value=m.Count; }
        FormatTable(decisions,row-1,2); decisions.Column(1).Width=60;
        decisions.Cell(row+1,1).Value="This is a summary of persisted records, not a complete audit trail. Latest committee activity is the award time, or latest recorded Top-stage vote if not awarded.";
        decisions.Range(row+1,1,row+1,3).Merge().Style.Alignment.WrapText=true; decisions.Column(3).Width=24; decisions.Row(row+1).Height=64;
        using var stream=new MemoryStream(); book.SaveAs(stream); return stream.ToArray();
    }

    private static void WriteHeader(IXLWorksheet sheet,string[] headers)
    {
        for(int i=0;i<headers.Length;i++)sheet.Cell(1,i+1).Value=headers[i];
        var heading=sheet.Range(1,1,1,headers.Length);
        heading.Style.Font.Bold=true; heading.Style.Fill.BackgroundColor=XLColor.FromHtml("#00a3a6");
        heading.Style.Font.FontColor=XLColor.FromHtml("#002e30"); heading.Style.Alignment.WrapText=true;
        sheet.Row(1).Height=34;
    }
    private static void FormatTable(IXLWorksheet sheet,int lastRow,int columns)
    {
        sheet.Columns(1,columns).Width=24;
        sheet.SheetView.FreezeRows(1); sheet.Range(1,1,lastRow,columns).SetAutoFilter();
        sheet.Range(1,1,lastRow,columns).Style.Alignment.Vertical=XLAlignmentVerticalValues.Top;
    }

    public byte[] Pdf(CommitteeReportPage p)
    {
        var doc=CreatePdfDocument("CEO & Chiefs Report");
        var section=doc.LastSection;
        var title=section.AddParagraph("CEO & Chiefs Report"); title.Format.Font.Size=20; title.Format.Font.Bold=true;
        section.AddParagraph("Generated "+CommitteeDisplay.Utc(p.GeneratedAt));
        section.AddParagraph(Scope).Format.SpaceAfter=8; AddPdfText(section,Filters(p.Filter)).Format.SpaceAfter=12;
        var footer=section.Footers.Primary.AddParagraph(); footer.AddText("CEO & Chiefs | Page "); footer.AddPageField(); footer.AddText(" of "); footer.AddNumPagesField();

        Heading(section,"A · General statistics");
        if(p.SystemStatistics is {} system)
            section.AddParagraph($"System-wide (unfiltered): {system.Suppliers:N0} suppliers | {system.Tenders:N0} tenders | {system.AwardedTenders:N0} awarded tenders (status).");
        else section.AddParagraph("System-wide statistics unavailable.");
        section.AddParagraph("Your assigned tenders: "+string.Join(" | ",MemberMetrics(p).Select(m=>$"{m.Label}: {m.Count:N0}")));
        section.AddParagraph("Member counts follow filters except total assigned. Rejected and Manager-review counts may overlap.").Format.SpaceAfter=12;

        Heading(section,"B · Tender and decision details");
        if(p.Rows.Count==0)section.AddParagraph("No matching tenders.");
        else
        {
            var table=Table(section,Headers,[1.2,6.5,3.8,1.9,1.9,1.6,2.8,2.7,4.3]);
            foreach(var r in p.Rows) AddRow(table,[r.TenderId.ToString(),r.Title,CommitteeDisplay.Status(r.Status),
                r.NominatedBidId?.ToString()??"None",r.AwardedBidId?.ToString()??"None",r.Approvals+" / 4",
                Outcome(r),r.MyDecision??"Not voted",CommitteeDisplay.Utc(r.DecisionAt)]);
        }

        if (p.SystemStatistics is { } registered && registered.SuppliersByStatus.Count > 0)
            section.AddParagraph("Registered suppliers by status: " + string.Join(" · ", registered.SuppliersByStatus.OrderBy(x => x.Key).Select(x => $"{x.Key}: {x.Value:N0}")));
        if(p.Rows.Count>12)section.AddPageBreak(); Heading(section,"C · Supplier statistics");
        section.AddParagraph("System-wide · all suppliers, including those with zero awards · independent of tender filters.").Format.SpaceAfter=6;
        section.AddParagraph(AwardDefinition).Format.SpaceAfter=12;
        if(p.Suppliers.Count==0)section.AddParagraph("No suppliers recorded.");
        else
        {
            var table=Table(section,["Supplier ID","Supplier name","Awarded Tenders","Status","Invited","Dealings","Win rate","Per month"],[2.2,7,3,3.3,2.2,2.3,2.7,3]);
            foreach(var s in p.Suppliers)AddRow(table,[s.SupplierId.ToString(),s.SupplierName,s.AwardedTenders.ToString("N0"),s.Status,s.Invited.ToString("N0"),s.Dealings.ToString("N0"),s.WinRate.ToString("P1"),s.Frequency.ToString("N1")]);
        }

        if(p.Suppliers.Count>12)section.AddPageBreak(); Heading(section,"D · Decision and audit summary");
        var auditIntro=section.AddParagraph("Matching assigned tenders only. This summary describes persisted votes and outcomes; it is not a complete audit trail."); auditIntro.Format.SpaceAfter=8; auditIntro.Format.KeepWithNext=true;
        foreach(var m in DecisionMetrics(p))section.AddParagraph($"{m.Label}: {m.Count:N0}").Format.KeepWithNext=true;
        var auditNote=section.AddParagraph("Latest committee activity preserves the award timestamp, or the latest Top-stage vote when no award is recorded. It is separate from your individual decision timestamp."); auditNote.Format.SpaceAfter=12; auditNote.Format.KeepWithNext=p.Rows.Count>0;
        if(p.Rows.Count>0)
        {
            var table=Table(section,["Tender ID","Final outcome","Latest committee activity (UTC)","Awarded at (UTC)"],[2,6,9.7,8]);
            foreach(var r in p.Rows)AddRow(table,[r.TenderId.ToString(),Outcome(r),CommitteeDisplay.Utc(r.CommitteeOutcomeAt),CommitteeDisplay.Utc(r.AwardedAt)]);
        }
        return RenderPdf(doc);
    }

    // The existing CEO & Chiefs document setup is shared by every report export.
    internal static Document CreatePdfDocument(string reportTitle)
    {
        lock(FontLock) { if(GlobalFontSettings.FontResolver==null)GlobalFontSettings.FontResolver=new ReportFontResolver(); }
        var doc=new Document(); doc.Info.Title=reportTitle;
        doc.Styles["Normal"]!.Font.Name="CommitteeReport"; doc.Styles["Normal"]!.Font.Size=9;
        var section=doc.AddSection(); section.PageSetup.Orientation=Orientation.Landscape;
        section.PageSetup.PageFormat=PageFormat.A4; section.PageSetup.LeftMargin=Unit.FromCentimeter(1.5);
        section.PageSetup.RightMargin=Unit.FromCentimeter(1.5);
        section.PageSetup.TopMargin=Unit.FromCentimeter(1.5); section.PageSetup.BottomMargin=Unit.FromCentimeter(1.5);
        return doc;
    }

    internal static byte[] RenderPdf(Document doc)
    {
        var renderer=new PdfDocumentRenderer { Document=doc }; renderer.RenderDocument();
        using var stream=new MemoryStream(); renderer.PdfDocument.Save(stream,false); return stream.ToArray();
    }

    internal static Paragraph AddPdfText(Section section,string? text)
    {
        Paragraph? last=null;
        foreach(var line in WrapPdfText(text,Unit.FromCentimeter(26.7).Point))
        {last=section.AddParagraph(line);last.Format.KeepTogether=false;last.Format.KeepWithNext=false;}
        return last!;
    }

    internal static void Heading(Section section,string text)
    {
        var p=section.AddParagraph(string.Join("\n",WrapPdfText(text,Unit.FromCentimeter(26.7).Point,13,XFontStyleEx.Bold))); p.Format.Font.Size=13; p.Format.Font.Bold=true;
        p.Format.Font.Color=Color.FromRgb(0,100,103); p.Format.SpaceBefore=10; p.Format.SpaceAfter=10; p.Format.KeepWithNext=true;
    }
    internal static Table Table(Section section,string[] headers,double[] widths)
    {
        var table=section.AddTable(); table.Borders.Width=.5; table.Borders.Color=Colors.LightGray;
        table.TopPadding=4; table.BottomPadding=4;
        foreach(var w in widths)table.AddColumn(Unit.FromCentimeter(w));
        var head=table.AddRow(); head.HeadingFormat=true; head.Format.Font.Bold=true;
        head.Shading.Color=Color.FromRgb(238,248,246);
        for(int i=0;i<headers.Length;i++)head.Cells[i].AddParagraph(headers[i]);
        return table;
    }
    internal static void AddRow(Table table,string[] values)
    {
        if(values.Length!=table.Columns.Count)throw new ArgumentException("Report row has an incorrect column count.");
        var wrapped=values.Select((value,i)=>WrapPdfText(value,table.Columns[i].Width.Point-10)).ToArray();
        // MigraDoc cannot split a single oversized row across pages. Explicit
        // continuation rows keep every measured line within the printable height.
        const int linesPerRow=22;
        for(var offset=0;offset<wrapped.Max(lines=>lines.Count);offset+=linesPerRow)
        {
            var row=table.AddRow();
            for(int i=0;i<values.Length;i++)
                row.Cells[i].AddParagraph(string.Join("\n",wrapped[i].Skip(offset).Take(linesPerRow)));
        }
    }

    internal static List<string> WrapPdfText(string? text,double width,double fontSize=9,XFontStyleEx fontStyle=XFontStyleEx.Regular)
    {
        using var graphics=XGraphics.CreateMeasureContext(new XSize(1000,1000),XGraphicsUnit.Point,XPageDirection.Downwards);
        var font=new XFont("CommitteeReport",fontSize,fontStyle);
        var lines=new List<string>();
        foreach(var original in (text??"").Replace("\r\n","\n").Replace('\r','\n').Split('\n'))
        {
            var remaining=original;
            while(remaining.Length>0 && graphics.MeasureString(remaining,font).Width>width)
            {
                var starts=StringInfo.ParseCombiningCharacters(remaining);
                var low=1;var high=starts.Length;
                while(low<high)
                {
                    var middle=(low+high+1)/2;
                    var end=middle==starts.Length?remaining.Length:starts[middle];
                    if(graphics.MeasureString(remaining[..end],font).Width<=width)low=middle;else high=middle-1;
                }
                var length=low==starts.Length?remaining.Length:starts[low];
                var space=remaining.LastIndexOf(' ',Math.Max(0,length-1),length);
                if(space>0)length=space;
                lines.Add(remaining[..length]);remaining=remaining[length..].TrimStart(' ');
            }
            lines.Add(remaining);
        }
        return lines;
    }
    private sealed class ReportFontResolver : IFontResolver
    {
        public FontResolverInfo ResolveTypeface(string familyName,bool bold,bool italic)=>new("CommitteeReport",bold,italic);
        public byte[] GetFont(string faceName)=>File.ReadAllBytes(Environment.GetEnvironmentVariable("TOP_COMMITTEE_REPORT_FONT")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts),"arial.ttf"));
    }
}
