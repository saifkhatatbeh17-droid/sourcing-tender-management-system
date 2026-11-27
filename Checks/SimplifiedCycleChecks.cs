using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Controllers;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;
namespace SourcingTenderManagement.Checks;
// Self-test, not part of the website. Tests simplified cycle. Not run from Program.cs; nothing calls it now, so it is kept only for running by hand.
public static class SimplifiedCycleChecks
{
    public static async Task RunAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var officer = (await users.GetUsersInRoleAsync(RoleNames.SourcingOfficer)).First();
        var source = await db.PurchaseRequests.FirstAsync();
        var suppliers = await db.SupplierProfiles.Where(s => s.Status == "Approved")
            .Select(s => s.SupplierProfileId)
            .Take(3)
            .ToListAsync();
        var request = new PurchaseRequest { DepartmentId = source.DepartmentId, RequestedByUserId = source.RequestedByUserId, ReviewedByUserId = officer.Id,
            Title = "Temporary simplified cycle check", Reason = "Temporary regression fixture", Priority = "Normal", Status = "UnderReview", TargetDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)), CreatedAt = DateTime.UtcNow, SubmittedAt = DateTime.UtcNow };
        db.PurchaseRequests.Add(request); await db.SaveChangesAsync();
        int tenderId = 0;
        SourcingOfficerController Controller(string? id = null) { var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, id
            ?? officer.Id) }, "check")) }; return new(db) { ControllerContext = new ControllerContext { HttpContext = context }, TempData = new TempDataDictionary(context, new MemoryData()) }; }
        void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        SourcingOfficerTenderForm Form() => new() { PurchaseRequestId = request.PurchaseRequestId,
            Title = request.Title,
            Description = request.Reason,
            OpensAt = DateTime.UtcNow,
            ClosesAt = DateTime.UtcNow.AddDays(7) };
        try
        {
            var invalid = Controller(); await invalid.Create(Form(), true, []); db.ChangeTracker.Clear();
            Require(!await db.Tenders.AnyAsync(t => t.PurchaseRequestId == request.PurchaseRequestId), "Invalid launch created tender");
            Require(await db.PurchaseRequests.AnyAsync(r => r.PurchaseRequestId == request.PurchaseRequestId
                && r.Status == "UnderReview"), "Invalid launch approved request");
            var wrong = Controller("not-owner"); await wrong.Create(Form(), true, suppliers); db.ChangeTracker.Clear();
            Require(!await db.Tenders.AnyAsync(t => t.PurchaseRequestId == request.PurchaseRequestId), "Another officer approved assigned request");
            await db.PurchaseRequests.Where(r => r.PurchaseRequestId == request.PurchaseRequestId)
                .ExecuteUpdateAsync(u => u.SetProperty(r => r.Status, "Submitted")
                    .SetProperty(r => r.ReviewedByUserId, (string?)null));
            await Controller().ReturnRequest(request.PurchaseRequestId, "");
            Require(await db.PurchaseRequests.AnyAsync(r => r.PurchaseRequestId == request.PurchaseRequestId
                && r.Status == "Submitted"
                && r.ReviewedByUserId == null), "Empty return reason claimed request");
            await Controller().ReturnRequest(request.PurchaseRequestId, "Clarify specifications");
            Require(await db.PurchaseRequests.AnyAsync(r => r.PurchaseRequestId == request.PurchaseRequestId
                && r.Status == "Returned"
                && r.ReviewedByUserId == officer.Id), "Combined claim and return failed");
            await db.PurchaseRequests.Where(r => r.PurchaseRequestId == request.PurchaseRequestId)
                .ExecuteUpdateAsync(u => u.SetProperty(r => r.Status, "Submitted")
                    .SetProperty(r => r.ReviewedByUserId, (string?)null));
            await Controller().Create(Form(), true, suppliers); db.ChangeTracker.Clear();
            var tender = await db.Tenders.SingleAsync(t => t.PurchaseRequestId == request.PurchaseRequestId); tenderId = tender.TenderId;
            Require(tender.Status == "Published",
                "Launch failed"); Require(await db.Invitations.CountAsync(i => i.TenderId == tenderId) == suppliers.Count,
                "Invitations not combined");
            Require(await db.PurchaseRequests.AnyAsync(r => r.PurchaseRequestId == request.PurchaseRequestId
                && r.Status == "ConvertedToTender"
                && r.ReviewedAt != null), "Approval not recorded");
            var invitation = await db.Invitations.FirstAsync(i => i.TenderId == tenderId);
            var bid = new Bid { TenderId = tenderId,
                InvitationId = invitation.InvitationId,
                Currency = "JOD",
                Status = "Submitted",
                SubmittedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                DeliveryDays = 7 };
            db.Bids.Add(bid); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            await Controller().ReviewAndSend(tenderId, new()); db.ChangeTracker.Clear();
            Require(await db.Tenders.AnyAsync(t => t.TenderId == tenderId
                && t.Status == "Published"), "Incomplete review forwarded");
            await Controller().ReviewAndSend(tenderId, new() { [bid.BidId] = "Compliant" }); db.ChangeTracker.Clear();
            Require(await db.Tenders.AnyAsync(t => t.TenderId == tenderId
                && t.Status == "Published"), "Handoff before deadline succeeded");
            var expired = DateTime.UtcNow.AddMinutes(-1);
            await db.Tenders.Where(t => t.TenderId == tenderId).ExecuteUpdateAsync(u => u
                .SetProperty(t => t.OpensAt, expired.AddDays(-1)).SetProperty(t => t.ClosesAt, expired));
            var question = new Clarification { TenderId = tenderId,
                SenderUserId = officer.Id,
                RecipientUserId = source.RequestedByUserId,
                Subject = "Temporary question",
                Description = "Temporary",
                Status = "Pending",
                CreatedAt = DateTime.UtcNow }; db.Clarifications.Add(question); await db
                .SaveChangesAsync();
            await Controller().ReviewAndSend(tenderId, new() { [bid.BidId] = "Compliant" }); db.ChangeTracker.Clear();
            Require(await db.Bids.AnyAsync(b => b.BidId == bid.BidId
                && b.ComplianceStatus == null), "Blocked handoff partially saved review");
            await db.Clarifications.Where(c => c.ClarificationId == question.ClarificationId).ExecuteDeleteAsync();
            var otherInvitations = await db.Invitations.Where(i => i.TenderId == tenderId
                && i.InvitationId != invitation.InvitationId)
                .ToListAsync();
            db.Bids.AddRange(otherInvitations.Select(i => new Bid
            {
                TenderId = tenderId, InvitationId = i.InvitationId, Currency = "JOD", Status = "Compliant",
                ComplianceStatus = "Compliant", SubmittedAt = expired.AddMinutes(-1), DeliveryDays = 7
            }));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            await Controller().ReviewAndSend(tenderId, new() { [bid.BidId] = "Compliant" }); db.ChangeTracker.Clear();
            Require(await db.Tenders.AnyAsync(t => t.TenderId == tenderId
                && t.Status == "UnderEvaluation"), "Early handoff before the future deadline failed");
            Require(await db.CommitteeAssignments.CountAsync(a => a.TenderId == tenderId) == 4, "Committee not initialized");
            Require(await db.EvaluationCriteria.Where(c => c.TenderId == tenderId)
                .SumAsync(c => c.Weight) == 100, "Default weights missing");
            Require(await db.EvaluationReports.AnyAsync(r => r.TenderId == tenderId
                && r.Status == "Draft"
                && r.RecommendedBidId == null), "Manual choice not preserved");
            await Controller().ReviewAndSend(tenderId, new() { [bid.BidId] = "Compliant" }); db.ChangeTracker.Clear();
            Require(await db.CommitteeAssignments.CountAsync(a => a.TenderId == tenderId) == 4, "Duplicate assignments");
            Console.WriteLine("PASS: combined approval/publish/invite; invalid launch and ownership rollback; incomplete " +
                    "review and clarification blockers; automatic four-member setup/weights/report; no automatic " +
                    "supplier selection; repeat handoff guard.");
        }
        finally
        {
            db.ChangeTracker.Clear();
            if (tenderId == 0) tenderId = await db.Tenders.Where(t => t.PurchaseRequestId == request.PurchaseRequestId)
                .Select(t => t.TenderId)
                .FirstOrDefaultAsync();
            await using var cleanup = await db.Database.BeginTransactionAsync();
            await db.Notifications.Where(n => n.RelativeUrl == $"/EvaluationCommittee/Evaluations/{tenderId}")
                .ExecuteDeleteAsync();
            await db.Clarifications.Where(c => c.TenderId == tenderId).ExecuteDeleteAsync();
            await db.EvaluationReports.Where(r => r.TenderId == tenderId).ExecuteDeleteAsync();
            await db.EvaluationCriteria.Where(c => c.TenderId == tenderId).ExecuteDeleteAsync();
            await db.CommitteeAssignments.Where(a => a.TenderId == tenderId).ExecuteDeleteAsync();
            await db.Bids.Where(b => b.TenderId == tenderId).ExecuteDeleteAsync();
            await db.Invitations.Where(i => i.TenderId == tenderId).ExecuteDeleteAsync();
            await db.Tenders.Where(t => t.TenderId == tenderId).ExecuteDeleteAsync();
            await db.PurchaseRequests.Where(r => r.PurchaseRequestId == request.PurchaseRequestId).ExecuteDeleteAsync();
            await cleanup.CommitAsync();
        }
    }
    private sealed class MemoryData : ITempDataProvider { public IDictionary<string,
        object> LoadTempData(HttpContext context) => new Dictionary<string,
        object>(); public void SaveTempData(HttpContext context,
        IDictionary<string,object> values) {} }
}
