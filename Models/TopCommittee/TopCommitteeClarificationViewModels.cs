using System.ComponentModel.DataAnnotations;

namespace SourcingTenderManagement.Models.TopCommittee;

public sealed class CommitteeCreateClarification
{
    [Range(1, int.MaxValue)] public int TenderId { get; set; }
    [Required, StringLength(200)] public string Subject { get; set; } = "";
    [Required, StringLength(4000)] public string Description { get; set; } = "";
}

public sealed record CommitteeClarificationTender(int TenderId, string Title, string Status);
public sealed record CommitteeClarificationThread(int ClarificationId, int TenderId, string TenderTitle,
    string Subject, string Description, string ManagerName, string Status, DateTime CreatedAt,
    DateTime? ResolvedAt, bool CanResolve, IReadOnlyList<CommitteeClarificationResponse> Responses);
public sealed record CommitteeClarificationsPage(IReadOnlyList<CommitteeClarificationTender> Tenders,
    IReadOnlyList<CommitteeClarificationThread> Threads, CommitteeCreateClarification Create,
    string? ManagerName, string? CreateBlockedReason, int OpenCount, string? Error = null);
