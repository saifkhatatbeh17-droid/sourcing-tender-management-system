using System.ComponentModel.DataAnnotations;
namespace SourcingTenderManagement.Models.TopCommittee;

public sealed class CommitteeHistoryFilter
{
    [StringLength(200)] public string? Search { get; set; }
    [StringLength(40)] public string? Status { get; set; }
    [StringLength(20)] public string? Decision { get; set; }
    [Range(1, 1000000)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 20;
}

public sealed record CommitteeHistoryRow(int VoteId, int TenderId, string Title, string Status,
    string Decision, DateTime DecidedAt, int? NominatedBidId, int? AwardedBidId,
    int Approvals, bool HasRejection, DateTime? AwardedAt)
{
    public string Outcome => HasRejection ? "Rejected — voting permanently ended"
        : Status == "Awarded" && AwardedBidId.HasValue && AwardedAt.HasValue ? "Awarded"
        : Status == "AwaitingCommitteeDecision" ? "Pending collective decision"
        : "No final committee outcome recorded";
}
public sealed record CommitteeHistoryPage(CommitteeHistoryFilter Filter, IReadOnlyList<CommitteeHistoryRow> Rows, int Total);
