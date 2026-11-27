using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using SourcingTenderManagement.Controllers;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Services.Reports.Hub;

namespace SourcingTenderManagement.Checks;
// Self-test, not part of the website.

public static class ReportsNavigationChecks
{
    public static void CheckRenderedSidebar(string html, string role, bool reportsVisible, bool reportsActive)
    {
        var match = System.Text.RegularExpressions.Regex.Match(html, "<nav id=\"admin-sidebar-menu\"[^>]*>([\\s\\S]*?)</nav>");
        if (!match.Success) throw new Exception("Workspace sidebar was not rendered.");
        var sidebar = match.Value;
        var links = System.Text.RegularExpressions.Regex.Matches(sidebar, "<a\\b[^>]*href=\"/Reports\"[^>]*>");
        if (links.Count != (reportsVisible ? 1 : 0)) throw new Exception("Incorrect number of Reports hub links for " + role);
        if (reportsVisible && links[0].Value.Contains("is-active") != reportsActive) throw new Exception("Incorrect Reports active state for " + role);
        if (sidebar.Contains("href=\"/Reports/Access\"") != (role == RoleNames.Admin)) throw new Exception("Report access link is not Admin-only.");
        if (role == RoleNames.SourcingOfficer && (!sidebar.Contains("Report review") || !sidebar.Contains("href=\"/SourcingOfficer/Reports\"")))
            throw new Exception("Officer workflow link was lost.");
        if (role == RoleNames.EvaluationCommittee && (!sidebar.Contains("Committee report") || !sidebar.Contains("href=\"/EvaluationCommittee/Reports\"")))
            throw new Exception("Committee workflow link was lost.");
    }

    public static void CheckAdminDashboardLinks(string html)
    {
        foreach (var key in new[] { "purchase-requests", "tenders", "suppliers", "bids-evaluation" })
            if (!html.Contains("href=\"/Reports/" + key + "\"")) throw new Exception("Dashboard link missing for " + key);
        foreach (var status in new[] { "Pending", "ChangesRequested", "Approved", "Rejected", "Suspended" })
            if (!html.Contains("href=\"/Reports/suppliers?status=" + status + "\"")) throw new Exception("Supplier chart filter missing for " + status);
    }

    public static async Task RunAsync(ApplicationDbContext db)
    {
        if (db.Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite" || db.Database.GetDbConnection().ConnectionString != "Data Source=:memory:")
            throw new InvalidOperationException("Reports navigation checks require SQLite in-memory only.");
        var checks = 0;
        void Require(bool condition, string label)
        {
            if (!condition) throw new Exception("Reports navigation check failed: " + label);
            checks++;
        }
        var report = new NavigationFixture();
        var registry = new ReportRegistry([report], db);
        ReportNavigation Navigation() => new(registry, NullLogger<ReportNavigation>.Instance);
        Require(await Navigation().HasReportsAsync(RoleNames.Admin), "Admin sees Reports");
        Require(!await Navigation().HasReportsAsync(RoleNames.Supplier), "Supplier never sees Reports");
        Require(await Navigation().HasReportsAsync(RoleNames.SourcingOfficer), "default role sees Reports");
        Require(!await Navigation().HasReportsAsync(RoleNames.Department), "role without reports sees no hub link");
        var cached = Navigation();
        Require(ReferenceEquals(cached.HasReportsAsync(RoleNames.SourcingOfficer), cached.HasReportsAsync(RoleNames.SourcingOfficer)), "one permission lookup per role per request");
        db.ReportAccessRules.AddRange(new ReportAccessRule { ReportKey = report.Key, Role = RoleNames.SourcingOfficer, Allowed = false, UpdatedByUserId = "hub-Admin" },
            new ReportAccessRule { ReportKey = report.Key, Role = RoleNames.Department, Allowed = true, UpdatedByUserId = "hub-Admin" },
            new ReportAccessRule { ReportKey = report.Key, Role = RoleNames.Supplier, Allowed = true, UpdatedByUserId = "hub-Admin" });
        await db.SaveChangesAsync();
        Require(!await Navigation().HasReportsAsync(RoleNames.SourcingOfficer), "stored Deny hides link on next request");
        Require(await Navigation().HasReportsAsync(RoleNames.Department), "stored Allow exposes eligible report");
        Require(!await Navigation().HasReportsAsync(RoleNames.Supplier), "stored Allow never exposes Supplier hub");
        Require(await Navigation().HasReportsAsync(RoleNames.Admin), "Admin remains allowed");
        foreach (var role in RoleNames.Primary)
        {
            var menu = PageNavigation.Menus[role];
            Require(menu.Count(item => item.Controller == "Reports" && item.Action == "Index" && item.Title == "Reports") == (role == RoleNames.Supplier ? 0 : 1),
                "one hub entry for eligible role " + role);
            Require(menu.Count(item => item.Controller == "ReportAccess") == (role == RoleNames.Admin ? 1 : 0), "Access is Admin only " + role);
        }
        Require(PageNavigation.Menus[RoleNames.SourcingOfficer].Any(item => item.Controller == null && item.Action == "Reports" && item.Title == "Report review"),
            "Officer workflow route retained with new label");
        Require(PageNavigation.Menus[RoleNames.EvaluationCommittee].Any(item => item.Controller == null && item.Action == "Reports" && item.Title == "Committee report"),
            "Committee workflow route retained with new label");
        Require(typeof(SourcingOfficerController).GetMethod("Reports") != null && typeof(EvaluationCommitteeController).GetMethod("Reports") != null &&
            typeof(SourcingManagerController).GetMethod("Reports") != null && typeof(TopCommitteeController).GetMethod("Reports") != null,
            "all legacy report actions remain");
        foreach (var (controller, role) in new[] { (typeof(SourcingOfficerController), RoleNames.SourcingOfficer),
            (typeof(EvaluationCommitteeController), RoleNames.EvaluationCommittee) })
            Require(controller.GetCustomAttribute<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()?.Roles == role &&
                controller.GetMethod("Reports")!.GetCustomAttribute<Microsoft.AspNetCore.Mvc.NonActionAttribute>() == null,
                "workflow action remains exposed with its existing role protection " + role);

        // Simulate SQL Server error 208 without connecting to SQL Server or changing any schema.
        var errors = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), nonPublic: true)!;
        var errorConstructor = typeof(SqlError).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .First(constructor => constructor.GetParameters().Length == 8);
        var error = errorConstructor.Invoke([208, (byte)1, (byte)16, "checks", "Invalid object name 'ReportAccessRules'.", "", 1, null]);
        typeof(SqlErrorCollection).GetMethod("Add", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(errors, [error]);
        var create = typeof(SqlException).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .First(method => method.Name == "CreateException" && method.GetParameters().Length == 2);
        var missingTable = (SqlException)create.Invoke(null, [errors, "checks"] )!;
        var interceptor = new MissingTableInterceptor(missingTable);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>((DbContextOptions<ApplicationDbContext>)db.GetService<IDbContextOptions>())
            .AddInterceptors(interceptor).Options;
        await using var failingDb = new ApplicationDbContext(options);
        var failing = new ReportNavigation(new ReportRegistry([report], failingDb), NullLogger<ReportNavigation>.Instance);
        Require(!await failing.HasReportsAsync(RoleNames.SourcingOfficer), "missing SQL access table hides Reports, no default fallback");
        Require(await failing.HasReportsAsync(RoleNames.Admin) && !await failing.HasReportsAsync(RoleNames.Supplier), "missing table does not change Admin or Supplier rules");
        Console.WriteLine($"Reports navigation checks passed: {checks}. SQLite memory and simulated SQL errors only.");
    }

    private sealed class MissingTableInterceptor(SqlException exception) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(System.Data.Common.DbCommand command,
            CommandEventData eventData, InterceptionResult<System.Data.Common.DbDataReader> result, CancellationToken cancellationToken = default) => throw exception;
    }

    private sealed class NavigationFixture : IReport
    {
        public string Key => "checks-navigation";
        public string Title => "Navigation checks";
        public string Description => "Isolated fixture";
        public string Icon => "file";
        public IReadOnlyList<string> DefaultRoles => [RoleNames.SourcingOfficer];
        public IReadOnlyList<string> Filters => [];
        public IReadOnlyList<string> StatusOptions => [];
        public Task<ReportResult> RunAsync(ReportQuery query, ReportScope scope, CancellationToken ct) => Task.FromResult(new ReportResult([], null, new ReportTable([], [], 0), []));
    }
}
