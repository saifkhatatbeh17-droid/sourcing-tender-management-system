using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models;

namespace SourcingTenderManagement.Controllers;

[Authorize]
public class ReportingController(ApplicationDbContext db, UserManager<ApplicationUser> users) : Controller
{
    public async Task<IActionResult> Index(string? report, string? format, string? search, CancellationToken ct)
    {
        var actor = await users.GetUserAsync(User);
        if (actor?.IsActive != true) return Forbid();
        var admin = User.IsInRole(RoleNames.Admin);
        var officer = User.IsInRole(RoleNames.SourcingOfficer);
        var manager = User.IsInRole(RoleNames.SourcingManager);
        var evaluator = User.IsInRole(RoleNames.EvaluationCommittee);
        var top = User.IsInRole(RoleNames.TopCommittee);
        var department = User.IsInRole(RoleNames.Department);
        var supplier = User.IsInRole(RoleNames.Supplier);
        if (supplier && !await db.SupplierProfiles.AnyAsync(s => s.UserId == actor.Id && s.Status == "Approved", ct)) return Forbid();
        var choices = new Dictionary<string, string>();
        if (admin || officer || department) choices.Add("requests", "Purchase requests");
        if (admin || officer || manager || top) choices.Add("tenders", "Tenders");
        if (admin) choices.Add("suppliers", "Supplier verification");
        if (officer || manager || evaluator || top || supplier) choices.Add("offers", supplier ? "My submitted offers" : "Submitted bids");
        if (officer || manager || evaluator || top) choices.Add("evaluations", "Evaluation results");
        if (officer || manager || top) choices.Add("decisions", "Approval and award history");
        if (top) choices.Add("statistics", "System statistics");
        if (choices.Count == 0) return Forbid();
        if (report != null && !choices.ContainsKey(report)) return Forbid();
        if (format is not (null or "pdf" or "xlsx")) return BadRequest("Choose PDF or Excel.");
        report ??= choices.Keys.First();
        var data = new DownloadableReport(choices[report], [], []);
        var tenders = db.Tenders.AsNoTracking().Where(t =>
            admin || (officer && t.CreatedByUserId == actor.Id) ||
            (manager && (t.Status == "AwaitingManagerReview" || t.TenderApprovals.Any(a => a.Stage == "Manager" && a.UserId == actor.Id))) ||
            t.CommitteeAssignments.Any(a => a.UserId == actor.Id &&
                ((evaluator && a.CommitteeType == "Evaluation") || (top && a.CommitteeType == "Top"))));
        if (report == "requests")
        {
            var requests = await db.PurchaseRequests.AsNoTracking()
                .Where(r => admin || (department && r.RequestedByUserId == actor.Id && r.DepartmentId == actor.DepartmentId) ||
                    (officer && r.ReviewedByUserId == actor.Id))
                .Select(r => new { r.Title, Department = r.Department.Name, r.Status, r.SubmittedAt, r.ReviewedAt })
                .ToListAsync(ct);
            data = data with { Columns = ["Request", "Department", "Status", "Submitted UTC", "Reviewed UTC"] };
            data.Rows.AddRange(requests.Select(r => new[] { r.Title, r.Department, r.Status, Date(r.SubmittedAt), Date(r.ReviewedAt) }));
            data.Rows.Add(["Total requests", "", "", requests.Count.ToString(), ""]);
            foreach (var group in requests.GroupBy(r => r.Status))
                data.Rows.Add(["Status total", "", group.Key, group.Count().ToString(), ""]);
            foreach (var group in requests.GroupBy(r => r.Department))
                data.Rows.Add(["Department total", group.Key, "", group.Count().ToString(), ""]);
            var durations = requests.Where(r => r.Status is "Approved" or "ConvertedToTender")
                .Where(r => r.SubmittedAt.HasValue && r.ReviewedAt >= r.SubmittedAt)
                .Select(r => (r.ReviewedAt!.Value - r.SubmittedAt!.Value).TotalHours).ToArray();
            if (durations.Length > 0) data.Rows.Add(["Average approval hours", "", "", durations.Average().ToString("N1"), ""]);
        }
        else if (report == "tenders")
        {
            var rows = await tenders.OrderBy(t => t.Title).Select(t => new
            {
                t.Title, t.Status, t.ClosesAt, Invitations = t.Invitations.Count,
                Responses = t.Invitations.Count(i => i.Bids.Any(b => b.SubmittedAt != null && b.Status != "Withdrawn")),
                ReportDate = t.EvaluationReport == null ? null : t.EvaluationReport.SubmittedAt
            }).ToListAsync(ct);
            data = data with { Columns = ["Tender", "Status", "Closes UTC", "Submitted / invited", "Participation", "Days from close to report"] };
            data.Rows.AddRange(rows.Select(r => new[] { r.Title, r.Status, Date(r.ClosesAt),
                $"{r.Responses} / {r.Invitations}", r.Invitations == 0 ? "" : ((double)r.Responses / r.Invitations).ToString("P0"),
                r.ReportDate >= r.ClosesAt ? (r.ReportDate!.Value - r.ClosesAt).TotalDays.ToString("N1") : "" }));
            data.Rows.Add(["Tenders with zero submitted bids", "", "", rows.Count(r => r.Responses == 0).ToString(), "", ""]);
            foreach (var group in rows.GroupBy(r => r.Status))
                data.Rows.Add(["Status total", group.Key, "", group.Count().ToString(), "", ""]);
        }
        else if (report == "suppliers")
        {
            var rows = await db.SupplierProfiles.AsNoTracking().Include(s => s.SupplierDocuments).OrderBy(s => s.CompanyName).ToListAsync(ct);
            data = data with { Columns = ["Company", "Registration", "Verification", "Legal documents and expiry", "Review date UTC"] };
            data.Rows.AddRange(rows.Select(s => new[] { s.CompanyName, s.RegistrationNumber, s.Status,
                string.Join("\n", s.SupplierDocuments.Select(d => $"{d.DocumentType} · {d.ExpiresOn:yyyy-MM-dd}")),
                Date(s.ReviewedAt) }));
            foreach (var group in rows.GroupBy(s => s.Status))
                data.Rows.Add(["Verification total", "", group.Key, group.Count().ToString(), ""]);
        }
        else if (report == "offers")
        {
            var ids = tenders.Select(t => t.TenderId);
            var rows = await db.Bids.AsNoTracking().Where(b => b.SubmittedAt != null &&
                ((supplier && b.Invitation.SupplierProfile.UserId == actor.Id) ||
                    ((officer || manager || evaluator || top) && ids.Contains(b.TenderId))))
                .Select(b => new { b.TenderId, b.BidId, b.Invitation.SupplierProfile.CompanyName, b.Status,
                    b.ComplianceStatus, ComplianceNotes = supplier ? null : b.ComplianceNotes, b.DeliveryDays, b.Currency,
                    Total = b.BidItems.Sum(i => i.Quantity * i.UnitPrice) + b.TaxAmount + b.AdditionalCosts })
                .OrderBy(b => b.TenderId).ThenBy(b => b.BidId).ToListAsync(ct);
            data = data with { Columns = ["Tender", "Bid", "Supplier", "Total", "Delivery days", "Status", "Compliance"] };
            data.Rows.AddRange(rows.Select(b => new[] { b.TenderId.ToString(), b.BidId.ToString(), b.CompanyName,
                $"{b.Total:N2} {b.Currency}", b.DeliveryDays.ToString(), b.Status,
                $"{b.ComplianceStatus} {b.ComplianceNotes}".Trim() }));
        }
        else if (report == "evaluations")
        {
            var ids = tenders.Select(t => t.TenderId);
            var summaries = await db.EvaluationReports.AsNoTracking().Where(r => ids.Contains(r.TenderId))
                .OrderBy(r => r.TenderId).ToListAsync(ct);
            var scores = await db.EvaluationScores.AsNoTracking().Where(s => ids.Contains(s.TenderId) &&
                (!evaluator || officer || manager || top || s.Evaluation.Status == "Approved" ||
                 s.Evaluation.CommitteeAssignment.UserId == actor.Id))
                .Select(s => new { s.TenderId, s.Evaluation.BidId, s.EvaluationCriterion.Name, s.EvaluationCriterion.Category, s.Score, s.Notes })
                .OrderBy(s => s.TenderId).ThenBy(s => s.BidId).ToListAsync(ct);
            data = data with { Columns = ["Tender", "Bid", "Category", "Criterion or stage", "Score or status", "Notes"] };
            data.Rows.AddRange(summaries.Select(r => new[] { r.TenderId.ToString(), r.RecommendedBidId?.ToString() ?? "",
                "Recommendation", "Combined report", r.Status, r.CommitteeSummary ?? "" }));
            data.Rows.AddRange(scores.Select(s => new[] { s.TenderId.ToString(), s.BidId.ToString(), s.Category,
                s.Name, s.Score.ToString("N2"), s.Notes ?? "" }));
        }
        else if (report == "decisions")
        {
            var ids = tenders.Select(t => t.TenderId);
            var rows = await db.TenderApprovals.AsNoTracking().Where(a => ids.Contains(a.TenderId))
                .OrderBy(a => a.DecidedAt).Select(a => new { a.Tender.Title, a.Stage, a.Decision, a.User.FullName, a.DecidedAt, a.Notes }).ToListAsync(ct);
            var awards = await tenders.Where(t => t.Status == "Awarded")
                .Select(t => new { t.Title, t.AwardedBidId, t.AwardedAt, t.AwardNotes }).ToListAsync(ct);
            data = data with { Columns = ["Tender", "Stage", "Decision", "Recorded by", "Date UTC", "Notes"] };
            data.Rows.AddRange(rows.Select(a => new[] { a.Title, a.Stage, a.Decision, a.FullName, Date(a.DecidedAt), a.Notes ?? "" }));
            data.Rows.AddRange(awards.Select(a => new[] { a.Title, "Award", $"Bid {a.AwardedBidId}", "", Date(a.AwardedAt), a.AwardNotes ?? "" }));
        }
        else
        {
            data = data with { Columns = ["Statistic", "Count"] };
            data.Rows.Add(["Registered suppliers", (await db.SupplierProfiles.CountAsync(ct)).ToString()]);
            data.Rows.Add(["Tenders", (await db.Tenders.CountAsync(ct)).ToString()]);
            data.Rows.Add(["Awarded tenders", (await db.Tenders.CountAsync(t => t.Status == "Awarded", ct)).ToString()]);
            var frequencies = await db.SupplierProfiles.AsNoTracking().OrderBy(s => s.CompanyName)
                .Select(s => new { s.CompanyName, Awards = db.Tenders.Count(t => t.Status == "Awarded" &&
                    t.Bid != null && t.Bid.Invitation.SupplierProfileId == s.SupplierProfileId) }).ToListAsync(ct);
            data.Rows.AddRange(frequencies.Select(s => new[] { $"Awards to {s.CompanyName}", s.Awards.ToString() }));
        }

        if (!string.IsNullOrWhiteSpace(search))
            data.Rows.RemoveAll(row => !row.Any(value => value.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)));
        ViewBag.Choices = choices;
        ViewBag.Selected = report;
        ViewBag.Search = search;
        if (format == "xlsx") return File(Excel(data), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{report}.xlsx");
        if (format == "pdf") return File(Pdf(data), "application/pdf", $"{report}.pdf");
        return View(data);
    }

    private static string Date(DateTime? value) => value?.ToString("yyyy-MM-dd HH:mm") ?? "";

    private static byte[] Excel(DownloadableReport data)
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("Report");
        sheet.Cell(1, 1).Value = data.Title;
        sheet.Cell(2, 1).Value = $"Generated UTC {Date(DateTime.UtcNow)}";
        for (var column = 0; column < data.Columns.Length; column++) sheet.Cell(4, column + 1).Value = data.Columns[column];
        for (var row = 0; row < data.Rows.Count; row++)
            for (var column = 0; column < data.Columns.Length; column++)
                sheet.Cell(row + 5, column + 1).Value = data.Rows[row][column];
        sheet.Row(4).Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(4);
        sheet.Columns(1, data.Columns.Length).Width = 25;
        sheet.Columns(1, data.Columns.Length).Style.Alignment.WrapText = true;
        if (data.Rows.Count == 0) sheet.Cell(5, 1).Value = "No matching records.";
        using var output = new MemoryStream();
        book.SaveAs(output);
        return output.ToArray();
    }

    private static byte[] Pdf(DownloadableReport data)
    {
        SourcingTenderManagement.Services.TopCommittee.TopCommitteeReportExport.EnsureReportFont();
        var document = new Document();
        document.Info.Title = data.Title;
        document.Styles["Normal"]!.Font.Name = "CommitteeReport";
        document.Styles["Normal"]!.Font.Size = 9;
        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.Orientation = Orientation.Landscape;
        var title = section.AddParagraph(data.Title);
        title.Format.Font.Size = 18;
        section.AddParagraph($"Generated UTC {Date(DateTime.UtcNow)}").Format.SpaceAfter = 12;
        if (data.Rows.Count == 0) section.AddParagraph("No matching records.");
        else
        {
            var table = section.AddTable();
            table.Borders.Width = 0.5;
            foreach (var _ in data.Columns) table.AddColumn(Unit.FromCentimeter(23 / (double)data.Columns.Length));
            var header = table.AddRow();
            header.HeadingFormat = true;
            header.Format.Font.Bold = true;
            header.Shading.Color = Colors.LightGray;
            for (var i = 0; i < data.Columns.Length; i++) header.Cells[i].AddParagraph(data.Columns[i]);
            foreach (var values in data.Rows)
            {
                var row = table.AddRow();
                for (var i = 0; i < values.Length; i++) row.Cells[i].AddParagraph(values[i]);
            }
        }
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var output = new MemoryStream();
        renderer.PdfDocument.Save(output, false);
        return output.ToArray();
    }
}
