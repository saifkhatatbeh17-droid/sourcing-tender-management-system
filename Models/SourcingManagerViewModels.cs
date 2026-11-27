using System.ComponentModel.DataAnnotations;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Models;

// Data shown on the Sourcing Manager pages (dashboard, review, reports, clarifications).
public record ManagerDashboard(int AwaitingReview, int Approved, int AwaitingCommittee, int PendingClarifications);
public record ManagerReviewPage(
    IReadOnlyList<EvaluationReport> Reports,
    EvaluationReport? Selected,
    IReadOnlyList<Bid> Offers,
    DateTime? ReviewVersion);

public record ManagerReportPage(IReadOnlyList<TenderApproval> Decisions, string? Search, string? Decision);

public record ManagerClarificationPage(
    IReadOnlyList<Tender> Tenders,
    int? SelectedTenderId,
    IReadOnlyList<CommitteeAssignment> Recipients,
    IReadOnlyList<Clarification> Conversations,
    Clarification? Selected,
    string UserId,
    bool CanReply);

public sealed class ManagerDecisionInput
{
    [Range(1, int.MaxValue)] public int ReportId { get; set; }
    [Required] public string Decision { get; set; } = "";
    [StringLength(4000)] public string? Notes { get; set; }
    public long? ReviewVersion { get; set; }
}

public sealed class ManagerForwardInput
{
    [Range(1, int.MaxValue)] public int ReportId { get; set; }
    [Required, StringLength(4000)] public string Reason { get; set; } = "";
}

public sealed class ManagerClarificationInput
{
    [Range(1, int.MaxValue)] public int TenderId { get; set; }
    [Required, StringLength(450)] public string RecipientId { get; set; } = "";
    [Required, StringLength(200)] public string Subject { get; set; } = "";
    [Required, StringLength(4000)] public string Description { get; set; } = "";
}

public sealed class ManagerClarificationReply
{
    [Range(1, int.MaxValue)] public int Id { get; set; }
    [Required, StringLength(4000)] public string Message { get; set; } = "";
}
