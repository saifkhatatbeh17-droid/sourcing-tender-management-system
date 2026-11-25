using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SourcingTenderManagement.Controllers;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Models.Reports;
using SourcingTenderManagement.Services.Reports.Hub;

namespace SourcingTenderManagement.Checks;

public static class ReportsAccessChecks
{
    // Supply an isolated, open SQLite in-memory context. Shared databases are explicitly refused.
    public static async Task RunAsync(ApplicationDbContext db)
    {
        if (db.Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite" ||
            db.Database.GetDbConnection().ConnectionString != "Data Source=:memory:")
            throw new InvalidOperationException("Reports access checks require a SQLite in-memory database.");

        var checks = 0;
        void Require(bool condition, string label)
        {
            if (!condition) throw new Exception("Reports access check failed: " + label);
            checks++;
        }

        var standard = new CheckReport("standard", [RoleNames.SourcingOfficer, RoleNames.SourcingManager]);
        var restricted = new CheckReport("restricted", []);
        var supplierDefault = new CheckReport("supplier-default", [RoleNames.Supplier]);
        var committee = new CheckReport("top-committee", [RoleNames.TopCommittee]);
        var audit = new CheckReport("audit-log", []);
        var registry = new ReportRegistry([standard, restricted, supplierDefault, committee, audit], db);
        Require(registry.Find("STANDARD") == standard && registry.Find("missing") == null, "key lookup");
        foreach (var role in RoleNames.Primary)
        {
            Require(await registry.CanAccessAsync("standard", role) ==
                (role is RoleNames.Admin or RoleNames.SourcingOfficer or RoleNames.SourcingManager), "defaults for " + role);
        }
        Require(!await registry.CanAccessAsync("missing", RoleNames.Admin), "unknown key denied to Admin");
        Require(!await registry.CanAccessAsync("restricted", "UnknownRole"), "unknown role denied");
        Require(!await registry.CanAccessAsync("supplier-default", RoleNames.Supplier), "supplier denied despite default");

        var controller = new ReportsController(db, registry, new ReportFilterOptions(db), new ReportExport(), NullLogger<ReportsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([
                        new Claim(ClaimTypes.NameIdentifier, "checks-admin"),
                        new Claim(ClaimTypes.Role, RoleNames.Admin)], "Checks"))
                }
            }
        };
        ReportAccessChange Change(string key, string role, bool? allowed) =>
            new() { ReportKey = key, Role = role, Allowed = allowed };

        foreach (var role in RoleNames.Primary.Where(role => role != RoleNames.TopCommittee))
            Require(await controller.SaveAccess([Change(committee.Key, role, true)], default) is BadRequestObjectResult,
                "unsupported committee permission rejected for " + role);
        Require(await controller.SaveAccess([Change(audit.Key, RoleNames.SourcingOfficer, true)], default) is BadRequestObjectResult,
            "audit report remains Admin-only");
        Require(await controller.SaveAccess([Change(committee.Key, RoleNames.TopCommittee, true)], default) is OkObjectResult,
            "supported committee permission accepted");
        db.ReportAccessRules.Add(new ReportAccessRule { ReportKey = committee.Key, Role = RoleNames.SourcingOfficer,
            Allowed = true, UpdatedByUserId = "checks-admin" });
        await db.SaveChangesAsync();
        Require(!await registry.CanAccessAsync(committee.Key, RoleNames.SourcingOfficer), "old incompatible Allow fails closed");
        Require(await registry.CanAccessAsync(committee.Key, RoleNames.TopCommittee), "supported committee role remains allowed");
        Require(await registry.CanAccessAsync(committee.Key, RoleNames.Admin), "Admin remains allowed on fixed report");
        var committeePermissions = (await registry.GetPermissionsAsync()).Single(entry => entry.Key == committee.Key).Permissions;
        Require(committeePermissions.Single(permission => permission.Role == RoleNames.SourcingOfficer) is
            { Allowed: false, CanEdit: false }, "matrix disables unsupported grant even with stored Allow");
        Require(committeePermissions.Single(permission => permission.Role == RoleNames.TopCommittee).CanEdit,
            "matrix keeps supported committee role editable");

        Require(await controller.SaveAccess([
            Change("RESTRICTED", RoleNames.Department, true),
            Change("standard", RoleNames.SourcingOfficer, false)], default) is OkObjectResult, "save valid batch");
        Require(await registry.CanAccessAsync("restricted", RoleNames.Department), "stored Allow overrides default Deny");
        Require(!await registry.CanAccessAsync("standard", RoleNames.SourcingOfficer), "stored Deny overrides default Allow");
        Require(await registry.CanAccessAsync("standard", RoleNames.SourcingManager), "unmodified role keeps default");
        var saved = await db.ReportAccessRules.SingleAsync(rule => rule.ReportKey == "restricted");
        Require(saved.UpdatedByUserId == "checks-admin" && saved.UpdatedAt.Kind == DateTimeKind.Utc,
            "server-side updater and UTC time");
        Require(await controller.SaveAccess([Change("restricted", RoleNames.Department, false)], default) is OkObjectResult,
            "repeat save updates existing rule");
        Require(await db.ReportAccessRules.CountAsync(rule => rule.ReportKey == "restricted" && rule.Role == RoleNames.Department) == 1,
            "repeat saves do not duplicate");
        Require(!await registry.CanAccessAsync("restricted", RoleNames.Department), "next check sees changed permission");

        var permissions = await registry.GetPermissionsAsync();
        var standardPermissions = permissions.Single(report => report.Key == "standard").Permissions;
        Require(standardPermissions.Single(permission => permission.Role == RoleNames.SourcingOfficer) is
            { Allowed: false, IsDefault: false, CanEdit: true }, "stored permission distinguished from default");
        Require(standardPermissions.Single(permission => permission.Role == RoleNames.SourcingManager) is
            { Allowed: true, IsDefault: true, CanEdit: true }, "default permission identified");
        Require(standardPermissions.Single(permission => permission.Role == RoleNames.Admin) is
            { Allowed: true, CanEdit: false }, "Admin is fixed in management grid");
        Require(standardPermissions.Single(permission => permission.Role == RoleNames.Supplier) is
            { Allowed: false, CanEdit: false }, "Supplier is fixed in management grid");
        Require(await controller.Access(default, data: true) is OkObjectResult, "management JSON GET returns permissions");
        Require(await controller.Access(default) is ViewResult, "management GET renders the access view");

        var originalCount = await db.ReportAccessRules.CountAsync();
        foreach (var invalid in new List<ReportAccessChange>[]
        {
            [Change("standard", RoleNames.Admin, false)],
            [Change("standard", RoleNames.Supplier, true)],
            [Change("standard", "UnknownRole", true)],
            [Change("standard", RoleNames.Department, null)],
            [Change("standard", RoleNames.Department, true), Change("missing", RoleNames.Department, true)],
            [Change("standard", RoleNames.Department, true), Change("STANDARD", RoleNames.Department, false)]
        })
            Require(await controller.SaveAccess(invalid, default) is BadRequestObjectResult, "invalid batch rejected");
        Require(await db.ReportAccessRules.CountAsync() == originalCount, "invalid batches do not partially write");
        var missingAllowed = Change("standard", RoleNames.Department, null);
        Require(!Validator.TryValidateObject(missingAllowed,
            new ValidationContext(missingAllowed), [], true), "Allowed is required");

        db.ReportAccessRules.AddRange(
            new ReportAccessRule { ReportKey = "standard", Role = RoleNames.Admin, Allowed = false, UpdatedByUserId = "checks-admin" },
            new ReportAccessRule { ReportKey = "standard", Role = RoleNames.Supplier, Allowed = true, UpdatedByUserId = "checks-admin" },
            new ReportAccessRule { ReportKey = "missing", Role = RoleNames.SourcingOfficer, Allowed = true, UpdatedByUserId = "checks-admin" });
        await db.SaveChangesAsync();
        Require(await registry.CanAccessAsync("standard", RoleNames.Admin), "stored Deny cannot block Admin");
        Require(!await registry.CanAccessAsync("standard", RoleNames.Supplier), "stored Allow cannot grant Supplier");
        Require(!await registry.CanAccessAsync("missing", RoleNames.SourcingOfficer), "stored unknown key cannot grant access");

        foreach (var forExport in new[] { false, true })
        {
            try
            {
                await registry.RunAsync("standard", new ReportQuery(ForExport: forExport),
                    new ReportScope("checks-officer", RoleNames.SourcingOfficer, null, false));
                throw new Exception("Denied report executed.");
            }
            catch (UnauthorizedAccessException) { checks++; }
        }
        Require(standard.RunCount == 0, "denied open/export never executes report logic");
        await registry.RunAsync("standard", new ReportQuery(ForExport: true),
            new ReportScope("checks-admin", RoleNames.Admin, null, true));
        Require(standard.RunCount == 1, "Admin can execute an export");
        try
        {
            await registry.RunAsync("standard", new ReportQuery(),
                new ReportScope("checks-manager", RoleNames.SourcingManager, null, true));
            throw new Exception("Inconsistent admin scope accepted.");
        }
        catch (UnauthorizedAccessException) { checks++; }

        db.ReportAccessRules.Add(new ReportAccessRule
            { ReportKey = "restricted", Role = RoleNames.Department, Allowed = true, UpdatedByUserId = "checks-admin" });
        try
        {
            await db.SaveChangesAsync();
            throw new Exception("Duplicate report and role accepted.");
        }
        catch (DbUpdateException) { checks++; db.ChangeTracker.Clear(); }

        var entity = db.Model.FindEntityType(typeof(ReportAccessRule))!;
        Require(entity.GetIndexes().Any(index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(["ReportKey", "Role"])), "unique composite index");
        Require(entity.FindProperty("ReportKey")!.GetMaxLength() == 100 &&
            entity.FindProperty("Role")!.GetMaxLength() == 64, "column lengths");
        Require(new[] { nameof(ReportsController.Access), nameof(ReportsController.SaveAccess) }.All(name => typeof(ReportsController).GetMethod(name)!.GetCustomAttribute<AuthorizeAttribute>()?.Roles == RoleNames.Admin),
            "management controller requires Admin");
        Require(typeof(ReportsController).GetMethod(nameof(ReportsController.SaveAccess))!
            .IsDefined(typeof(ValidateAntiForgeryTokenAttribute)), "management POST requires antiforgery");
        Require(typeof(ReportsController).GetMethod(nameof(ReportsController.SaveAccess))!
            .IsDefined(typeof(HttpPostAttribute)), "management writes require POST");
        Console.WriteLine($"Reports access checks passed: {checks}. SQLite in-memory only.");
    }

    private sealed class CheckReport(string key, IReadOnlyList<string> roles) : IReport
    {
        public int RunCount { get; private set; }
        public string Key => key;
        public string Title => key;
        public string Description => key;
        public string Icon => "file";
        public IReadOnlyList<string> DefaultRoles => roles;
        public IReadOnlyList<string> Filters => [];
        public IReadOnlyList<string> StatusOptions => [];
        public Task<ReportResult> RunAsync(ReportQuery query, ReportScope scope, CancellationToken ct)
        {
            RunCount++;
            return Task.FromResult(new ReportResult([], null, new ReportTable([], [], 0), []));
        }
    }
}
