using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SourcingTenderManagement.Contracts.TopCommittee;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Models.TopCommittee;
using SourcingTenderManagement.Services.TopCommittee;

namespace SourcingTenderManagement.Checks;

// Dedicated read-only checks. No fixtures are inserted, no roles are seeded, no migrations are run.
public static class TopCommitteeFoundationChecks
{
    public static async Task RunAsync(IServiceProvider services)
    {
        var passed = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            passed++;
        }
        async Task Rejects<T>(Func<Task> action, string message) where T : Exception
        {
            try { await action(); }
            catch (T) { passed++; return; }
            throw new InvalidOperationException(message);
        }

        var members = Enumerable.Range(1, 4)
            .Select(p => new CommitteeMember(p, $"member-{p}", $"Member {p}", p, true, true))
            .ToList();
        var managers = new List<CommitteeManager> { new("manager", "Manager", true, true, DateTime.UtcNow) };
        var nomination = new Bid { BidId = 10, TenderId = 1, Status = "Compliant", ComplianceStatus = "Compliant", SubmittedAt = DateTime.UtcNow };
        var tender = new Tender { TenderId = 1, Status = "AwaitingCommitteeDecision", Title = "Synthetic in-memory tender",
            Description = "Not persisted", EvaluationReport = new EvaluationReport { EvaluationReportId = 1, TenderId = 1,
                RecommendedBidId = 10, ManagerSummary = "Existing manager recommendation", Status = "ManagerApproved" } };
        CommitteeReadiness State(int open = 0) => TopCommitteeRules.Inspect(tender, members, managers, nomination, open, "member-1");

        Check(State().CanCurrentMemberVote, "A valid assigned member should be eligible in a referred tender.");
        members.RemoveAt(3);
        Check(!State().HasFourEligibleMembers && !State().CanCurrentMemberVote, "Three members must not open voting.");
        members.Add(new(4, "member-4", "Member 4", 4, false, true));
        Check(!State().HasFourEligibleMembers, "Inactive committee member must block readiness.");
        members[3] = members[3] with { IsActive = true, HasCommitteeRole = false };
        Check(!State().HasFourEligibleMembers, "Missing database committee role must block readiness.");
        members[3] = members[3] with { HasCommitteeRole = true, UserId = "member-1" };
        Check(!State().HasFourEligibleMembers, "Duplicate people must not count as four voters.");
        members[3] = members[3] with { UserId = "member-4", Position = 1 };
        Check(!State().HasFourEligibleMembers, "Duplicate positions must not form a valid roster.");
        members[3] = members[3] with { Position = 4 };
        Check(!State(1).CanCurrentMemberVote
            && State(1).IsVotingOpen == false, "Any open clarification pauses everyone.");
        var ownApproval = new TenderApproval { TenderApprovalId = 1, TenderId = 1, Stage = "Top", Decision = "Approved",
            UserId = "member-1", CommitteeAssignmentId = 1, User = new ApplicationUser { FullName = "Member 1" } };
        tender.TenderApprovals.Add(ownApproval);
        Check(State().HasCurrentMemberVoted
            && !State().CanCurrentMemberVote, "Final vote cannot become eligible again.");
        Check(State(1).HasCurrentMemberVoted, "Pausing must preserve recorded votes.");
        tender.TenderApprovals.Remove(ownApproval);
        var rejection = new TenderApproval { Stage = "Top", Decision = "Rejected", UserId = "member-2" };
        tender.TenderApprovals.Add(rejection);
        Check(State().HasRejection
            && !State().IsVotingOpen, "Historical rejection blocks re-entry even if tender status is changed.");
        tender.TenderApprovals.Clear();
        tender.Status = "AwaitingManagerReview";
        Check(!State().IsVotingOpen, "Manager review is not a committee voting state.");
        tender.Status = "AwaitingCommitteeDecision";
        tender.AwardedBidId = 10;
        Check(!State().IsVotingOpen, "A recorded award must block readiness.");
        tender.AwardedBidId = null;
        nomination.TenderId = 2;
        Check(!State().HasValidNomination, "Cross-tender nomination must fail.");
        nomination.TenderId = 1;
        foreach (var invalid in new[] { "Draft", "Withdrawn", "Rejected", "NonCompliant" })
        {
            nomination.Status = invalid;
            Check(!State().HasValidNomination, $"{invalid} nomination must fail.");
        }
        nomination.Status = "Compliant";
        nomination.SubmittedAt = null;
        Check(!State().HasValidNomination, "An offer without submission evidence cannot be nominated.");
        nomination.SubmittedAt = DateTime.UtcNow;
        managers.Add(managers[0] with { UserId = "another-manager" });
        Check(!State().HasResponsibleManager, "Ambiguous responsible managers must not be guessed.");
        managers.RemoveAt(1);
        tender.EvaluationReport!.ManagerSummary = " ";
        Check(!State().IsVotingOpen, "Blank manager recommendation must fail.");
        tender.EvaluationReport.ManagerSummary = "Verified recommendation";
        Check(!TopCommitteeRules.Inspect(tender, members, managers, nomination, 0, "outsider").CanCurrentMemberVote,
            "An unassigned user must never receive voting eligibility.");

        // Exercise the success-path read projection without persisting invented tender data.
        var item = new PurchaseRequestItem { PurchaseRequestItemId = 5, ItemName = "Computer", Quantity = 2, Unit = "Each", Specifications = "Specification" };
        tender.PurchaseRequest = new PurchaseRequest { PurchaseRequestId = 3, Title = "Request", Reason = "Reason", Priority = "Normal",
            Department = new Department { Name = "IT" }, PurchaseRequestItems = new List<PurchaseRequestItem> { item } };
        var invitation = new Invitation { InvitationId = 7, TenderId = 1, SupplierProfileId = 8,
            SupplierProfile = new SupplierProfile { CompanyName = "Synthetic supplier" } };
        nomination.Currency = "USD"; nomination.TaxAmount = 10; nomination.AdditionalCosts = 5;
        nomination.BidItems.Add(new BidItem { PurchaseRequestItemId = 5,
            PurchaseRequestItem = item,
            Quantity = 2,
            UnitPrice = 100,
            OfferedSpecifications = "Offered" });
        nomination.BidDocuments.Add(new BidDocument { BidDocumentId = 1,
            OriginalFileName = "offer.pdf",
            StoredFileName = "private-storage-secret",
            DocumentType = "Technical",
            FileSizeBytes = 100 });
        nomination.Evaluations.Add(new Evaluation { Status = "Approved", CommitteeAssignment = new CommitteeAssignment { User = new ApplicationUser { FullName = "Evaluator" } },
            EvaluationScores = new List<EvaluationScore> { new() { Score = 80, EvaluationCriterion = new EvaluationCriterion { Name = "Technical", Category = "Requirements", Weight = 40 } } } });
        nomination.Evaluations.Add(new Evaluation { Status = "Draft" });
        invitation.Bids.Add(nomination);
        invitation.Bids.Add(new Bid { BidId = 11, TenderId = 1, Status = "Withdrawn", SubmittedAt = DateTime.UtcNow, Currency = "JOD" });
        invitation.Bids.Add(new Bid { BidId = 12, TenderId = 1, Status = "Draft" });
        tender.Invitations.Add(invitation);
        tender.TenderApprovals.Add(ownApproval);
        var privateQuestion = new Clarification { ClarificationId = 1, TenderId = 1, SenderUserId = "member-2", Subject = "Other member private question" };
        var model = TopCommitteeQueryService.MapDetails(tender, members, managers, new[] { privateQuestion }, 1, "member-1");
        Check(model.Bids.Count == 2
            && model.Bids.Any(b => b.Status == "Withdrawn"), "Submitted history must survive lifecycle status changes; drafts stay private.");
        Check(model.Bids.Single(b => b.IsNominated).Total == 215m, "Item subtotal, taxes and additional costs must agree.");
        Check(model.Bids[0].Evaluations.Count == 1
            && model.Bids[0].Evaluations[0].Scores[0].Score == 80m, "Read outcomes without inventing weighted evaluation totals or publishing drafts.");
        Check(model.OpenClarifications == 1
            && model.MyClarifications.Count == 0, "Pause count must not disclose another member's conversation.");
        Check(!JsonSerializer.Serialize(model)
            .Contains("private-storage-secret", StringComparison.Ordinal), "Read models must not expose stored file paths.");
        Check(model.Votes.Count == 1
            && model.Votes[0].UserId == "member-1", "Recorded committee votes must remain visible.");

        _ = services.GetRequiredService<ITopCommitteeQueries>();
        _ = services.GetRequiredService<ITopCommitteeReferralContract>();
        var configured = services.GetRequiredService<ApplicationDbContext>();
        Check(!configured.Database.HasPendingModelChanges(), "Phase 1 must not change the EF database model.");
        var readOnly = new ReadOnlyCommandGuard();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(configured.Database.GetConnectionString()).AddInterceptors(readOnly).Options;
        await using var db = new ApplicationDbContext(options);
        var access = new TopCommitteeAccessService(db);
        var queries = new TopCommitteeQueryService(db, access);
        var referral = new TopCommitteeReferralContract(db, access);
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
        await Rejects<CommitteeAccessDeniedException>(() => queries.GetDashboardAsync(anonymous), "Anonymous access must fail.");
        await Rejects<CommitteeAccessDeniedException>(() => queries.GetDashboardAsync(Principal("missing-user", RoleNames.TopCommittee)), "Cookie claims alone must not authorize access.");

        var memberId = await db.Users.Where(u => u.IsActive && db.UserRoles.Any(ur => ur.UserId == u.Id &&
            db.Roles.Any(r => r.Id == ur.RoleId && r.Name == RoleNames.TopCommittee))).Select(u => u.Id).FirstOrDefaultAsync();
        if (memberId == null) throw new InvalidOperationException("A pre-existing active Top Committee account is required for read-only integration checks. No account was created.");
        var principal = Principal(memberId, RoleNames.TopCommittee);
        await Rejects<CommitteeAccessDeniedException>(() => queries.GetDashboardAsync(Principal(memberId, RoleNames.Supplier)), "A missing committee claim must fail.");
        await Rejects<ValidationException>(() => queries.GetTendersAsync(principal, new CommitteeTenderFilter { PageSize = 101 }), "Invalid paging must fail.");
        await Rejects<ValidationException>(() => queries.GetTendersAsync(principal, new CommitteeTenderFilter { Status = "Invented" }), "Unknown status must fail.");
        await Rejects<ValidationException>(() => queries.GetTendersAsync(principal, new CommitteeTenderFilter { FromDate = new(2030,
            2,
            1), ToDate = new(2030,
            1,
            1) }), "Reversed dates must fail.");
        var dashboard = await queries.GetDashboardAsync(principal);
        var page = await queries.GetTendersAsync(principal, new CommitteeTenderFilter { Search = "read-only-check",
            FromDate = new(2000, 1, 1),
            ToDate = new(2099, 1, 1) });
        Check(dashboard.AssignedTenders >= 0
            && page.Items.Count <= page.PageSize, "Live SQL dashboard and paged/filter projections must execute.");
        var emptyDetails = await queries.DetailsQuery(memberId, -1).SingleOrDefaultAsync();
        Check(emptyDetails == null, "The full assigned-details SELECT must translate and execute without seeded fixtures.");
        await Rejects<CommitteeTenderNotFoundException>(() => queries.GetTenderDetailsAsync(principal, -1), "Invalid tender must not disclose records.");
        var unassignedId = await db.Tenders.Where(t => !t.CommitteeAssignments.Any(a => a.UserId == memberId
            && a.CommitteeType == "Top"))
            .Select(t => (int?)t.TenderId).FirstOrDefaultAsync() ?? int.MaxValue;
        await Rejects<CommitteeTenderNotFoundException>(() => queries.GetTenderDetailsAsync(principal, unassignedId), "Unassigned and missing tenders must be indistinguishable.");
        var nonMemberId = await db.Users.Where(u => u.IsActive && !db.UserRoles.Any(ur => ur.UserId == u.Id &&
            db.Roles.Any(r => r.Id == ur.RoleId && r.Name == RoleNames.TopCommittee))).Select(u => u.Id).FirstOrDefaultAsync();
        if (nonMemberId != null)
            await Rejects<CommitteeAccessDeniedException>(() => queries.GetDashboardAsync(Principal(nonMemberId, RoleNames.TopCommittee)), "Forged/stale role claim must fail against database membership.");
        await Rejects<CommitteeAccessDeniedException>(() => referral.PrepareAsync(principal, int.MaxValue), "Committee member must not impersonate a referring manager.");
        var managerId = await db.Users.Where(u => u.IsActive && db.UserRoles.Any(ur => ur.UserId == u.Id &&
            db.Roles.Any(r => r.Id == ur.RoleId && r.Name == RoleNames.SourcingManager))).Select(u => u.Id).FirstOrDefaultAsync();
        if (managerId != null)
            await Rejects<CommitteeIntegrationException>(() => referral.PrepareAsync(Principal(managerId, RoleNames.SourcingManager), -1), "A manager cannot refer an unapproved/missing tender.");

        var assignedId = await db.CommitteeAssignments.Where(a => a.CommitteeType == "Top" && a.UserId == memberId)
            .Select(a => (int?)a.TenderId).FirstOrDefaultAsync();
        if (assignedId.HasValue)
        {
            var liveDetails = await queries.GetTenderDetailsAsync(principal, assignedId.Value);
            Check(liveDetails.TenderId == assignedId.Value, "Live assigned tender details must load.");
        }
        else Console.WriteLine("NOTE: No assigned tender exists; successful details projection tested in memory, not with persisted fixtures.");
        Check(!db.ChangeTracker.HasChanges()
            && readOnly.ReadCommands > 0, "Integration checks must execute SELECTs only.");
        Console.WriteLine($"PASS: {passed} Top Committee foundation checks; {readOnly.ReadCommands} guarded SQL SELECTs; no data, role, schema or migration writes.");
        await TopCommitteePhase2Checks.RunAsync(services);
        await TopCommitteePhase3Checks.RunAsync(services);
        await TopCommitteePhase4Checks.RunAsync(services);
        await TopCommitteePhase5Checks.RunAsync(services);
        await TopCommitteePhase6Checks.RunAsync(services);
        await TopCommitteePhase7Checks.RunAsync(services);
        await TopCommitteeNotificationNavigationChecks.RunAsync(services);
    }

    private static ClaimsPrincipal Principal(string userId, string role) => new(new ClaimsIdentity(
        new[] { new Claim(ClaimTypes.NameIdentifier, userId), new Claim(ClaimTypes.Role, role) }, "FoundationChecks"));

    private sealed class ReadOnlyCommandGuard : DbCommandInterceptor
    {
        public int ReadCommands { get; private set; }
        private void RequireSelect(DbCommand command)
        {
            if (!command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Foundation checks prohibit non-SELECT SQL commands.");
            ReadCommands++;
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { RequireSelect(command); return ValueTask.FromResult(result); }
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result)
        { RequireSelect(command); return result; }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Foundation checks prohibit database writes.");
        public override InterceptionResult<int> NonQueryExecuting(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result) =>
            throw new InvalidOperationException("Foundation checks prohibit database writes.");
        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
        { RequireSelect(command); return ValueTask.FromResult(result); }
        public override InterceptionResult<object> ScalarExecuting(DbCommand command,
            CommandEventData eventData, InterceptionResult<object> result)
        { RequireSelect(command); return result; }
    }
}
