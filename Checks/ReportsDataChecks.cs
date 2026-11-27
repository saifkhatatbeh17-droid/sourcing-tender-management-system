using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.FileProviders;
using SourcingTenderManagement.Models;
using System.Security.Claims;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Controllers;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Services.Reports.Data;
using SourcingTenderManagement.Services.Reports.Hub;
using SourcingTenderManagement.Services.TopCommittee;

namespace SourcingTenderManagement.Checks;
// Self-test, not part of the website. Run it with: dotnet run -- --reports-data-checks

public static class ReportsDataChecks
{
    public static async Task RunAsync(string connection)
    {
        var today = new DateOnly(2026, 11, 22);
        Require(DocumentExpiry.State(today.AddDays(-1), today, 30) == "Expired" &&
            DocumentExpiry.State(today.AddDays(30), today, 30) == "Expiring soon" &&
            DocumentExpiry.State(today.AddDays(31), today, 30) == "Valid" &&
            DocumentExpiry.State(null, today, 30) == "No expiry date", "document expiry boundaries");
        Require(TopCommitteeReports.Frequency(6, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31)) == 2m,
            "dealing frequency over three months");

        var sql = new SqlConnectionStringBuilder(connection) { InitialCatalog = "TiryaqReportsDataQA_" + Guid.NewGuid()
            .ToString("N") };
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext
        {
            User = Actor("qa-admin", RoleNames.Admin)
        }};
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHttpContextAccessor>(accessor);
        services.AddScoped<IAuditLog, AuditLog>();
        services.AddIdentityCore<ApplicationUser>(o => o.Stores.SchemaVersion = Microsoft.AspNetCore.Identity.IdentitySchemaVersions.Version3)
            .AddEntityFrameworkStores<ApplicationDbContext>();
        services.AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(sql.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(accessor)));
        await using var provider = services.BuildServiceProvider();
        await using var dbScope = provider.CreateAsyncScope();
        var db = dbScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        accessor.HttpContext!.RequestServices = dbScope.ServiceProvider;
        var users = dbScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        T Controller<T>(T controller, string user, string role) where T : Controller
        {
            accessor.HttpContext.User = Actor(user, role);
            controller.ControllerContext = new ControllerContext(new ActionContext(accessor.HttpContext,
                new Microsoft.AspNetCore.Routing.RouteData(), new Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor()));
            controller.Url = new Microsoft.AspNetCore.Mvc.Routing.UrlHelper(controller.ControllerContext);
            controller.TempData = new TempDataDictionary(accessor.HttpContext, new MemoryTempData());
            return controller;
        }
        try
        {
            await db.Database.MigrateAsync();
            var department = new Department { Name = "QA procurement", IsActive = true };
            var deleted = new Department { Name = "QA deleted", IsActive = true };
            db.Departments.AddRange(department, deleted);
            await db.SaveChangesAsync();
            department.Name = "QA changed";
            db.Departments.Remove(deleted);
            await db.SaveChangesAsync();
            var officer = new ApplicationUser { Id = "qa-officer", FullName = "QA Officer", IsActive = true, PasswordHash = "fixture-secret" };
            var admin = new ApplicationUser { Id = "qa-admin", FullName = "QA Admin", IsActive = true };
            db.Users.AddRange(officer, admin, new ApplicationUser { Id = "qa-supplier", FullName = "QA Supplier", IsActive = true });
            db.Roles.Add(new Microsoft.AspNetCore.Identity.IdentityRole { Id = "qa-role", Name = RoleNames.Admin, NormalizedName = "ADMIN" });
            await db.SaveChangesAsync();
            db.UserRoles.Add(new Microsoft.AspNetCore.Identity.IdentityUserRole<string> { UserId = admin.Id, RoleId = "qa-role" });
            await db.SaveChangesAsync();

            var request = new PurchaseRequest { DepartmentId = department.DepartmentId, RequestedByUserId = officer.Id,
                Title = "QA request", Reason = "Report test", Priority = "Normal", Status = "ConvertedToTender",
                TargetDate = today, CreatedAt = DateTime.UtcNow };
            db.PurchaseRequests.Add(request);
            var supplier = new SupplierProfile { UserId = "qa-supplier", CompanyName = "QA supply", ContactName = "QA",
                RegistrationNumber = "QA-001", Status = "Approved" };
            db.SupplierProfiles.Add(supplier);
            await db.SaveChangesAsync();
            var tender = new Tender { PurchaseRequestId = request.PurchaseRequestId, CreatedByUserId = officer.Id, Title = "QA tender",
                Description = "QA", Status = "Published", OpensAt = DateTime.UtcNow.AddDays(-3), ClosesAt = DateTime.UtcNow
                    .AddDays(-1),
                CreatedAt = DateTime.UtcNow.AddDays(-3) };
            db.Tenders.Add(tender);
            await db.SaveChangesAsync();
            await Services.TenderClosureService.CloseExpired(db, DateTime.UtcNow);
            await db.Entry(tender).ReloadAsync();
            Require(tender.Status == "Closed" && await db.AuditEntries.AnyAsync(a => a.TenderId == tender.TenderId &&
                a.UserName == "System" && a.Summary.Contains("deadline")), "audit entry for bulk tender closure");
            var reviewRequest = new PurchaseRequest { DepartmentId = department.DepartmentId, RequestedByUserId = officer.Id,
                Title = "QA review", Reason = "Audit check", Priority = "Normal", Status = "Submitted", TargetDate = today };
            db.PurchaseRequests.Add(reviewRequest);
            await db.SaveChangesAsync();
            var officerController = Controller(new SourcingOfficerController(db), officer.Id, RoleNames.SourcingOfficer);
            await officerController.StartReview(reviewRequest.PurchaseRequestId);
            Require(await db.AuditEntries.AnyAsync(a => a.EntityType == "PurchaseRequest" &&
                a.EntityId == reviewRequest.PurchaseRequestId.ToString() && a.Summary.Contains("review started") && a.UserId == officer.Id),
                "audit entry for bulk review start");
            var environment = new TestEnvironment();
            var config = new ConfigurationBuilder().Build();
            var adminController = Controller(new AdminController(db, users, config, environment), admin.Id, RoleNames.Admin);
            Require(await adminController.SetSupplierSuspension(supplier.SupplierProfileId, true, "", default) is BadRequestObjectResult,
                "suspension requires a reason");
            Require(await adminController.SetSupplierSuspension(supplier.SupplierProfileId, true, "QA hold", default) is RedirectToActionResult,
                "supplier suspended");
            tender.Status = "Open";
            tender.ClosesAt = DateTime.UtcNow.AddDays(1);
            await db.SaveChangesAsync();
            officerController = Controller(officerController, officer.Id, RoleNames.SourcingOfficer);
            await officerController.CreateInvitation(tender.TenderId, [supplier.SupplierProfileId]);
            Require(!await db.Invitations.AnyAsync(i => i.TenderId == tender.TenderId), "suspended supplier cannot be invited");
            adminController = Controller(adminController, admin.Id, RoleNames.Admin);
            Require(await adminController.SetSupplierSuspension(supplier.SupplierProfileId, false, null, default) is RedirectToActionResult,
                "supplier reactivated");
            officerController = Controller(officerController, officer.Id, RoleNames.SourcingOfficer);
            await officerController.CreateInvitation(tender.TenderId, [supplier.SupplierProfileId]);
            var invitation = await db.Invitations.SingleAsync(i => i.TenderId == tender.TenderId);
            Require(invitation.Status == "Pending", "reactivated supplier can be invited");
            invitation.Status = "Accepted";
            await db.SaveChangesAsync();
            adminController = Controller(adminController, admin.Id, RoleNames.Admin);
            await adminController.SetSupplierSuspension(supplier.SupplierProfileId, true, "Second QA hold", default);
            var supplierController = Controller(new SupplierController(db, users, environment), "qa-supplier", RoleNames.Supplier);
            Require(await supplierController.SaveOffer(new OfferFormViewModel { InvitationId = invitation.InvitationId }, "Draft", null)
                is ForbidResult, "suspended supplier cannot save or edit offers");
            Require(await supplierController.SaveOffer(new OfferFormViewModel { InvitationId = invitation.InvitationId }, "Submit", null)
                is ForbidResult, "suspended supplier cannot submit offers");
            adminController = Controller(adminController, admin.Id, RoleNames.Admin);
            await adminController.SetSupplierSuspension(supplier.SupplierProfileId, false, null, default);

            var contractController = Controller(new TenderContractsController(db, environment), admin.Id, RoleNames.Admin);
            Require(await contractController.Generate(tender.TenderId, default) is NotFoundResult, "generation requires an award");
            using var invalid = new MemoryStream("not a PDF"u8.ToArray());
            Require(await contractController.UploadSigned(tender.TenderId, new FormFile(invalid, 0, invalid.Length, "signedFile", "fake.pdf"),
                null, default) is BadRequestObjectResult, "non-PDF signed copy rejected");
            Require(await contractController.UploadSigned(tender.TenderId, new FormFile(invalid, 0, 10 * 1024 * 1024 + 1, "signedFile", "large.pdf"),
                null, default) is BadRequestObjectResult, "oversized signed copy rejected");

            db.Bids.Add(new Bid { InvitationId = invitation.InvitationId, TenderId = tender.TenderId,
                Currency = "JOD", DeliveryDays = 3, Status = "NonCompliant", ComplianceStatus = "NonCompliant",
                ComplianceNotes = "Missing required certificate", SubmittedAt = DateTime.UtcNow.AddDays(-1) });
            await db.SaveChangesAsync();

            var audit = new AuditLog(db, accessor);
            await audit.WriteAsync("Account.Import", "ApplicationUser", "", "Test import");
            var events = await db.AuditEntries.AsNoTracking().ToListAsync();
            Require(new[] { "Added", "Modified", "Deleted" }.All(action => events.Any(e =>
                e.Action == action && e.EntityType == "Department" && e.UserId == "qa-admin" && int.Parse(e.EntityId) > 0)),
                "audit identity and generated entity IDs");
            Require(events.All(e => e.ChangesJson?.Contains("fixture-secret") != true &&
                e.ChangesJson?.Contains("PasswordHash") != true), "secret-free audit");

            var adminScope = new ReportScope("qa-admin", RoleNames.Admin, null, true);
            var officerScope = new ReportScope("qa-officer", RoleNames.SourcingOfficer, null, false);
            var bids = new BidsEvaluationReport(db);
            var excluded = await bids.RunAsync(new(Drill: "status:Excluded"), officerScope, default);
            Require(excluded.Table.Total == 1
                && excluded.Table.Rows[0].Cells.Contains("Missing required certificate"), "scoped excluded offers");
            Require((await bids.RunAsync(new(), officerScope with { UserId = "other" }, default)).Table.Total == 0 &&
                (await bids.RunAsync(new(), officerScope with { Role = RoleNames.Department }, default)).Table.Total == 0,
                "bid scope and confidential prices");
            var activity = new SupplierActivityQuery(db);
            var supplierReport = new SuppliersReport(db, activity, new ConfigurationBuilder().Build());
            var drill = await supplierReport.RunAsync(new(Drill: $"supplier:{supplier.SupplierProfileId}"), adminScope, default);
            var legacy = await activity.ReadAsync(db.Tenders.Select(t => t.TenderId), supplier.SupplierProfileId, default);
            Require(drill.Table.Total == legacy[supplier.SupplierProfileId].Count(), "supplier history parity");
            Require((await new ApprovalsReport(db).RunAsync(new(), adminScope, default)).Table.Total == 0, "non-evaluated tender excluded");
            Require((await new AuditLogReport(db).RunAsync(new(), officerScope, default)).Table.Total == 0 &&
                (await new AuditLogReport(db).RunAsync(new(), adminScope, default)).Table.Total == events.Count, "audit report access");
            var top = new TopCommitteeHubReport(new TopCommitteeReports(db, new TopCommitteeAccessService(db)));
            Require((await top.RunAsync(new(Drill: "table:suppliers"), adminScope, default)).Table.Total == 1, "Top Committee supplier statistics");
            accessor.HttpContext.User = Actor(admin.Id, RoleNames.Admin);
            var winner = await db.Bids.SingleAsync();
            winner.ComplianceStatus = "Compliant";
            winner.Status = "Compliant";
            tender.Status = "Awarded";
            tender.AwardedBidId = winner.BidId;
            tender.AwardedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            contractController = Controller(contractController, admin.Id, RoleNames.Admin);
            Require(await contractController.Generate(tender.TenderId, default) is RedirectToActionResult &&
                await db.TenderContracts.AnyAsync(c => c.TenderId == tender.TenderId), "award PDF generated and saved");
            contractController = Controller(contractController, "unrelated-supplier", RoleNames.Supplier);
            Require(await contractController.Download(tender.TenderId, false, default) is NotFoundResult, "unrelated supplier cannot download");
            contractController = Controller(contractController, "qa-supplier", RoleNames.Supplier);
            Require(await contractController.Download(tender.TenderId, false, default) is FileContentResult pdf &&
                pdf.FileContents.AsSpan().StartsWith("%PDF-"u8), "winner can download generated contract");
            Console.WriteLine("PASS: reports data migration, expiry, dealing frequency, audit actor and changes, two bulk " +
                    "audit paths, suspension, contract guards, and scoped report SQL.");
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    private static ClaimsPrincipal Actor(string id, string role) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, id), new Claim(ClaimTypes.Name, "Reports QA"), new Claim(ClaimTypes.Role, role)], "ReportsQA"));

    private static void Require(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException("Reports data check failed: " + label);
    }
    private sealed class MemoryTempData : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "ReportsQA";
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
