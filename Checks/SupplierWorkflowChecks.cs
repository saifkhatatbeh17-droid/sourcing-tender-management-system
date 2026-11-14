using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Controllers;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Services;

namespace SourcingTenderManagement.Checks;

// Explicit integration check: isolated temporary records and attachment directory, removed in finally.
public static class SupplierWorkflowChecks
{
    public static async Task RunAsync(IServiceProvider services, IWebHostEnvironment environment)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var supplier = await db.SupplierProfiles.Include(p => p.User).FirstAsync(p => p.Status == "Approved");
        var source = await db.PurchaseRequests.Include(r => r.PurchaseRequestItems).FirstAsync(r => r.PurchaseRequestItems.Any());
        var officer = (await users.GetUsersInRoleAsync(RoleNames.SourcingOfficer)).First();
        var request = new PurchaseRequest { DepartmentId = source.DepartmentId, RequestedByUserId = source.RequestedByUserId,
            Title = "Temporary supplier regression check", Reason = "Automated regression check", Priority = source.Priority,
            TargetDate = source.TargetDate, Status = "Approved", CreatedAt = DateTime.UtcNow };
        request.PurchaseRequestItems.Add(new PurchaseRequestItem { ItemName = "Test item", Quantity = 1, Unit = "unit", Specifications = "Test specification" });
        db.PurchaseRequests.Add(request);
        await db.SaveChangesAsync();
        var tender = new Tender { PurchaseRequestId = request.PurchaseRequestId, CreatedByUserId = officer.Id,
            Title = request.Title, Description = request.Reason, Status = "Published", OpensAt = DateTime.UtcNow.AddDays(-2), ClosesAt = DateTime.UtcNow.AddDays(-1), CreatedAt = DateTime.UtcNow };
        db.Tenders.Add(tender);
        await db.SaveChangesAsync();
        var invitation = new Invitation { TenderId = tender.TenderId, SupplierProfileId = supplier.SupplierProfileId,
            InvitedByUserId = officer.Id, Status = "Accepted", SentAt = DateTime.UtcNow };
        db.Invitations.Add(invitation);
        await db.SaveChangesAsync();
        var originalRoot = environment.ContentRootPath;
        var testRoot = Path.Combine(Path.GetTempPath(), "tender-upload-check-" + Guid.NewGuid().ToString("N"));
        environment.ContentRootPath = testRoot;
        try
        {
            await using (var tx = await db.Database.BeginTransactionAsync())
            {
                foreach (var status in new[] { "Published", "Open", "Draft", "Cancelled", "UnderEvaluation" })
                {
                    await db.Tenders.Where(t => t.TenderId == tender.TenderId).ExecuteUpdateAsync(u => u.SetProperty(t => t.Status, status));
                    await TenderClosureService.CloseExpired(db, DateTime.UtcNow, ownerId: officer.Id);
                    var expected = status is "Published" or "Open" ? "Closed" : status;
                    Require(await db.Tenders.AnyAsync(t => t.TenderId == tender.TenderId && t.Status == expected), "Incorrect closure transition: " + status);
                }
                await db.Tenders.Where(t => t.TenderId == tender.TenderId).ExecuteUpdateAsync(u => u.SetProperty(t => t.Status, "Published").SetProperty(t => t.ClosesAt, DateTime.UtcNow.AddDays(1)));
                await TenderClosureService.CloseExpired(db, DateTime.UtcNow, ownerId: officer.Id);
                Require(await db.Tenders.AnyAsync(t => t.TenderId == tender.TenderId && t.Status == "Published"), "Future tender closed early");
                await tx.RollbackAsync();
            }
            db.ChangeTracker.Clear();
            await db.Tenders.Where(t => t.TenderId == tender.TenderId).ExecuteUpdateAsync(u => u.SetProperty(t => t.ClosesAt, DateTime.UtcNow.AddDays(1)));
            SupplierController Controller() => new(db, users, environment)
            {
                ControllerContext = new ControllerContext { ActionDescriptor = new Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor(), RouteData = new Microsoft.AspNetCore.Routing.RouteData(), HttpContext = new DefaultHttpContext { RequestServices = services,
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, supplier.UserId) }, "check")) } },
                TempData = new TempDataDictionary(new DefaultHttpContext(), new MemoryTempData())
            };
            OfferFormViewModel Form(int? bidId = null, decimal price = 10) => new()
            {
                InvitationId = invitation.InvitationId, BidId = bidId, Currency = "JOD", DeliveryDays = 7,
                Items = [new OfferLineViewModel { PurchaseRequestItemId = request.PurchaseRequestItems.Single().PurchaseRequestItemId, Quantity = 1, UnitPrice = price, Specifications = "Test specification" }]
            };
            List<IFormFile> FailingFiles() => [new FormFile(new MemoryStream([1,2,3]), 0, 3, "documents", "first.pdf") { Headers = new HeaderDictionary(), ContentType = "application/pdf" }, new FailingFile()];
            Require(await Controller().SaveOffer(Form(), "Submit", FailingFiles()) is ViewResult, "Upload failure did not return form");
            db.ChangeTracker.Clear();
            Require(!await db.Bids.AnyAsync(b => b.TenderId == tender.TenderId), "Failed new submission left a bid");
            Require(!Directory.Exists(testRoot) || !Directory.EnumerateFiles(testRoot, "*", SearchOption.AllDirectories).Any(), "Failed upload left files");
            await Controller().SaveOffer(Form(), "Draft", null);
            db.ChangeTracker.Clear();
            var draft = await db.Bids.Include(b => b.BidItems).SingleAsync(b => b.TenderId == tender.TenderId);
            await Controller().SaveOffer(Form(draft.BidId, 99), "Submit", FailingFiles());
            db.ChangeTracker.Clear();
            var unchanged = await db.Bids.Include(b => b.BidItems).SingleAsync(b => b.BidId == draft.BidId);
            Require(unchanged.Status == "Draft" && unchanged.SubmittedAt == null && unchanged.BidItems.Single().UnitPrice == 10, "Failed draft submission changed saved data");
            Require(!Directory.EnumerateFiles(testRoot, "*", SearchOption.AllDirectories).Any(), "Failed draft left files");
            var file = new FormFile(new MemoryStream([1,2,3]), 0, 3, "documents", "success.pdf") { Headers = new HeaderDictionary(), ContentType = "application/pdf" };
            Require(await Controller().SaveOffer(Form(draft.BidId), "Submit", [file]) is RedirectToActionResult, "Successful upload failed");
            db.ChangeTracker.Clear();
            Require(await db.Bids.AnyAsync(b => b.BidId == draft.BidId && b.Status == "Submitted" && b.SubmittedAt != null), "Submission did not commit");
            Require(await db.BidDocuments.CountAsync(d => d.BidId == draft.BidId) == 1, "Attachment metadata missing");
            var history = (ViewResult)await Controller().History();
            var bids = (List<Bid>)history.Model!;
            Require(bids.Any(b => b.BidId == draft.BidId) && bids.All(b => b.Invitation.SupplierProfileId == supplier.SupplierProfileId && b.SubmittedAt != null), "History ownership or status filter failed");
            Require(await Controller().Offer(-1) is NotFoundResult, "Unknown offer was exposed");
            Console.WriteLine("PASS: deadline transitions and future dates; new/draft attachment failures rolled back with file cleanup; successful submission; supplier-scoped history.");
        }
        finally
        {
            environment.ContentRootPath = originalRoot;
            db.ChangeTracker.Clear();
            await using var cleanup = await db.Database.BeginTransactionAsync();
            await db.Notifications.Where(n => n.RelativeUrl == $"/SourcingOfficer/Offers?tenderId={tender.TenderId}").ExecuteDeleteAsync();
            await db.BidDocuments.Where(d => d.Bid.TenderId == tender.TenderId).ExecuteDeleteAsync();
            await db.BidItems.Where(i => i.Bid.TenderId == tender.TenderId).ExecuteDeleteAsync();
            await db.Bids.Where(b => b.TenderId == tender.TenderId).ExecuteDeleteAsync();
            await db.Invitations.Where(i => i.TenderId == tender.TenderId).ExecuteDeleteAsync();
            await db.Tenders.Where(t => t.TenderId == tender.TenderId).ExecuteDeleteAsync();
            await db.PurchaseRequestItems.Where(i => i.PurchaseRequestId == request.PurchaseRequestId).ExecuteDeleteAsync();
            await db.PurchaseRequests.Where(r => r.PurchaseRequestId == request.PurchaseRequestId).ExecuteDeleteAsync();
            await cleanup.CommitAsync();
            if (Directory.Exists(testRoot)) Directory.Delete(testRoot, true);
        }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class MemoryTempData : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
    private sealed class FailingFile : IFormFile
    {
        public string ContentType => "application/pdf"; public string ContentDisposition => "";
        public IHeaderDictionary Headers => new HeaderDictionary(); public long Length => 3;
        public string Name => "documents"; public string FileName => "failure.pdf";
        public Stream OpenReadStream() => throw new IOException("Simulated read failure");
        public void CopyTo(Stream target) => throw new IOException("Simulated copy failure");
        public async Task CopyToAsync(Stream target, CancellationToken cancellationToken = default)
        { await target.WriteAsync(new byte[] { 1 }, cancellationToken); throw new IOException("Simulated partial copy failure"); }
    }
}
