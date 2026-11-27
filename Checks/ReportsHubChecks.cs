using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SourcingTenderManagement.Controllers;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Models.Reports;
using SourcingTenderManagement.Services.Reports.Hub;

namespace SourcingTenderManagement.Checks;
// Self-test, not part of the website. Tests reports hub. Not run from Program.cs: a temporary test host gives it an in-memory SQLite database (see REPORTS-INTEGRATION-NOTES.md).

public static class ReportsHubChecks
{
    public static async Task RunAsync(ApplicationDbContext db)
    {
        if (db.Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite" ||
            db.Database.GetDbConnection().ConnectionString != "Data Source=:memory:")
            throw new InvalidOperationException("Reports hub checks require a SQLite in-memory database.");
        var checks = 0;
        void Require(bool condition, string label)
        {
            if (!condition) throw new Exception("Reports hub check failed: " + label);
            checks++;
        }

        var report = new FixtureReport();
        var normalized = ReportQueryValidation.Normalize(report,
            new ReportQuery(new DateOnly(2026, 12, 1), new DateOnly(2026, 1, 1), DepartmentId: -1,
                Status: "not-a-status", Search: "  search  ", Drill: "supplier:bad", Page: -1, ForExport: true));
        Require(normalized.Query is { From: null, To: null, DepartmentId: null, Status: null, Search: "search", Drill: null, Page: 1, ForExport: false },
            "invalid filters are cleared and export is disabled");
        Require(normalized.Messages.Count > 0, "invalid filters produce inline messages");
        Require(ReportQueryValidation.Normalize(report, new ReportQuery(new DateOnly(2020, 1, 1), new DateOnly(2026, 1, 1))).Query.From == null,
            "ranges over five years are rejected");
        Require(ReportQueryValidation.Normalize(report, new ReportQuery(new DateOnly(9999, 1, 1), new DateOnly(9999, 12, 31))).Query.From != null,
            "upper DateOnly boundary does not overflow");
        Require(ReportQueryValidation.Normalize(report, new ReportQuery(Search: new string('a', 201))).Query.Search!.Length == 200,
            "search length bounded");
        var limited = new Services.Reports.Data.BidsEvaluationReport(db);
        Require(ReportQueryValidation.Normalize(limited, new ReportQuery(DepartmentId: 1, Search: "ignored")).Query is
            { DepartmentId: null, Search: null }, "filters unsupported by Person B report are ignored");

        var department = new Department { Name = "Checks department", IsActive = true };
        var otherDepartment = new Department { Name = "Other department", IsActive = true };
        db.Departments.AddRange(department, otherDepartment);
        await db.SaveChangesAsync();
        foreach (var role in RoleNames.Primary)
        {
            db.Roles.Add(new IdentityRole { Id = "hub-role-" + role, Name = role, NormalizedName = role.ToUpperInvariant() });
            db.Users.Add(new ApplicationUser { Id = "hub-" + role, FullName = "Checks " + role, DepartmentId = department.DepartmentId });
            db.UserRoles.Add(new IdentityUserRole<string> { UserId = "hub-" + role, RoleId = "hub-role-" + role });
        }
        await db.SaveChangesAsync();
        var officerId = "hub-" + RoleNames.SourcingOfficer;
        var ownRequest = new PurchaseRequest { DepartmentId = department.DepartmentId, RequestedByUserId = officerId,
            ReviewedByUserId = officerId, Title = "Owned request", Reason = "Checks", Priority = "Normal", Status = "ConvertedToTender" };
        var otherRequest = new PurchaseRequest { DepartmentId = otherDepartment.DepartmentId, RequestedByUserId = "hub-Admin",
            Title = "Other request", Reason = "Checks", Priority = "Normal", Status = "ConvertedToTender" };
        db.PurchaseRequests.AddRange(ownRequest, otherRequest);
        await db.SaveChangesAsync();
        var ownTender = new Tender { PurchaseRequestId = ownRequest.PurchaseRequestId, CreatedByUserId = officerId,
            Title = "Owned tender", Description = "Checks", Status = "Published" };
        var otherTender = new Tender { PurchaseRequestId = otherRequest.PurchaseRequestId, CreatedByUserId = "hub-Admin",
            Title = "Private tender", Description = "Checks", Status = "Published" };
        db.Tenders.AddRange(ownTender, otherTender);
        await db.SaveChangesAsync();
        var supplier = new SupplierProfile { UserId = "hub-Supplier", CompanyName = "Checks supplier", ContactName = "Checks",
            RegistrationNumber = "HUB-CHECKS", Status = "Approved" };
        db.SupplierProfiles.Add(supplier);
        await db.SaveChangesAsync();
        db.Invitations.Add(new Invitation { TenderId = ownTender.TenderId, SupplierProfileId = supplier.SupplierProfileId,
            InvitedByUserId = officerId, Status = "Pending" });
        await db.SaveChangesAsync();

        var registry = new ReportRegistry([report], db);
        var options = new ReportFilterOptions(db);
        ReportsController Controller(string role, string? userId = null, string? claimRole = null) => new(db, registry, options, new ReportExport(),
            NullLogger<ReportsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim(ClaimTypes.NameIdentifier, userId ?? "hub-" + role),
                    new Claim(ClaimTypes.Role, claimRole ?? role)], "Checks")) }
            }
        };

        Require(await Controller(RoleNames.Supplier).Index(default) is StatusCodeResult { StatusCode: 403 }, "Supplier hub denied");
        Require(await Controller(RoleNames.Admin).Show("missing", new ReportQuery(), default) is NotFoundResult, "unknown report is 404");
        Require(await Controller(RoleNames.Department).Show(report.Key, new ReportQuery(), default) is StatusCodeResult { StatusCode: 403 },
            "denied report is 403");
        Require(await Controller(RoleNames.SourcingOfficer, claimRole: RoleNames.Admin).Index(default) is StatusCodeResult { StatusCode: 403 },
            "cookie claim cannot invent an assigned role");
        var officer = Controller(RoleNames.SourcingOfficer);
        officer.Request.QueryString = new QueryString("?role=Admin&userId=hub-Admin&departmentId=999&forExport=true");
        var result = await officer.Show(report.Key, new ReportQuery(TenderId: otherTender.TenderId, Page: 900, ForExport: true), default);
        var page = (ReportPageModel)((ViewResult)result).Model!;
        Require(page.Scope is { Role: RoleNames.SourcingOfficer, IsAdmin: false } && page.Scope.UserId == officerId &&
            page.Scope.DepartmentId == department.DepartmentId, "scope comes from actual Identity account");
        Require(page.Query is { Page: 3, ForExport: false, TenderId: null }, "page clamped and unavailable tender cleared");
        Require(page.Choices.Tenders.Count == 1
            && page.Choices.Tenders[0].Id == ownTender.TenderId, "tender choices are scoped");
        Require(page.Choices.Suppliers.Count == 1
            && page.Choices.Suppliers[0].Id == supplier.SupplierProfileId, "supplier choices are scoped");
        Require(page.Choices.Departments.Count == 1
            && page.Choices.Departments[0].Id == department.DepartmentId, "department choices are scoped");
        Require(page.Messages.Count > 0, "unavailable record filter explained");
        Require(page.Result.Table.Rows.Count == 1
            && report.LastScope!.UserId == officerId, "page size and execution scope preserved");

        var filtered = page with { Query = new ReportQuery(new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1),
            department.DepartmentId, supplier.SupplierProfileId, ownTender.TenderId, "Approved", "a & b", "status:Approved", 2) };
        var link = filtered.Link(drill: "department:" + department.DepartmentId);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri("http://checks" + link).Query);
        Require(query["search"] == "a & b" && query["status"] == "Approved" && query["page"] == "1" &&
            query["from"] == "2026-01-01" && query["to"] == "2026-02-01", "drill links preserve filters and reset page");
        Require(query["departmentId"] == department.DepartmentId.ToString() && query["supplierId"] == supplier.SupplierProfileId.ToString() &&
            query["tenderId"] == ownTender.TenderId.ToString(), "drill links preserve record filters");
        Require(!filtered.Link(clearDrill: true).Contains("drill=") && filtered.Link(page: 3).Contains("drill=status%3AApproved"),
            "removable drill and pagination links");

        db.ReportAccessRules.Add(new ReportAccessRule { ReportKey = report.Key, Role = RoleNames.Department, Allowed = true, UpdatedByUserId = "hub-Admin" });
        await db.SaveChangesAsync();
        Require(await Controller(RoleNames.Department).Show(report.Key, new ReportQuery(), default) is ViewResult, "stored Allow opens report");
        db.ReportAccessRules.Add(new ReportAccessRule { ReportKey = report.Key,
            Role = RoleNames.SourcingOfficer,
            Allowed = false,
            UpdatedByUserId = "hub-Admin" });
        await db.SaveChangesAsync();
        Require(await officer.Show(report.Key, new ReportQuery(), default) is StatusCodeResult { StatusCode: 403 }, "stored Deny blocks report");
        var hub = (ReportsHubPage)((ViewResult)await officer.Index(default)).Model!;
        Require(hub.Reports.Count == 0, "hub hides denied reports");
        Require(((ReportsHubPage)((ViewResult)await Controller(RoleNames.Admin).Index(default)).Model!).Reports.Count == 1,
            "Admin hub contains all registered reports");
        Console.WriteLine($"Reports hub checks passed: {checks}. SQLite in-memory only.");
    }

    private sealed class FixtureReport : IReport
    {
        public ReportScope? LastScope { get; private set; }
        public string Key => "checks-hub";
        public string Title => "Checks report";
        public string Description => "Isolated report fixture";
        public string Icon => "file";
        public IReadOnlyList<string> DefaultRoles => [RoleNames.SourcingOfficer];
        public IReadOnlyList<string> Filters => ["date", "department", "supplier", "tender", "status", "search"];
        public IReadOnlyList<string> StatusOptions => ["Approved", "Pending"];
        public Task<ReportResult> RunAsync(ReportQuery query, ReportScope scope, CancellationToken ct)
        {
            LastScope = scope;
            var rows = Enumerable.Range(1, 51).Skip((query.Page - 1) * 25).Take(25)
                .Select(value => new ReportRow([value.ToString(), "Checks row"], "/Reports/checks-hub")).ToList();
            return Task.FromResult(new ReportResult([new("Total", "51", "status:Approved")],
                new("Checks", "bar", [new("Approved", 51, "status:Approved")]), new(["Number", "Record"], rows, 51), []));
        }
    }
}
