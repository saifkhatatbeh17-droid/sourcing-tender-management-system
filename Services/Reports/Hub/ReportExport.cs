using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using MigraDoc.DocumentObjectModel;
using SourcingTenderManagement.Services.TopCommittee;

namespace SourcingTenderManagement.Services.Reports.Hub;

// Turns any Reports hub report into an Excel or PDF file.
public sealed class ReportExport
{
    public const int MaximumRows = 5000;
    public const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string LimitNote = "Export limited to the first 5,000 matching records. Narrow the filters to export a smaller set.";

    public static ReportResult Prepare(ReportResult result)
    {
        if (result.Table.Rows.Count < Math.Min(MaximumRows, result.Table.Total))
            throw new InvalidOperationException("The report returned an incomplete export. No partial file can be downloaded.");

        var limited = result.Table.Rows.Count > MaximumRows || result.Table.Total > MaximumRows;
        return result with
        {
            Table = result.Table with { Rows = result.Table.Rows.Take(MaximumRows).ToList() },
            Notes = limited ? result.Notes.Append(LimitNote).Distinct().ToList() : result.Notes
        };
    }

    public byte[] Excel(IReport report, ReportQuery query, ReportResult source, DateTime generatedAtUtc)
    {
        var result = Prepare(source);
        using var workbook = new XLWorkbook();
        var summary = workbook.AddWorksheet("Summary");
        summary.Cell(1, 1).Value = report.Title;
        summary.Range(1, 1, 1, 2).Merge();
        summary.Cell(1, 1).Style.Font.Bold = true;
        summary.Cell(1, 1).Style.Font.FontSize = 18;
        summary.Cell(2, 1).Value = "Generated UTC";
        summary.Cell(2, 2).Value = generatedAtUtc;
        summary.Cell(2, 2).Style.DateFormat.Format = "dd mmm yyyy hh:mm:ss \"UTC\"";
        summary.Cell(3, 1).Value = "Exported records";
        summary.Cell(3, 2).Value = $"{result.Table.Rows.Count:N0} of {result.Table.Total:N0} matching records";
        var row = 4;
        WriteHeading(summary, row++, "Applied filter", "Value");
        foreach (var filter in AppliedFilters(query))
        {
            summary.Cell(row, 1).Value = filter.Key;
            summary.Cell(row++, 2).Value = filter.Value;
        }
        row++;
        WriteHeading(summary, row++, "KPI", "Value");
        foreach (var kpi in result.Kpis)
        {
            summary.Cell(row, 1).Value = kpi.Label;
            WriteValue(summary.Cell(row++, 2), kpi.Value);
        }
        row++;
        WriteHeading(summary, row++, "Report notes", "");
        foreach (var note in result.Notes)
        {
            summary.Cell(row, 1).Value = note;
            summary.Range(row, 1, row, 2).Merge().Style.Alignment.WrapText = true;
            row++;
        }
        summary.Columns(1, 2).AdjustToContents(1, row, 12, 60);
        summary.Rows(1, row).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        summary.Columns(1, 2).Style.Alignment.WrapText = true;

        var data = workbook.AddWorksheet("Data");
        var columns = result.Table.Columns;
        if (columns.Count > 0)
        {
            WriteHeading(data, 1, columns.ToArray());
            for (var index = 0; index < result.Table.Rows.Count; index++)
            {
                var cells = result.Table.Rows[index].Cells;
                for (var column = 0; column < columns.Count; column++)
                    WriteValue(data.Cell(index + 2, column + 1), column < cells.Count ? cells[column] : "");
            }
            data.Columns(1, columns.Count).AdjustToContents(1, result.Table.Rows.Count + 1, 10, 60);
            data.Range(1, 1, Math.Max(1, result.Table.Rows.Count + 1), columns.Count).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
            data.Columns(1, columns.Count).Style.Alignment.WrapText = true;
        }
        data.SheetView.FreezeRows(1);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public byte[] Pdf(IReport report, ReportQuery query, ReportResult source, DateTime generatedAtUtc)
    {
        var result = Prepare(source);
        var document = TopCommitteeReportExport.CreatePdfDocument(report.Title);
        var section = document.LastSection;
        // Wider data tables use a larger landscape sheet while retaining the existing report style.
        var wide = result.Table.Columns.Count > 10;
        if (wide) section.PageSetup.PageFormat = PageFormat.A3;
        var title = section.AddParagraph(report.Title);
        title.Format.Font.Size = 20;
        title.Format.Font.Bold = true;
        title.Format.Font.Color = Color.FromRgb(29, 85, 96);
        TopCommitteeReportExport.AddPdfText(section, "Generated " + generatedAtUtc.ToString("dd MMM yyyy HH:mm:ss 'UTC'", CultureInfo.InvariantCulture));
        var footer = section.Footers.Primary.AddParagraph();
        footer.AddText("TIRYAQ | Page ");
        footer.AddPageField();
        footer.AddText(" of ");
        footer.AddNumPagesField();

        TopCommitteeReportExport.Heading(section, "Applied filters");
        foreach (var filter in AppliedFilters(query))
            TopCommitteeReportExport.AddPdfText(section, filter.Key + " · " + filter.Value);
        if (result.Kpis.Count > 0)
        {
            TopCommitteeReportExport.Heading(section, "Summary");
            var kpis = TopCommitteeReportExport.Table(section, ["KPI", "Value"], [16, 10.7]);
            foreach (var kpi in result.Kpis)
                TopCommitteeReportExport.AddRow(kpis, [kpi.Label, kpi.Value]);
        }
        TopCommitteeReportExport.Heading(section, "Data");
        TopCommitteeReportExport.AddPdfText(section, $"{result.Table.Rows.Count:N0} of {result.Table.Total:N0} matching records exported.");
        if (result.Table.Columns.Count == 0 || result.Table.Rows.Count == 0)
            TopCommitteeReportExport.AddPdfText(section, "No matching records.");
        else
        {
            var width = (wide ? 39d : 26.7d) / result.Table.Columns.Count;
            var headers = result.Table.Columns.Select(header => string.Join("\n",
                TopCommitteeReportExport.WrapPdfText(header, Unit.FromCentimeter(width).Point - 10,
                    fontStyle: PdfSharp.Drawing.XFontStyleEx.Bold))).ToArray();
            var table = TopCommitteeReportExport.Table(section, headers,
                Enumerable.Repeat(width, headers.Length).ToArray());
            foreach (var record in result.Table.Rows)
                TopCommitteeReportExport.AddRow(table, Enumerable.Range(0, headers.Length)
                    .Select(index => index < record.Cells.Count ? record.Cells[index] : "").ToArray());
        }
        if (result.Notes.Count > 0)
        {
            TopCommitteeReportExport.Heading(section, "Report notes");
            foreach (var note in result.Notes) TopCommitteeReportExport.AddPdfText(section, note);
        }
        return TopCommitteeReportExport.RenderPdf(document);
    }

    private static IReadOnlyList<KeyValuePair<string, string>> AppliedFilters(ReportQuery query)
    {
        var filters = new List<KeyValuePair<string, string>>();
        if (query.From.HasValue) filters.Add(new("From UTC", query.From.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)));
        if (query.To.HasValue) filters.Add(new("To UTC", query.To.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)));
        if (query.DepartmentId.HasValue) filters.Add(new("Department ID", query.DepartmentId.Value.ToString(CultureInfo.InvariantCulture)));
        if (query.SupplierId.HasValue) filters.Add(new("Supplier ID", query.SupplierId.Value.ToString(CultureInfo.InvariantCulture)));
        if (query.TenderId.HasValue) filters.Add(new("Tender ID", query.TenderId.Value.ToString(CultureInfo.InvariantCulture)));
        if (query.Status != null) filters.Add(new("Status", query.Status));
        if (query.Search != null) filters.Add(new("Search", query.Search));
        if (query.Drill != null) filters.Add(new("Drill-down", query.Drill));
        if (filters.Count == 0) filters.Add(new("Filters", "All records in your authorized workspace"));
        return filters;
    }

    private static void WriteHeading(IXLWorksheet sheet, int row, params string[] values)
    {
        for (var column = 0; column < values.Length; column++) sheet.Cell(row, column + 1).Value = values[column];
        var heading = sheet.Range(row, 1, row, values.Length);
        heading.Style.Font.Bold = true;
        heading.Style.Fill.BackgroundColor = XLColor.FromHtml("#1d5560");
        heading.Style.Font.FontColor = XLColor.White;
        heading.Style.Alignment.WrapText = true;
    }

    private static void WriteValue(IXLCell cell, string value)
    {
        // Preserve leading-zero identifiers and values beyond Excel's 15-digit numeric precision.
        var digits = value.Where(char.IsDigit).ToArray();
        if (digits.Length <= 15 && Regex.IsMatch(value, @"\A-?(?:0|[1-9]\d*|[1-9]\d{0,2}(?:,\d{3})+)(?:\.\d+)?\z") &&
            decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
            cell.Value = number;
        else
            cell.Value = value; // Assign text, never a spreadsheet formula.
    }
}
