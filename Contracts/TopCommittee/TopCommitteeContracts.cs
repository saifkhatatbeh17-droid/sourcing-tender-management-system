using System.Security.Claims;
using SourcingTenderManagement.Models.TopCommittee;
using SourcingTenderManagement.Data;

namespace SourcingTenderManagement.Contracts.TopCommittee;

public interface ITopCommitteeQueries
{
    Task<CommitteeDashboard> GetDashboardAsync(ClaimsPrincipal actor, CancellationToken cancellationToken = default);
    Task<CommitteePage<CommitteeTenderRow>> GetTendersAsync(ClaimsPrincipal actor,
        CommitteeTenderFilter filter, CancellationToken cancellationToken = default);
    Task<CommitteeTenderDetails> GetTenderDetailsAsync(ClaimsPrincipal actor, int tenderId,
        CancellationToken cancellationToken = default);
}

public sealed class CommitteeAccessDeniedException() : Exception("Top Committee access is not permitted.");

// Do not reveal whether an inaccessible tender exists.
public sealed class CommitteeTenderNotFoundException() : Exception("Tender is not available.");

public sealed class CommitteeIntegrationException(string message) : Exception(message);

public interface ITopCommitteeVoting
{
    Task<CommitteeVoteResult> SubmitAsync(ClaimsPrincipal member, CommitteeVoteInput input, CancellationToken cancellationToken = default);
}

public interface ITopCommitteeVotingReadiness
{
    Task<bool> IsDatabaseReadyAsync(ClaimsPrincipal member, int tenderId, CancellationToken cancellationToken = default);
}

public sealed record CommitteeVoteResult(int TenderId, string Decision, bool IsAwarded);

public interface ITopCommitteeClarifications
{
    Task<CommitteeClarificationResult> CreateAsync(ClaimsPrincipal actor, CommitteeCreateClarification input, CancellationToken cancellationToken = default);
    Task<CommitteeClarificationResult> FollowUpAsync(ClaimsPrincipal actor, int clarificationId, string message, CancellationToken cancellationToken = default);
    Task<CommitteeClarificationResult> ResolveAsync(ClaimsPrincipal actor, int clarificationId, CancellationToken cancellationToken = default);
}

public interface ITopCommitteeClarificationQueries
{
    Task<CommitteeClarificationsPage> GetPageAsync(ClaimsPrincipal actor, int? tenderId, CancellationToken cancellationToken = default);
}

// Manager team calls this from its own authorized, anti-forgery-protected action. No manager UI is added here.
public interface ITopCommitteeManagerAnswers
{
    Task<CommitteeClarificationResult> ConfirmResolutionAsync(ClaimsPrincipal manager, int clarificationId, CancellationToken cancellationToken = default);
    Task<CommitteeClarificationResult> AnswerAsync(ClaimsPrincipal manager, int clarificationId, string answer, CancellationToken cancellationToken = default);
}

public interface ITopCommitteeVotingPause
{
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
