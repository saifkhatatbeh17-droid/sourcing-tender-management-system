using System.Security.Claims;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.TopCommittee;

namespace SourcingTenderManagement.Contracts.TopCommittee;

public interface ITopCommitteeClarifications
{
    Task<CommitteeClarificationResult> CreateAsync(ClaimsPrincipal actor, CommitteeCreateClarification input, CancellationToken cancellationToken = default);
    Task<CommitteeClarificationResult> ResolveAsync(ClaimsPrincipal actor, int clarificationId, CancellationToken cancellationToken = default);
}

public interface ITopCommitteeClarificationQueries
{
    Task<CommitteeClarificationsPage> GetPageAsync(ClaimsPrincipal actor, int? tenderId, CancellationToken cancellationToken = default);
}

// Manager team calls this from its own authorized, anti-forgery-protected action. No manager UI is added here.
public interface ITopCommitteeManagerAnswers
{
    Task<CommitteeClarificationResult> AnswerAsync(ClaimsPrincipal manager, int clarificationId, string answer, CancellationToken cancellationToken = default);
}

public interface ITopCommitteeVotingPause
{
    Task<CommitteeVotingPause> GetAsync(ClaimsPrincipal member, int tenderId, CancellationToken cancellationToken = default);
    // Phase 4 must use this INSIDE ITopCommitteeTenderTransaction, before inserting any vote.
    Task AssertNotPausedAsync(ApplicationDbContext lockedContext, int tenderId, CancellationToken cancellationToken = default);
}

public interface ITopCommitteeTenderTransaction
{
    Task<T> ExecuteAsync<T>(int tenderId, Func<ApplicationDbContext, CancellationToken, Task<T>> action, CancellationToken cancellationToken = default);
}

public sealed record CommitteeClarificationResult(int TenderId, int ClarificationId);
public sealed record CommitteeVotingPause(int OpenCount)
{
    public bool IsPaused => OpenCount > 0;
}

public sealed class CommitteeStateConflictException(string message) : Exception(message);
