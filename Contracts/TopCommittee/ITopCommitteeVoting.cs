using System.Security.Claims;
using SourcingTenderManagement.Models.TopCommittee;

namespace SourcingTenderManagement.Contracts.TopCommittee;

public interface ITopCommitteeVoting
{
    Task<CommitteeVoteResult> SubmitAsync(ClaimsPrincipal member, CommitteeVoteInput input, CancellationToken cancellationToken = default);
}

public interface ITopCommitteeVotingReadiness
{
    Task<bool> IsDatabaseReadyAsync(ClaimsPrincipal member, int tenderId, CancellationToken cancellationToken = default);
}

public sealed record CommitteeVoteResult(int TenderId, string Decision, bool IsAwarded);
