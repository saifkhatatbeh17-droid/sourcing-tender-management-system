using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Contracts.TopCommittee;
using SourcingTenderManagement.Data;

namespace SourcingTenderManagement.Services.TopCommittee;

public sealed class TopCommitteeVotingPause : ITopCommitteeVotingPause
{
    public async Task AssertNotPausedAsync(ApplicationDbContext lockedContext, int tenderId, CancellationToken cancellationToken = default)
    {
        await TopCommitteeTenderTransaction.RequireLockAsync(lockedContext, tenderId, cancellationToken);
        if ((await ReadAsync(lockedContext, tenderId, cancellationToken)).IsPaused)
            throw new CommitteeStateConflictException("Voting is paused until all relevant clarifications are resolved.");
    }

    internal static async Task<CommitteeVotingPause> ReadAsync(ApplicationDbContext db, int tenderId, CancellationToken cancellationToken) =>
        new(await TopCommitteeClarificationScope.Query(db).CountAsync(c => c.TenderId == tenderId &&
            (c.Status == "Pending" || c.Status == "Answered"), cancellationToken));
}
