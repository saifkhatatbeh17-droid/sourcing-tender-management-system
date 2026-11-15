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
            SourcingOfficerController Officer(string id) => new(db)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id)], "check"))
                } },
                TempData = new TempDataDictionary(new DefaultHttpContext(), new MemoryTempData())
            };
            await using (var categoryCheck = await db.Database.BeginTransactionAsync())
            {
                var matching = "Temporary instrument category";
                await db.Tenders.Where(t => t.TenderId == tender.TenderId)
                    .ExecuteUpdateAsync(u => u.SetProperty(t => t.Category, matching));
                var otherSupplier = await db.SupplierProfiles.AsNoTracking()
                    .FirstAsync(s => s.Status == "Approved" && s.SupplierProfileId != supplier.SupplierProfileId);
                var candidate = db.SupplierProfiles.Where(s => s.SupplierProfileId == otherSupplier.SupplierProfileId);
                await candidate.ExecuteUpdateAsync(u => u.SetProperty(s => s.Category, "Different category"));
                await Officer(officer.Id).CreateInvitation(tender.TenderId, [otherSupplier.SupplierProfileId]);
                Require(!await db.Invitations.AnyAsync(i => i.TenderId == tender.TenderId &&
                    i.SupplierProfileId == otherSupplier.SupplierProfileId), "Mismatched supplier was invited");
                await candidate.ExecuteUpdateAsync(u => u.SetProperty(s => s.Category, matching.ToUpperInvariant()));
                await Officer(officer.Id).CreateInvitation(tender.TenderId, null, allSuppliers: true);
                Require(await db.Invitations.CountAsync(i => i.TenderId == tender.TenderId) == 2 &&
                    await db.Invitations.AnyAsync(i => i.TenderId == tender.TenderId &&
                        i.SupplierProfileId == otherSupplier.SupplierProfileId), "Invite all selected suppliers outside the matching category");
                await categoryCheck.RollbackAsync();
            }
            db.ChangeTracker.Clear();
            Console.WriteLine("PASS: mismatched suppliers rejected and invite all limited to matching categories.");
            var future = DateTime.UtcNow.AddDays(1);
            var past = DateTime.UtcNow.AddDays(-1);
            var testTender = db.Tenders.Where(t => t.TenderId == tender.TenderId);
            var testBid = db.Bids.Where(b => b.BidId == draft.BidId);
            await Officer(officer.Id).ReviewAndSend(tender.TenderId, new() { [draft.BidId] = "Compliant" });
            db.ChangeTracker.Clear();
            Require(await testTender.AnyAsync(t => t.Status == "Published"), "Early committee handoff changed tender status");
            Require(await Officer("other-officer").CloseSubmissionsEarly(tender.TenderId) is NotFoundResult,
                "Another officer could close submissions");
            await Officer(officer.Id).CloseSubmissionsEarly(tender.TenderId);
            db.ChangeTracker.Clear();
            Require(await testTender.AnyAsync(t => t.Status == "Published"), "Early closure accepted fewer than three suppliers");
            Require(await Officer("other-officer").CancelTender(tender.TenderId) is NotFoundResult, "Another officer could cancel the tender");
            Require(await Controller().CancelOffer(-1) is NotFoundResult, "An unknown offer could be canceled");
            await testTender.ExecuteUpdateAsync(u => u.SetProperty(t => t.ClosesAt, past));
            await Controller().CancelOffer(draft.BidId);
            db.ChangeTracker.Clear();
            Require(await testBid.AnyAsync(b => b.Status == "Submitted"), "Expired offer cancellation succeeded");
            await Officer(officer.Id).CancelTender(tender.TenderId);
            db.ChangeTracker.Clear();
            Require(await testTender.AnyAsync(t => t.Status == "Published"), "Expired tender cancellation succeeded");
            await testTender.ExecuteUpdateAsync(u => u.SetProperty(t => t.ClosesAt, future));
            await Controller().CancelOffer(draft.BidId);
            db.ChangeTracker.Clear();
            Require(await testBid.AnyAsync(b => b.Status == "Withdrawn" && b.SubmittedAt != null), "Offer was not withdrawn before the deadline");
            await Officer(officer.Id).CancelTender(tender.TenderId);
            db.ChangeTracker.Clear();
            Require(await testTender.AnyAsync(t => t.Status == "Cancelled"), "Tender was not canceled before the deadline");
            var canceledOffers = (ViewResult)await Controller().Offers(null, "Cancelled");
            Require(((List<Bid>)canceledOffers.Model!).Any(b => b.BidId == draft.BidId && b.Invitation.Tender.Status == "Cancelled"), "Canceled tender missing from supplier offers");
            Require(await Controller().SaveOffer(Form(), "Submit", null) is ForbidResult, "Canceled tender still accepts offers");
            var extensions = db.Set<TenderOfferExtension>().Where(e => e.TenderId == tender.TenderId);
            await testTender.ExecuteUpdateAsync(u => u.SetProperty(t => t.Status, "Closed").SetProperty(t => t.ClosesAt, past));
            await testBid.ExecuteUpdateAsync(u => u.SetProperty(b => b.Status, "NonCompliant").SetProperty(b => b.ComplianceStatus, "NonCompliant"));
            await Officer(officer.Id).ExtendOfferRound(tender.TenderId, 1, DateTime.UtcNow.AddHours(1));
            db.ChangeTracker.Clear();
            Require(!await extensions.AnyAsync(), "A round opened without a supplier response window");
            await Officer(officer.Id).ExtendOfferRound(tender.TenderId, 1, DateTime.UtcNow.AddDays(2));
            db.ChangeTracker.Clear();
            Require(await extensions.AnyAsync(e => e.Round == 2 && e.PreviousCompliantOffers == 0 &&
                e.Outreach.Contains("Reminded")), "First extension and outreach were not recorded");
            Require(await db.Notifications.AnyAsync(n => n.UserId == supplier.UserId &&
                n.RelativeUrl == $"/Supplier/Invitations#tender-{tender.TenderId}"), "Supplier did not receive a reminder");
            await Officer(officer.Id).ExtendOfferRound(tender.TenderId, 1, DateTime.UtcNow.AddDays(2));
            db.ChangeTracker.Clear();
            Require(await extensions.CountAsync() == 1, "A repeated click skipped a round");
            Require(await Controller().SaveOffer(Form(draft.BidId), "Submit", null) is RedirectToActionResult,
                "Supplier could not revise a non-compliant offer after reopening");
            db.ChangeTracker.Clear();
            Require(await testBid.AnyAsync(b => b.Status == "Submitted" && b.ComplianceStatus == null),
                "Revised offer did not return to compliance review");

            for (var round = 2; round <= 3; round++)
            {
                var originalEnvironment = environment.EnvironmentName;
                try
                {
                    environment.EnvironmentName = "Production";
                    Require(await Officer(officer.Id).SimulateDeadline(tender.TenderId, round, environment) is NotFoundResult,
                        "Deadline simulation was exposed in production");
                    environment.EnvironmentName = "Development";
                    Require(await Officer("other-officer").SimulateDeadline(tender.TenderId, round, environment) is NotFoundResult,
                        "Another officer could simulate the deadline");
                    await Officer(officer.Id).SimulateDeadline(tender.TenderId, round - 1, environment);
                    db.ChangeTracker.Clear();
                    Require(await testTender.AnyAsync(t => t.Status == "Open"), "An outdated round closed submissions");
                    await Officer(officer.Id).SimulateDeadline(tender.TenderId, round, environment);
                    db.ChangeTracker.Clear();
                    var closedTender = await testTender.AsNoTracking().SingleAsync();
                    Require(closedTender.Status == "Closed" && closedTender.ClosesAt <= DateTime.UtcNow,
                        "Development deadline simulation did not close the round");
                }
                finally
                {
                    environment.EnvironmentName = originalEnvironment;
                }
                await Officer(officer.Id).ReviewAndSend(tender.TenderId, new() { [draft.BidId] = "Compliant" }, shortfallReason: "Attempted early exception");
                db.ChangeTracker.Clear();
                Require(await testTender.AnyAsync(t => t.Status == "Closed") &&
                    await testBid.AnyAsync(b => b.Status == "Compliant"), "Early shortfall was forwarded or compliance was lost");
                await Officer(officer.Id).ExtendOfferRound(tender.TenderId, round, DateTime.UtcNow.AddDays(2));
                db.ChangeTracker.Clear();
                Require(await extensions.AnyAsync(e => e.Round == round + 1 && e.PreviousCompliantOffers == 1),
                    "Extension did not preserve the compliant offer");
            }

            await testTender.ExecuteUpdateAsync(u => u.SetProperty(t => t.Status, "Closed").SetProperty(t => t.ClosesAt, past));
            await Officer(officer.Id).ExtendOfferRound(tender.TenderId, 4, DateTime.UtcNow.AddDays(2));
            db.ChangeTracker.Clear();
            Require(await extensions.CountAsync() == 3, "A fifth round was opened");
            await Officer(officer.Id).ReviewAndSend(tender.TenderId, new());
            db.ChangeTracker.Clear();
            Require(await testTender.AnyAsync(t => t.Status == "Closed"), "Final shortfall accepted without a reason");

            var evaluators = (await users.GetUsersInRoleAsync(RoleNames.EvaluationCommittee)).Take(4).ToList();
            Require(evaluators.Count == 4, "Four demo committee members are needed for this fixture");
            db.CommitteeAssignments.AddRange(evaluators.Select((member, index) => new CommitteeAssignment
            {
                TenderId = tender.TenderId, UserId = member.Id, Position = index + 1, CommitteeType = "Evaluation"
            }));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            const string reason = "Only one offer met the specifications after supplier outreach.";
            await Officer(officer.Id).ReviewAndSend(tender.TenderId, new(), shortfallReason: reason);
            db.ChangeTracker.Clear();
            Require(await testTender.AnyAsync(t => t.Status == "UnderEvaluation" && t.OfferShortfallReason == reason),
                "Final-round exception and reason were not recorded");
            // Reuse the fixture for a first-round early closure and normal handoff.
            await extensions.ExecuteDeleteAsync();
            await testTender.ExecuteUpdateAsync(u => u.SetProperty(t => t.Status, "Published").SetProperty(t => t.ClosesAt, future));
            var extraInvitations = await db.Invitations.AsNoTracking()
                .Where(i => i.TenderId == tender.TenderId && i.InvitationId != invitation.InvitationId)
                .OrderBy(i => i.InvitationId).Take(2).ToListAsync();
            Require(extraInvitations.Count == 2, "Two additional approved suppliers are needed for this fixture");
            db.Bids.AddRange(extraInvitations.Select(i => new Bid
            {
                TenderId = tender.TenderId, InvitationId = i.InvitationId, Currency = "JOD",
                Status = "Compliant", ComplianceStatus = "Compliant", SubmittedAt = past.AddMinutes(-1)
            }));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            await Officer(officer.Id).CloseSubmissionsEarly(tender.TenderId);
            db.ChangeTracker.Clear();
            Require(await testTender.AnyAsync(t => t.Status == "Closed" && t.ClosesAt < future), "Three suppliers could not trigger early closure");
            await Officer(officer.Id).CloseSubmissionsEarly(tender.TenderId);
            db.ChangeTracker.Clear();
            var notifications = db.Notifications.Where(n => n.RelativeUrl == $"/Supplier/Invitations#tender-{tender.TenderId}" &&
                n.Title == "Tender submissions closed early");
            var invitedSuppliers = await db.Invitations.CountAsync(i => i.TenderId == tender.TenderId);
            Require(await notifications.CountAsync() == invitedSuppliers, "Suppliers did not each receive exactly one closure notice");
            await Controller().CancelOffer(draft.BidId);
            db.ChangeTracker.Clear();
            Require(await testBid.AnyAsync(b => b.Status == "Compliant"), "An offer was withdrawn after early closure");
            Require(await Controller().SaveOffer(Form(), "Submit", null) is ForbidResult, "Early closure still accepts submissions");
            await Officer(officer.Id).ReviewAndSend(tender.TenderId, new());
            db.ChangeTracker.Clear();
            Require(await testTender.AnyAsync(t => t.Status == "UnderEvaluation" && t.OfferShortfallReason == null),
                "Three compliant suppliers could not be sent in the first round");
            Console.WriteLine("PASS: early closure requires three suppliers, locks submissions and withdrawals, sends one notice per supplier, and allows normal handoff.");
            Console.WriteLine("PASS: development deadline simulation closes the current round while rejecting stale requests, other officers, and production access.");
            Console.WriteLine("PASS: outreach, repeat-click and deadline guards, revised offers, preserved compliance, and round-four exception.");
            Console.WriteLine("PASS: early handoff and expired cancellation guards, owner-only tender cancellation, supplier withdrawal, and shared cancellation status.");
            Console.WriteLine("PASS: deadline transitions and future dates; new/draft attachment failures rolled back with file cleanup; successful submission; supplier-scoped history.");
        }
        finally
        {
            environment.ContentRootPath = originalRoot;
            db.ChangeTracker.Clear();
            await using var cleanup = await db.Database.BeginTransactionAsync();
            await db.Notifications.Where(n => n.RelativeUrl == $"/SourcingOfficer/Offers?tenderId={tender.TenderId}" ||
                n.RelativeUrl == $"/Supplier/Invitations#tender-{tender.TenderId}").ExecuteDeleteAsync();
            await db.CommitteeAssignments.Where(a => a.TenderId == tender.TenderId).ExecuteDeleteAsync();
            await db.BidDocuments.Where(d => d.Bid.TenderId == tender.TenderId).ExecuteDeleteAsync();
            await db.BidItems.Where(i => i.Bid.TenderId == tender.TenderId).ExecuteDeleteAsync();
            await db.Bids.Where(b => b.TenderId == tender.TenderId).ExecuteDeleteAsync();
            await db.Invitations.Where(i => i.TenderId == tender.TenderId).ExecuteDeleteAsync();
            await db.Set<TenderOfferExtension>().Where(e => e.TenderId == tender.TenderId).ExecuteDeleteAsync();
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
