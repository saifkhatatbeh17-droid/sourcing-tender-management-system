using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Contracts.TopCommittee;
using SourcingTenderManagement.Data;

namespace SourcingTenderManagement.Services.TopCommittee;

public sealed class TopCommitteeReferralContract(ApplicationDbContext db, TopCommitteeAccessService access)
    : ITopCommitteeReferralContract
{
    public async Task<CommitteeReferralPreparation> PrepareAsync(ClaimsPrincipal manager, int tenderId,
        CancellationToken cancellationToken = default)
    {
        var managerId = await access.RequireRoleAsync(manager, RoleNames.SourcingManager, cancellationToken);
        var managers = await access.GetManagersAsync(tenderId, cancellationToken);
        if (managers.Count != 1 || managers[0].UserId != managerId || !managers[0].IsActive || !managers[0].HasManagerRole)
            throw new CommitteeIntegrationException("Referral requires one recorded Manager approval owned by the authenticated manager.");

        var tender = await db.Tenders.AsNoTracking().Include(t => t.EvaluationReport)
            .Include(t => t.TenderApprovals).SingleOrDefaultAsync(t => t.TenderId == tenderId, cancellationToken)
            ?? throw new CommitteeIntegrationException("Tender is not available for referral.");
        if (tender.TenderApprovals.Any(a => a.Stage == "Top"))
            throw new CommitteeIntegrationException("A tender with an existing committee vote cannot be referred again.");
        if (tender.Status is not ("AwaitingManagerReview" or "AwaitingCommitteeDecision") ||
            tender.AwardedBidId.HasValue || tender.AwardedAt.HasValue)
            throw new CommitteeIntegrationException("Tender is not in a valid referral state.");
        if (tender.EvaluationReport is not { RecommendedBidId: { } bidId } report ||
            string.IsNullOrWhiteSpace(report.ManagerSummary))
            throw new CommitteeIntegrationException("Persist the nominated bid and manager recommendation on the evaluation report first.");

        var nomination = await db.Bids.AsNoTracking().Include(b => b.Invitation).ThenInclude(i => i.SupplierProfile)
            .SingleOrDefaultAsync(b => b.BidId == bidId && b.TenderId == tenderId, cancellationToken);
        if (!TopCommitteeRules.IsEligibleNomination(nomination, tenderId))
            throw new CommitteeIntegrationException("The nomination must be a submitted, eligible bid belonging to this tender.");
        var members = await access.GetMembersAsync(tenderId, cancellationToken);
        if (!TopCommitteeRules.HasFourMembers(members))
            throw new CommitteeIntegrationException("Assign exactly four distinct, active CEO & Chiefs members before referral.");

        return new CommitteeReferralPreparation(tenderId, nomination!.BidId, nomination.Invitation.SupplierProfileId,
            nomination.Invitation.SupplierProfile.CompanyName, managerId, report.ManagerSummary,
            report.EvaluationReportId, members, tender.Status == "AwaitingCommitteeDecision");
    }
}
