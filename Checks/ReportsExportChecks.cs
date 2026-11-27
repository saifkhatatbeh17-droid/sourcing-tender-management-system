using System.Security.Claims;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SourcingTenderManagement.Controllers;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Services.Reports.Hub;
using PdfDocument = UglyToad.PdfPig.PdfDocument;

namespace SourcingTenderManagement.Checks;

public static class ReportsExportChecks
{
    // Supply only the isolated loopback clients created by the Reports checks host.
    public static async Task RunCompatibilityHttpChecksAsync(HttpClient admin, HttpClient officer, HttpClient committee)
    {
        foreach (var client in new[] { admin, officer, committee })
            if (client.BaseAddress is not { IsLoopback: true } ||
                !client.DefaultRequestHeaders.Contains("X-Reports-Check-Token"))
                throw new InvalidOperationException("Compatibility checks require the isolated Reports host.");
        var checks = 0;
        foreach (var path in new[] { "/Reports/top-committee", "/Reports/top-committee/export?format=pdf", "/Reports/top-committee/export?format=xlsx" })
        {
            using var denied = await officer.GetAsync(path);
            if ((int)denied.StatusCode != 403) throw new Exception("Incompatible committee grant did not return 403: " + path);
            checks++;
            foreach (var client in new[] { admin, committee })
            {
                using var allowed = await client.GetAsync(path);
                allowed.EnsureSuccessStatusCode();
                checks++;
            }
        }
        var html = await admin.GetStringAsync("/Reports/Access");
        var row = System.Text.RegularExpressions.Regex.Match(html, @"<tr><th scope=""row"">Top Committee statistics</th>(.*?)</tr>",
            System.Text.RegularExpressions.RegexOptions.Singleline).Value;
        var input = System.Text.RegularExpressions.Regex.Match(row, @"<input[^>]*data-report-role=""SourcingOfficer""[^>]*>").Value;
        if (!input.Contains("disabled") || input.Contains("checked") || !row.Contains("Unavailable"))
            throw new Exception("Unsupported committee access must be unavailable in rendered Admin UI.");
        checks++;
        var token = System.Text.RegularExpressions.Regex.Match(html, @"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""").Groups[1].Value;
        if (token.Length == 0) throw new Exception("Missing Admin antiforgery token.");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/Reports/Access")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new[] { new { ReportKey = "top-committee", Role = RoleNames.SourcingOfficer, Allowed = true } })
        };
        request.Headers.Add("RequestVerificationToken", System.Net.WebUtility.HtmlDecode(token));
        using var response = await admin.SendAsync(request);
        if ((int)response.StatusCode != 400 || !(await response.Content.ReadAsStringAsync()).Contains("supported editable role"))
            throw new Exception("Authenticated, antiforgery-valid unsupported permission POST must fail validation.");
        checks++;
        Console.WriteLine($"Reports compatibility HTTP checks passed: {checks}. Real Top Committee report, isolated loopback only.");
    }

    public static async Task RunAsync(ApplicationDbContext db, string outputDirectory)
    {
        if (db.Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite" ||
            db.Database.GetDbConnection().ConnectionString != "Data Source=:memory:")
            throw new InvalidOperationException("Reports export checks require a SQLite in-memory database.");
        Directory.CreateDirectory(outputDirectory);
        var checks = 0;
        void Require(bool condition, string label)
        {
            if (!condition) throw new Exception("Reports export check failed: " + label);
            checks++;
        }

        var report = new ExportFixture();
        var exporter = new ReportExport();
        var generated = new DateTime(2026, 11, 22, 12, 30, 0, DateTimeKind.Utc);
        var query = new ReportQuery(new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1),
            Status: "Approved", Search: "needle & value", Drill: "status:Approved", ForExport: true);
        var result = new ReportResult([new("Total", "2")], null,
            new(["Reference", "Quantity", "Amount", "Currency", "Notes", "Identifier"],
            [new(["REC-1", "12", "1,234.50", "JD", "=HYPERLINK(\"https://example.invalid\")", "00123"]),
             new(["REC-2", "-2.5", "9.00", "JD", "Recorded text", "12345678901234567"])], 2), ["Recorded report note."]);
        var bytes = exporter.Excel(report, query, result, generated);
        using (var workbook = new XLWorkbook(new MemoryStream(bytes)))
        {
            Require(workbook.Worksheets.Count == 2 && workbook.Worksheets.Contains("Summary") && workbook.Worksheets.Contains("Data"),
                "Summary and Data sheets open in ClosedXML");
            var summary = workbook.Worksheet("Summary");
            var data = workbook.Worksheet("Data");
            Require(summary.Cell(1, 1).GetString() == report.Title && summary.Cell(2, 2).GetDateTime() == generated,
                "title and UTC timestamp exported");
            Require(summary.CellsUsed().Any(cell => cell.GetString() == query.Search) &&
                summary.CellsUsed().Any(cell => cell.GetString() == query.Drill) &&
                summary.CellsUsed().Any(cell => cell.GetString() == "Recorded report note."), "filters, drill and notes exported");
            Require(data.Cell(1, 3).GetString() == "Amount" && data.Cell(1, 3).Style.Font.Bold && data.SheetView.SplitRow == 1,
                "header text, bold style and frozen header");
            Require(data.Cell(2, 2).DataType == XLDataType.Number && data.Cell(2, 2).GetDouble() == 12 &&
                data.Cell(2, 3).GetDouble() == 1234.5 && data.Cell(3, 2).GetDouble() == -2.5, "numbers preserved");
            Require(data.Cell(2, 6).DataType == XLDataType.Text && data.Cell(2, 6).GetString() == "00123" &&
                data.Cell(3, 6).DataType == XLDataType.Text, "identifier zeros and precision preserved");
            Require(data.Cell(2, 5).GetString().StartsWith("=HYPERLINK") && !data.Cell(2, 5).HasFormula, "untrusted text is never a formula");
            Require(data.ColumnsUsed().All(column => column.Width <= 60), "column width capped at 60");
        }
        await File.WriteAllBytesAsync(Path.Combine(outputDirectory, "generic-report.xlsx"), bytes);

        var longText = "LONGTEXTSTART " + string.Join(" ", Enumerable.Repeat("Complete recorded observations.", 90)) + " LONGTEXTEND";
        var pdfRows = Enumerable.Range(1, 110).Select(index => new ReportRow(["ROW-" + index, index == 1 ? longText : "Recorded observation " + index])).ToList();
        var pdfResult = result with { Table = new ReportTable(["Reference", "Comments"], pdfRows, pdfRows.Count) };
        var pdf = exporter.Pdf(report, query, pdfResult, generated);
        await File.WriteAllBytesAsync(Path.Combine(outputDirectory, "generic-report.pdf"), pdf);
        Require(System.Text.Encoding.ASCII.GetString(pdf, 0, 4) == "%PDF", "PDF signature");
        using (var document = PdfDocument.Open(pdf))
        {
            var pages = document.GetPages().ToList();
            var text = string.Join(" ", pages.Select(page => page.Text));
            Require(pages.Count > 1 && text.Contains("LONGTEXTSTART") && text.Contains("LONGTEXTEND") && text.Contains("ROW-110"),
                "long text and last row retained across pages");
            Require(pages.Where(page => page.Text.Contains("ROW-")).All(page => page.Text.Contains("Reference") && page.Text.Contains("Comments")),
                "table headers repeat on data pages");
            var compact = string.Concat(text.Where(character => !char.IsWhiteSpace(character)));
            Require(compact.Contains("needle&value") && compact.Contains("status:Approved") && compact.Contains("Recordedreportnote."),
                "PDF includes applied filters, drill and notes");
        }

        var overflowing = result with { Table = new ReportTable(["Reference"], Enumerable.Range(1, 5001)
            .Select(index => new ReportRow(["LIMIT-" + index])).ToList(), 5001) };
        var capped = ReportExport.Prepare(overflowing);
        Require(capped.Table.Rows.Count == 5000 && capped.Notes.Contains(ReportExport.LimitNote), "central cap and explanatory note");
        using (var workbook = new XLWorkbook(new MemoryStream(exporter.Excel(report, query, overflowing, generated))))
        {
            Require(workbook.Worksheet("Data").LastRowUsed()!.RowNumber() == 5001 &&
                workbook.Worksheet("Data").Cell(5001, 1).GetString() == "LIMIT-5000", "Excel cannot exceed 5,000 data rows");
            Require(workbook.Worksheet("Summary").CellsUsed().Any(cell => cell.GetString() == ReportExport.LimitNote), "Excel explains truncation");
        }
        using (var document = PdfDocument.Open(exporter.Pdf(report, query, overflowing, generated)))
        {
            var text = string.Concat(document.GetPages().SelectMany(page => page.Text).Where(character => !char.IsWhiteSpace(character)));
            Require(text.Contains("LIMIT-5000") && !text.Contains("LIMIT-5001") && text.Contains("Exportlimitedtothefirst5,000matchingrecords."),
                "PDF cannot exceed 5,000 rows and explains truncation");
        }
        var wideResult = result with { Table = new ReportTable(Enumerable.Range(1, 18).Select(index => "Column " + index).ToList(),
            [new ReportRow(Enumerable.Range(1, 18).Select(index => "Value-" + index).ToList())], 1) };
        var widePdf = exporter.Pdf(report, query, wideResult, generated);
        await File.WriteAllBytesAsync(Path.Combine(outputDirectory, "generic-wide-report.pdf"), widePdf);
        using (var document = PdfDocument.Open(widePdf))
            Require(document.GetPages().Any(page => page.Width > 1100 && page.Text.Contains("Value-18")),
                "wide report uses landscape A3 and retains the final column");
        try
        {
            _ = ReportExport.Prepare(result with { Table = result.Table with { Total = 100 } });
            throw new Exception("A paginated partial export was accepted.");
        }
        catch (InvalidOperationException) { checks++; }

        var committee = new ExportFixture("top-committee");
        var registry = new ReportRegistry([report, committee], db);
        var serviceCollection = new ServiceCollection().AddLogging();
        serviceCollection.AddControllersWithViews();
        using var services = serviceCollection.BuildServiceProvider();
        ReportsController Controller(string role) => new(db, registry, new ReportFilterOptions(db), exporter, NullLogger<ReportsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
            {
                RequestServices = services,
                User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim(ClaimTypes.NameIdentifier, "hub-" + role), new Claim(ClaimTypes.Role, role)], "Checks"))
            }}
        };
        var officer = Controller(RoleNames.SourcingOfficer);
        var oldRule = await db.ReportAccessRules.SingleAsync(rule => rule.ReportKey == committee.Key && rule.Role == RoleNames.SourcingOfficer);
        oldRule.Allowed = true;
        await db.SaveChangesAsync();
        Require(await officer.Show(committee.Key, new(), default) is StatusCodeResult { StatusCode: 403 },
            "direct committee URL rejects incompatible stored Allow without executing report");
        foreach (var format in new[] { "pdf", "xlsx" })
            Require(await officer.Export(committee.Key, new(), format, default) is StatusCodeResult { StatusCode: 403 },
                "committee " + format + " rejects incompatible stored Allow without HTTP 500");
        Require(committee.LastScope == null, "incompatible role never reaches committee implementation");
        foreach (var role in new[] { RoleNames.Admin, RoleNames.TopCommittee })
        {
            Require(await Controller(role).Show(committee.Key, new(), default) is ViewResult, "supported committee page " + role);
            foreach (var format in new[] { "pdf", "xlsx" })
                Require(await Controller(role).Export(committee.Key, new(), format, default) is FileContentResult,
                    "supported committee export " + role + " " + format);
        }
        committee.RefuseAccess = true;
        Require(await Controller(RoleNames.TopCommittee).Show(committee.Key, new(), default) is StatusCodeResult { StatusCode: 403 },
            "committee service denial is mapped to 403 on page");
        foreach (var format in new[] { "pdf", "xlsx" })
            Require(await Controller(RoleNames.TopCommittee).Export(committee.Key, new(), format, default) is StatusCodeResult { StatusCode: 403 },
                "committee service denial is mapped to 403 on " + format);
        committee.RefuseAccess = false;
        Require(await officer.Export("missing", query, "xlsx", default) is NotFoundResult, "unknown export key returns 404");
        Require(await Controller(RoleNames.Department).Export(report.Key, query, "xlsx", default) is StatusCodeResult { StatusCode: 403 },
            "unauthorized export returns 403");
        Require(await Controller(RoleNames.Supplier).Export(report.Key, query, "pdf", default) is StatusCodeResult { StatusCode: 403 },
            "Supplier export returns 403");
        Require(await officer.Export(report.Key, query, "csv", default) is BadRequestObjectResult, "unsupported format rejected");
        var file = (FileContentResult)await officer.Export(report.Key, query with { Page = 44 }, "xlsx", default);
        Require(file.ContentType == ReportExport.ExcelContentType && file.FileDownloadName.StartsWith(report.Key + "-report-") &&
            file.FileDownloadName.EndsWith(".xlsx"), "Excel MIME and filename");
        Require(report.LastQuery is { ForExport: true, Page: 1 } && report.LastQuery.Search == query.Search && report.LastQuery.Drill == query.Drill &&
            report.LastQuery.From == query.From && report.LastQuery.To == query.To, "export removes pagination and preserves filters/drill");
        Require(report.LastScope is { IsAdmin: false, Role: RoleNames.SourcingOfficer, UserId: "hub-SourcingOfficer" },
            "export uses actual user scope");
        using (var workbook = new XLWorkbook(new MemoryStream(file.FileContents)))
            Require(workbook.Worksheet("Data").LastRowUsed()!.RowNumber() == 52 && workbook.Worksheet("Data").Cell(2, 1).GetString() == "hub-SourcingOfficer",
                "all matching rows and scoped values exported");
        var pdfFile = (FileContentResult)await officer.Export(report.Key, query, "pdf", default);
        Require(pdfFile.ContentType == "application/pdf" && pdfFile.FileDownloadName.EndsWith(".pdf"), "PDF MIME and filename");
        var invalid = Controller(RoleNames.SourcingOfficer);
        Require(await invalid.Export(report.Key, query with { From = new DateOnly(2027, 1, 1) }, "xlsx", default) is ViewResult &&
            invalid.Response.StatusCode == 400, "invalid export filters show an inline error instead of exporting a wider result");
        Require(ReportQueryValidation.Normalize(report, query with { Drill = "decision:invalid" }).Messages.Count > 0,
            "invalid decision drill does not reach Person B validation");
        db.ReportAccessRules.Add(new ReportAccessRule { ReportKey = report.Key, Role = RoleNames.SourcingOfficer,
            Allowed = false, UpdatedByUserId = "hub-Admin" });
        await db.SaveChangesAsync();
        Require(await officer.Export(report.Key, query, "xlsx", default) is StatusCodeResult { StatusCode: 403 }, "stored Deny blocks export");
        Console.WriteLine($"Reports export checks passed: {checks}. SQLite in-memory only.");
    }

    private sealed class ExportFixture(string key = "checks-export") : IReport
    {
        public bool RefuseAccess { get; set; }
        public ReportQuery? LastQuery { get; private set; }
        public ReportScope? LastScope { get; private set; }
        public string Key => key;
        public string Title => "Generic export checks";
        public string Description => "Isolated export fixture";
        public string Icon => "file";
        public IReadOnlyList<string> DefaultRoles => key == "top-committee" ? [RoleNames.TopCommittee] : [RoleNames.SourcingOfficer];
        public IReadOnlyList<string> Filters => ["date", "status", "search"];
        public IReadOnlyList<string> StatusOptions => ["Approved"];
        public Task<ReportResult> RunAsync(ReportQuery query, ReportScope scope, CancellationToken ct)
        {
            if (RefuseAccess) throw new SourcingTenderManagement.Contracts.TopCommittee.CommitteeAccessDeniedException();
            LastQuery = query;
            LastScope = scope;
            var rows = Enumerable.Range(1, 51).Select(index => new ReportRow([scope.UserId, index.ToString()])).ToList();
            return Task.FromResult(new ReportResult([], null, new ReportTable(["User", "Reference"], rows, rows.Count), []));
        }
    }
}
