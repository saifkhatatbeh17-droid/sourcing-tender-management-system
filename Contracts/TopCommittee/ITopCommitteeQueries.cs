using System.Security.Claims;
using SourcingTenderManagement.Models.TopCommittee;

namespace SourcingTenderManagement.Contracts.TopCommittee;

// Interface for loading the CEO & Chiefs pages, plus the errors for 'not allowed', 'tender not found' and integration problems.
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
