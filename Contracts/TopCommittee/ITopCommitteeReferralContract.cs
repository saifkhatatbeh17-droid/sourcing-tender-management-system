using System.Security.Claims;
using SourcingTenderManagement.Models.TopCommittee;

namespace SourcingTenderManagement.Contracts.TopCommittee;

/// <summary>
/// Read-only preflight for the future referral command. It does not save a referral, change a status,
/// create assignments or send notifications. The later command must revalidate inside its transaction.
/// </summary>
public interface ITopCommitteeReferralContract
{
    Task<CommitteeReferralPreparation> PrepareAsync(ClaimsPrincipal manager, int tenderId,
        CancellationToken cancellationToken = default);
}

// All identities, nominations and recommendation text are resolved from persisted records, not a form.
public sealed record CommitteeReferralPreparation(int TenderId, int NominatedBidId,
    int SupplierId, string SupplierName, string ManagerUserId, string Recommendation,
    int EvaluationReportId, IReadOnlyList<CommitteeMember> Members, bool IsAlreadyReferred);
