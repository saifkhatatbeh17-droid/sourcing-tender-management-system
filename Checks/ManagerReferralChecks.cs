// Self-test, not part of the website. Tests manager referral. Not run from Program.cs; nothing calls it now, so it is kept only for running by hand.
using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SourcingTenderManagement.Contracts.TopCommittee;
using SourcingTenderManagement.Models.TopCommittee;
using SourcingTenderManagement.Controllers;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Services.TopCommittee;

namespace SourcingTenderManagement.Checks;

public static class ManagerReferralChecks
{
    public static async Task RunAsync(string connection, bool verifyVoting = false)
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options);
        var manager = await db.Users.SingleAsync(u => u.Email == "manager@tender.local");
        var officer = await db.Users.SingleAsync(u => u.Email == "officer@tender.local");
        var tender = new Tender { Title = "ReferralCheck-" + Guid.NewGuid().ToString("N"), Description = "Temporary referral test",
            PurchaseRequestId = await db.PurchaseRequests.Select(r => r.PurchaseRequestId)
                .FirstAsync(), CreatedByUserId = officer.Id,
            Status = "AwaitingManagerReview", OpensAt = DateTime.UtcNow.AddDays(-2), ClosesAt = DateTime.UtcNow.AddDays(-1) };
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        try
        {
            db.Tenders.Add(tender); await db.SaveChangesAsync();
            var bid = new Bid { TenderId = tender.TenderId, Invitation = new Invitation { TenderId = tender.TenderId,
                SupplierProfileId = await db.SupplierProfiles.Select(s => s.SupplierProfileId).FirstAsync(),
                InvitedByUserId = officer.Id, Status = "Accepted" }, Currency = "JOD", Status = "Compliant", ComplianceStatus = "Compliant", SubmittedAt = DateTime.UtcNow };
            var report = new EvaluationReport { TenderId = tender.TenderId, Bid = bid, Status = "ManagerApproved", ManagerSummary = "Test recommendation" };
            db.EvaluationReports.Add(report);
            db.TenderApprovals.Add(new TenderApproval { TenderId = tender.TenderId, UserId = manager.Id, Stage = "Manager", Decision = "Approved" });
            await db.SaveChangesAsync();
            var http = new DefaultHttpContext { User = Actor(manager.Id, RoleNames.SourcingManager) };
            var controller = new SourcingManagerController(db, NullLogger<SourcingManagerController>.Instance)
            { ControllerContext = new ControllerContext { HttpContext = http },
                TempData = new TempDataDictionary(http, new MemoryTempData()),
                Url = new TestUrls() };
            var input = new ManagerForwardInput { ReportId = report.EvaluationReportId, Reason = "Demo referral test" };
            controller.Forward(input);
            var assignments = await db.CommitteeAssignments.Where(a => a.TenderId == tender.TenderId
                && a.CommitteeType == "Top")
                .ToListAsync();
            Check(assignments.Count == 4
                && assignments.Select(a => a.UserId)
                .Distinct()
                .Count() == 4, "Four distinct assignments were not created.");
            Check(tender.Status == "AwaitingCommitteeDecision", "Tender was not forwarded.");
            var notificationText = $"Tender #{tender.TenderId} has been referred for final decision.";
            Check(await db.Notifications.CountAsync(n => n.Message == notificationText) == 4, "Four member notifications were not created.");
            var queries = new TopCommitteeQueryService(db, new TopCommitteeAccessService(db));
            foreach (var assignment in assignments)
            {
                var dashboard = await queries.GetDashboardAsync(Actor(assignment.UserId, RoleNames.TopCommittee));
                Check(dashboard.PendingTenders.Any(t => t.TenderId == tender.TenderId), "A Top Committee member cannot see the referred tender.");
            }
            controller.Forward(input);
            Check(await db.CommitteeAssignments.CountAsync(a => a.TenderId == tender.TenderId
                && a.CommitteeType == "Top") == 4, "Repeat referral created duplicates.");
            tender.Status = "AwaitingManagerReview";
            db.CommitteeAssignments.Remove(assignments[3]); await db.SaveChangesAsync();
            controller.Forward(input);
            Check(tender.Status == "AwaitingManagerReview", "Incomplete committee was referred.");
            Console.WriteLine("PASS: four assignments, notifications, visibility for every Top member, duplicate prevention and partial-assignment rejection.");
            if (verifyVoting)
            {
                var otherProfiles = await db.SupplierProfiles.Where(s => s.SupplierProfileId != bid.Invitation.SupplierProfileId)
                    .OrderBy(s => s.SupplierProfileId)
                    .Take(3)
                    .ToListAsync();
                Check(otherProfiles.Count == 3, "Four demo suppliers are needed for the award notification check.");
                var otherOffers = new List<Bid>();
                foreach (var status in new[] { "Submitted", "Draft", "Withdrawn" })
                {
                    var supplier = otherProfiles[otherOffers.Count];
                    var other = new Bid { TenderId = tender.TenderId, Invitation = new Invitation { TenderId = tender.TenderId,
                        SupplierProfileId = supplier.SupplierProfileId, InvitedByUserId = officer.Id, Status = "Accepted" },
                        Currency = "JOD", Status = status, SubmittedAt = status == "Draft" ? null : DateTime.UtcNow };
                    db.Bids.Add(other); otherOffers.Add(other);
                }
                db.CommitteeAssignments.Add(new CommitteeAssignment { TenderId = tender.TenderId,
                    UserId = assignments[3].UserId,
                    CommitteeType = "Top",
                    Position = 4 });
                tender.Status = "AwaitingCommitteeDecision";
                await db.SaveChangesAsync();
                var services = new ServiceCollection().AddLogging();
                services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(connection));
                services.AddTopCommitteeFoundation();
                await using var provider = services.BuildServiceProvider();
                using var scope = provider.CreateScope();
                var voting = scope.ServiceProvider.GetRequiredService<ITopCommitteeVoting>();
                var readiness = scope.ServiceProvider.GetRequiredService<ITopCommitteeVotingReadiness>();
                var members = await db.CommitteeAssignments.AsNoTracking()
                    .Where(a => a.TenderId == tender.TenderId && a.CommitteeType == "Top")
                    .OrderBy(a => a.Position)
                    .ToListAsync();
                Check(await readiness.IsDatabaseReadyAsync(Actor(members[0].UserId, RoleNames.TopCommittee), tender.TenderId), "Existing approval-only constraint was rejected.");
                try
                {
                    await voting.SubmitAsync(Actor(members[0].UserId,
                        RoleNames.TopCommittee), new CommitteeVoteInput { TenderId = tender.TenderId,
                        ExpectedNominatedBidId = bid.BidId,
                        Decision = "Rejected",
                        Notes = "Forged rejection",
                        Confirmed = true });
                    throw new Exception("Rejection was accepted.");
                }
                catch (ValidationException) { }
                for (var index = 0; index < members.Count; index++)
                {
                    await voting.SubmitAsync(Actor(members[index].UserId,
                        RoleNames.TopCommittee), new CommitteeVoteInput { TenderId = tender.TenderId,
                        ExpectedNominatedBidId = bid.BidId,
                        Decision = "Approved",
                        Confirmed = true });
                    await db.Entry(tender).ReloadAsync();
                    Check(tender.Status == (index == 3 ? "Awarded" : "AwaitingCommitteeDecision"), "Incorrect award timing.");
                    if (index < 3) Check(!await db.Notifications.AnyAsync(n => n.RelativeUrl == $"/Supplier/Offer/{bid.BidId}"), "Supplier was notified before the final approval.");
                }
                Check(tender.AwardedBidId == bid.BidId
                    && await db.TenderApprovals.CountAsync(a => a.TenderId == tender.TenderId && a.Stage == "Top") == 4, "Final award or vote count is incorrect.");
                var outcomes = await db.Bids.AsNoTracking().Where(b => b.TenderId == tender.TenderId).ToListAsync();
                Check(outcomes.Single(b => b.BidId == bid.BidId).Status == "Accepted", "Winning offer was not accepted.");
                Check(outcomes.Single(b => b.BidId == otherOffers[0].BidId).Status == "Rejected", "Losing submitted offer was not rejected.");
                Check(outcomes.Single(b => b.BidId == otherOffers[1].BidId).Status == "Draft"
                    && outcomes
                    .Single(b => b.BidId == otherOffers[2].BidId).Status == "Withdrawn", "Draft or withdrawn offer was changed.");
                var urls = outcomes.Select(b => $"/Supplier/Offer/{b.BidId}").ToList();
                var notices = await db.Notifications.AsNoTracking()
                    .Where(n => urls.Contains(n.RelativeUrl!))
                    .ToListAsync();
                Check(notices.Count == 2
                    && notices.Single(n => n.Title == "Your offer was not selected").UserId == otherProfiles[0].UserId, "Supplier results were not sent privately to each participant.");
                var coordinator = scope.ServiceProvider.GetRequiredService<ITopCommitteeTenderTransaction>();
                await coordinator.ExecuteAsync(tender.TenderId, async (locked, ct) =>
                {
                    var access = await TopCommitteeClarificationStore.LoadAsync(locked, members[0].UserId, tender.TenderId, ct);
                    var nomination = await locked.Bids.SingleAsync(b => b.BidId == bid.BidId, ct);
                    await TopCommitteeNotificationEvents.DecisionAsync(locked, new CommitteeVotingContext(access, nomination), ct);
                    await locked.SaveChangesAsync(ct); return true;
                });
                Check(await db.Notifications.CountAsync(n => urls.Contains(n.RelativeUrl!)) == 2, "Repeated award processing duplicated supplier notifications.");
                Console.WriteLine("PASS: winner/loser statuses, private notifications, no early or duplicate results, drafts and withdrawn offers preserved.");
                Console.WriteLine("PASS: existing database readiness, forged rejection blocked, four approvals and fourth-vote award without schema changes.");
            }
        }
        finally
        {
            await using var cleanup = await db.Database.BeginTransactionAsync();
            var supplierUrls = await db.Bids.Where(b => b.TenderId == tender.TenderId)
                .Select(b => "/Supplier/Offer/" + b.BidId)
                .ToListAsync();
            await db.Notifications.Where(n => n.Message == $"Tender #{tender.TenderId} has been referred for final decision."
                || n.RelativeUrl == $"/TopCommittee/TenderDetails/{tender.TenderId}"
                || supplierUrls.Contains(n.RelativeUrl!))
                .ExecuteDeleteAsync();
            await db.Tenders.Where(t => t.TenderId == tender.TenderId)
                .ExecuteUpdateAsync(update => update.SetProperty(t => t.AwardedBidId, (int?)null)
                    .SetProperty(t => t.AwardedAt, (DateTime?)null)
                    .SetProperty(t => t.Status, "AwaitingCommitteeDecision"));
            await db.TenderApprovals.Where(a => a.TenderId == tender.TenderId).ExecuteDeleteAsync();
            await db.EvaluationReports.Where(r => r.TenderId == tender.TenderId).ExecuteDeleteAsync();
            await db.CommitteeAssignments.Where(a => a.TenderId == tender.TenderId).ExecuteDeleteAsync();
            await db.Bids.Where(b => b.TenderId == tender.TenderId).ExecuteDeleteAsync();
            await db.Invitations.Where(i => i.TenderId == tender.TenderId).ExecuteDeleteAsync();
            await db.Tenders.Where(t => t.TenderId == tender.TenderId).ExecuteDeleteAsync();
            await cleanup.CommitAsync();
            Console.WriteLine("Temporary referral fixtures removed.");
        }
    }
    private static ClaimsPrincipal Actor(string id, string role) => new(new ClaimsIdentity([new(ClaimTypes.NameIdentifier,
        id), new(ClaimTypes.Role,
        role)], "Test"));
    private sealed class MemoryTempData : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
    private sealed class TestUrls : IUrlHelper
    {
        public ActionContext ActionContext { get; } = new();
        public string? Action(UrlActionContext context) => "/TopCommittee/Decisions";
        public string? Content(string? path) => path;
        public bool IsLocalUrl(string? url) => true;
        public string? Link(string? routeName, object? values) => null;
        public string? RouteUrl(UrlRouteContext context) => null;
    }
}
