using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Services;
using SourcingTenderManagement.Services.Reports.Hub;
using PdfDocument = UglyToad.PdfPig.PdfDocument;

namespace SourcingTenderManagement.Checks;
// Self-test, not part of the website.

public static class PurchaseRequestsReportChecks
{
    public static async Task RunAsync(ApplicationDbContext db, string outputDirectory)
    {
        if (db.Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite" ||
            db.Database.GetDbConnection().ConnectionString != "Data Source=:memory:")
            throw new InvalidOperationException("Purchase request checks require SQLite in-memory only.");
        var checks = 0;
        void Require(bool condition, string label)
        {
            if (!condition) throw new Exception("Purchase request report check failed: " + label);
            checks++;
        }
        var ownDepartment = await db.Departments.SingleAsync(department => department.DepartmentId ==
            db.Users.Where(user => user.Id == "hub-Department").Select(user => user.DepartmentId).Single());
        var otherDepartment = new Department { Name = "PRCHECK private department", IsActive = true };
        var emptyDepartment = new Department { Name = "PRCHECK empty department", IsActive = true };
        db.Departments.AddRange(otherDepartment, emptyDepartment);
        db.Users.Add(new ApplicationUser { Id = "prcheck-other-officer", FullName = "Other officer" });
        await db.SaveChangesAsync();
        var submitted = new DateTime(2026, 1, 10, 10, 0, 0, DateTimeKind.Utc);
        var ownRequests = Enumerable.Range(1, 32).Select(index => new PurchaseRequest
        {
            DepartmentId = ownDepartment.DepartmentId, RequestedByUserId = "hub-Department",
            ReviewedByUserId = "hub-SourcingOfficer", Title = "PRCHECK owned " + index.ToString("D2", CultureInfo.InvariantCulture),
            Reason = "Synthetic isolated checks", Priority = "Normal", Status = "Submitted", SubmittedAt = submitted,
            CreatedAt = submitted, TargetDate = new DateOnly(2026, 2, 1)
        }).ToList();
        ownRequests[0].Status = "Approved"; ownRequests[0].ReviewedAt = submitted.AddDays(2);
        ownRequests[1].Status = "Returned"; ownRequests[1].ReviewedAt = submitted.AddDays(4);
        ownRequests[2].Status = "Rejected"; ownRequests[2].ReviewedAt = submitted.AddDays(6);
        ownRequests[3].Status = "ConvertedToTender"; ownRequests[3].ReviewedAt = submitted.AddDays(8);
        ownRequests[4].Status = "Approved"; // Missing ReviewedAt must not contribute to the average.
        ownRequests[5].Status = "UnderReview"; ownRequests[5].ReviewedAt = submitted.AddDays(100);
        ownRequests[6].ReviewedAt = submitted.AddDays(100); ownRequests[6].ReviewedByUserId = null;
        ownRequests[7].Status = "Approved"; ownRequests[7].SubmittedAt = null; ownRequests[7].ReviewedAt = submitted.AddDays(100);
        ownRequests[8].Status = "Approved"; ownRequests[8].ReviewedAt = submitted.AddDays(-1);
        ownRequests[9].ReviewedByUserId = "prcheck-other-officer";
        var privateRequests = new[]
        {
            new PurchaseRequest { DepartmentId = otherDepartment.DepartmentId, RequestedByUserId = "hub-Admin",
                ReviewedByUserId = "prcheck-other-officer", Status = "Approved", Title = "PRCHECK PRIVATE-ONLY",
                Reason = "Checks", Priority = "High", SubmittedAt = submitted, ReviewedAt = submitted.AddDays(10), CreatedAt = submitted },
            new PurchaseRequest { DepartmentId = otherDepartment.DepartmentId, RequestedByUserId = "hub-Admin",
                Status = "Submitted", Title = "PRCHECK shared queue", Reason = "Checks", Priority = "High", SubmittedAt = submitted, CreatedAt = submitted },
            new PurchaseRequest { DepartmentId = otherDepartment.DepartmentId, RequestedByUserId = "hub-Admin",
                ReviewedByUserId = "hub-SourcingOfficer", Status = "Returned", Title = "PRCHECK reviewed by officer",
                Reason = "Checks", Priority = "High", SubmittedAt = submitted, ReviewedAt = submitted.AddDays(3), CreatedAt = submitted }
        };
        db.PurchaseRequests.AddRange(ownRequests);
        db.PurchaseRequests.AddRange(privateRequests);
        db.PurchaseRequests.Add(new PurchaseRequest { DepartmentId = ownDepartment.DepartmentId, RequestedByUserId = "hub-Department",
            ReviewedByUserId = "hub-SourcingOfficer", Status = "Draft", Title = "PRCHECK LEGACY-DRAFT",
            Reason = "Checks", Priority = "Normal", CreatedAt = submitted });
        await db.SaveChangesAsync();
        var report = new PurchaseRequestsReport(db);
        var departmentScope = new ReportScope("hub-Department", RoleNames.Department, ownDepartment.DepartmentId, false);
        var officerScope = new ReportScope("hub-SourcingOfficer", RoleNames.SourcingOfficer, ownDepartment.DepartmentId, false);
        var adminScope = new ReportScope("hub-Admin", RoleNames.Admin, null, true);
        var query = new ReportQuery(Search: "PRCHECK");
        var department = await report.RunAsync(query, departmentScope, default);
        string Kpi(ReportResult result, string label) => result.Kpis.Single(kpi => kpi.Label == label).Value;
        Require(department.Table.Total == 32 && Kpi(department, "Total purchase requests") == "32", "own department total");
        foreach (var (status, count) in new[] { ("Submitted",24), ("UnderReview",1), ("Approved",4), ("Returned",1), ("Rejected",1), ("ConvertedToTender",1) })
            Require(Kpi(department, status) == count.ToString(CultureInfo.InvariantCulture), "status count " + status);
        Require(Kpi(department, "Average review time") == "5.0 days", "average excludes missing, pending and negative dates");
        Require(Kpi(department, "Returned request rate") == "3.1%", "returned rate denominator");
        Require(department.Kpis.Any(kpi => kpi.Label == "Average review · " + ownDepartment.Name && kpi.Value == "5.0 days"), "department review average");
        Require(department.Chart is { Title: "Purchase Requests by Department", Kind: "bar" } && department.Chart.Points.Count == 1 &&
            department.Chart.Points[0].Value == 32 && department.Chart.Points[0].Drill == "department:" + ownDepartment.DepartmentId, "scoped department chart");
        Require(department.Table.Rows.Count == 25 && department.Table.Rows.All(row => row.Link!.StartsWith("/Department/Details/")), "25 rows and authorized links");
        Require((await report.RunAsync(query with { Page = 2 }, departmentScope, default)).Table.Rows.Count == 7, "second page");
        var statusDrill = await report.RunAsync(query with { Drill = "status:Returned" }, departmentScope, default);
        Require(statusDrill.Table.Total == 1 && statusDrill.Table.Rows[0].Cells[5] == "Returned" && Kpi(statusDrill, "Total purchase requests") == "32", "status drill narrows table only");
        Require((await report.RunAsync(query with { Drill = "department:" + otherDepartment.DepartmentId }, departmentScope, default)).Table.Total == 0,
            "cross-department drill cannot escape scope");
        Require((await report.RunAsync(query with { DepartmentId = otherDepartment.DepartmentId }, departmentScope, default)).Table.Total == 0,
            "cross-department filter cannot escape scope");
        var officer = await report.RunAsync(query with { ForExport = true }, officerScope, default);
        var visibleIds = await db.PurchaseRequests.ReviewableBy(officerScope.UserId).Where(request => request.Title.StartsWith("PRCHECK"))
            .Select(request => "#" + request.PurchaseRequestId).ToListAsync();
        Require(officer.Table.Total == visibleIds.Count && officer.Table.Rows.Select(row => row.Cells[0]).ToHashSet().SetEquals(visibleIds), "Officer matches existing ReviewableBy exactly");
        Require(officer.Table.Rows.All(row => row.Link!.StartsWith("/SourcingOfficer/Details/")), "officer details links");
        var admin = await report.RunAsync(query, adminScope, default);
        var manager = await report.RunAsync(query, new ReportScope("hub-SourcingManager", RoleNames.SourcingManager, null, false), default);
        Require(admin.Table.Total == 35 && manager.Table.Total == 35 && admin.Table.Rows.All(row => row.Link == null) && manager.Table.Rows.All(row => row.Link == null),
            "Admin and Manager see all saved requests without unauthorized details links");
        Require((await report.RunAsync(query with { Drill = "department:" + otherDepartment.DepartmentId }, adminScope, default)).Table.Total == 3, "Admin department drill");
        Require((await report.RunAsync(query with { Status = "Approved", Drill = "status:Returned" }, departmentScope, default)).Table.Total == 0, "status filter and drill intersect");
        Require((await report.RunAsync(query with { From = new DateOnly(2026, 1, 10), To = new DateOnly(2026, 1, 10), Status = "Approved",
            DepartmentId = ownDepartment.DepartmentId }, adminScope, default)).Table.Total == 3, "date, department, status and search combine");
        Require((await report.RunAsync(query with { From = new DateOnly(2026, 1, 11) }, departmentScope, default)).Table.Total == 0, "date filters use SubmittedAt");
        var draft = await report.RunAsync(query with { Status = "Draft" }, adminScope, default);
        Require(draft.Table.Total == 0 && draft.Notes.Contains("Drafts are not saved in this system."), "Draft is informational only, including legacy rows");
        var empty = await report.RunAsync(query, departmentScope with { DepartmentId = emptyDepartment.DepartmentId }, default);
        Require(empty.Table.Total == 0 && Kpi(empty, "Returned request rate") == "0.0%" && Kpi(empty, "Average review time") == "—", "zero denominator and empty average");
        Require((await report.RunAsync(query, departmentScope with { DepartmentId = null }, default)).Table.Total == 0, "missing department fails closed");
        Require((await report.RunAsync(query, departmentScope with { Role = RoleNames.Supplier }, default)).Table.Total == 0, "Supplier has no data scope");
        var invalid = await report.RunAsync(query with { Page = -1, Status = "invalid", Drill = "department:bad", From = new DateOnly(2027, 1, 1), To = new DateOnly(2026, 1, 1) }, departmentScope, default);
        Require(invalid.Table.Total == 32 && invalid.Table.Rows.Count == 25 && invalid.Notes.Count > department.Notes.Count, "invalid input is normalized without exceptions");
        Require(ReportQueryValidation.Normalize(report, query with { Drill = "status:invalid" }).Messages.Count > 0, "unknown status drill explained");
        var choices = await new ReportFilterOptions(db).LoadAsync(report, departmentScope, default);
        Require(choices.Departments.Count == 1 && choices.Departments[0].Id == ownDepartment.DepartmentId, "department options cannot leak other departments");
        var managerChoices = await new ReportFilterOptions(db).LoadAsync(report, managerScope(), default);
        Require(managerChoices.Departments.Any(option => option.Id == otherDepartment.DepartmentId), "Manager can filter requests without tenders");
        var exporter = new ReportExport();
        var exported = await report.RunAsync(query with { ForExport = true, Page = 99 }, departmentScope, default);
        Require(exported.Table.Rows.Count == 32 && exported.Table.Rows.All(row => row.Cells[2] == ownDepartment.Name), "export ignores pagination and preserves department scope");
        Directory.CreateDirectory(outputDirectory);
        foreach (var (scope, name) in new[] { (departmentScope, "department"), (officerScope, "officer") })
        {
            var result = await report.RunAsync(query with { ForExport = true }, scope, default);
            var xlsx = exporter.Excel(report, query, result, submitted);
            using (var workbook = new XLWorkbook(new MemoryStream(xlsx)))
            {
                var data = workbook.Worksheet("Data");
                Require(data.LastRowUsed()!.RowNumber() == result.Table.Total + 1 && data.Cell(1, 1).GetString() == "Request Number" &&
                    !data.CellsUsed().Any(cell => cell.GetString().Contains("PRIVATE-ONLY")), name + " Excel headers, all scoped rows and no private record");
            }
            var pdf = exporter.Pdf(report, query, result, submitted);
            using (var document = PdfDocument.Open(pdf))
            {
                var text = string.Join(" ", document.GetPages().Select(page => page.Text));
                Require(System.Text.Encoding.ASCII.GetString(pdf, 0, 4) == "%PDF" && text.Contains("PRCHECK") && !text.Contains("PRIVATE-ONLY"), name + " PDF respects scope");
            }
            await File.WriteAllBytesAsync(Path.Combine(outputDirectory, "purchase-requests-" + name + ".xlsx"), xlsx);
            await File.WriteAllBytesAsync(Path.Combine(outputDirectory, "purchase-requests-" + name + ".pdf"), pdf);
        }
        Console.WriteLine($"Purchase request report checks passed: {checks}. SQLite in-memory only.");
        ReportScope managerScope() => new("hub-SourcingManager", RoleNames.SourcingManager, null, false);
    }
}
