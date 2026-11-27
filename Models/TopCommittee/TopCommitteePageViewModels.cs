using System.Globalization;

namespace SourcingTenderManagement.Models.TopCommittee;

// Data shown on the CEO & Chiefs pages, plus small display helpers (status names, money format).
public sealed record CommitteePendingPage(CommitteePage<CommitteeTenderRow> Tenders, CommitteeTenderFilter Filter);
public sealed record CommitteeTenderPage(CommitteeTenderDetails Tender, CommitteeBid? SelectedBid,
    CommitteeVoteInput? Vote = null, bool VotingDatabaseReady = false);

public static class CommitteeDisplay
{
    public static string Status(string? status) => status switch
    {
        "AwaitingCommitteeDecision" => "Awaiting committee decision",
        "AwaitingManagerReview" => "Awaiting manager review",
        "AwaitingOfficerSummary" => "Awaiting officer summary",
        "UnderEvaluation" => "Under evaluation",
        "UnderReview" => "Under review",
        "NonCompliant" => "Non-compliant",
        "PendingClarification" => "Pending clarification",
        "ManagerApproved" => "Manager approved",
        "ManagerRejected" => "Manager rejected",
        "CommitteeApproved" => "Committee approved",
        "TopApproved" => "Top approved",
        "OfficerSubmitted" => "Officer submitted",
        "ReturnedForReview" => "Returned for review",
        null or "" => "Not recorded",
        _ => status
    };

    public static string Text(string? value) => string.IsNullOrWhiteSpace(value) ? "Not provided" : value;
    public static string Money(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);
    public static string Quantity(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    public static string Utc(DateTime? value) => value.HasValue
        ? value.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC" : "Not recorded";
    public static string Date(DateOnly? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "Not provided";
}
