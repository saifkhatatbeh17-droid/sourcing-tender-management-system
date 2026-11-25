using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace SourcingTenderManagement.Models.TopCommittee;

// Read models only. None of these types are EF entities or database snapshots.
public sealed class CommitteeTenderFilter
{
    [StringLength(200)] public string? Search { get; init; }
    [StringLength(40)] public string? Status { get; init; }
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
    [Range(1, 1000000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
}

public sealed record CommitteePage<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);

public sealed record CommitteeTenderRow(int TenderId, string Title, string Status, DateTime CreatedAt,
    int? NominatedBidId, string? NominatedSupplier, int RecordedApprovals, bool HasRejection,
    int OpenClarifications);

public sealed record CommitteeDashboard(int AssignedTenders, int AwaitingDecision, int AwardedTenders,
    int RejectedTenders, int OpenClarifications, IReadOnlyList<CommitteeTenderRow> PendingTenders,
    IReadOnlyList<CommitteeTenderRow> RecentDecisions, IReadOnlyList<CommitteeMemberProgress> VotingProgress)
{
    public SourcingTenderManagement.Models.DashboardOverview? Overview { get; init; }
}

public sealed record CommitteeMemberProgress(int TenderId, string UserId, string FullName, int Position,
    bool IsActive, bool HasCommitteeRole, string? RecordedDecision);

public sealed record CommitteeMember(int AssignmentId, string UserId, string FullName, int Position,
    bool IsActive, bool HasCommitteeRole)
{
    public bool IsEligible => IsActive && HasCommitteeRole;
}

public sealed record CommitteeVote(int ApprovalId, string UserId, int? AssignmentId,
    string MemberName, string Decision, string? Justification, DateTime DecidedAt);

public sealed record CommitteeManager(string UserId, string FullName, bool IsActive, bool HasManagerRole,
    DateTime ApprovedAt);

public sealed record CommitteeReadiness(bool HasFourEligibleMembers, bool HasValidNomination,
    bool HasResponsibleManager, bool HasRejection, bool IsVotingOpen, bool HasCurrentMemberVoted,
    bool CanCurrentMemberVote, string? BlockingReason);

public sealed record CommitteeDocument(int DocumentId, string OriginalFileName, string? DocumentType,
    long SizeBytes);

public sealed record CommitteeRequestItem(int ItemId, string Name, decimal Quantity, string Unit,
    string Specifications);

public sealed record CommitteePurchaseRequest(int RequestId, string Title, string Reason, string Department,
    string Priority, DateOnly TargetDate, IReadOnlyList<CommitteeRequestItem> Items);

public sealed record CommitteeBidItem(int RequestItemId, string ItemName, decimal Quantity,
    decimal UnitPrice, string OfferedSpecifications);

public sealed record CommitteeScore(string Criterion, string Category, decimal Weight, decimal Score,
    string? Notes)
{
    public decimal Maximum { get; init; } = 100;
}

public sealed record CommitteeEvaluation(string Evaluator, string Status, string? Notes,
    DateTime? SubmittedAt, DateTime? ApprovedAt, IReadOnlyList<CommitteeScore> Scores);

public sealed record CommitteeBid(int BidId, int SupplierId, string SupplierName, string Status,
    string? ComplianceStatus, string? ComplianceNotes, string Currency, decimal Subtotal,
    decimal TaxAmount, decimal AdditionalCosts, decimal Total, int DeliveryDays, string? PaymentTerms,
    DateOnly? ValidUntil, string? TechnicalDetails, DateTime? SubmittedAt, bool IsNominated,
    IReadOnlyList<CommitteeBidItem> Items, IReadOnlyList<CommitteeDocument> Documents,
    IReadOnlyList<CommitteeEvaluation> Evaluations);

public sealed record CommitteeReport(int ReportId, string Status, int? RecommendedBidId,
    string? CommitteeSummary, string? OfficerSummary, string? ManagerSummary, DateTime? SubmittedAt);

public sealed record CommitteeClarificationResponse(string RespondedByUserId, string RespondedByName,
    string Message, DateTime CreatedAt);

public sealed record CommitteeClarification(int ClarificationId, string Subject, string Description,
    string Status, DateTime CreatedAt, DateTime? ResolvedAt,
    IReadOnlyList<CommitteeClarificationResponse> Responses);

public sealed record CommitteeTenderDetails(int TenderId, string Title, string Description, string? Terms,
    string Status, DateTime OpensAt, DateTime ClosesAt, int? AwardedBidId, DateTime? AwardedAt,
    string? AwardNotes, CommitteePurchaseRequest PurchaseRequest, CommitteeReport? Report,
    CommitteeManager? ResponsibleManager, IReadOnlyList<CommitteeMember> Members,
    IReadOnlyList<CommitteeVote> Votes, IReadOnlyList<CommitteeBid> Bids,
    IReadOnlyList<CommitteeDocument> Documents, int OpenClarifications,
    IReadOnlyList<CommitteeClarification> MyClarifications, CommitteeReadiness Readiness)
{
    public string OfferRoundSummary { get; init; } = "";
    public string? TopEditState { get; init; }
    public string? TopEditRequestNotes { get; init; }
    public DateTime? TopEditRequestedAt { get; init; }
}

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

public sealed class CommitteeVoteInput : IValidatableObject
{
    [Range(1, int.MaxValue)] public int TenderId { get; set; }
    // A stale-page guard only. The bid to award always comes from the database nomination.
    [Range(1, int.MaxValue)] public int ExpectedNominatedBidId { get; set; }
    [Required, RegularExpression("^(Approved|Rejected|RequestEdit)$")]
    public string Decision { get; set; } = "";
    [StringLength(4000)] public string? Notes { get; set; }
    [Range(typeof(bool), "true", "true", ErrorMessage = "Confirm your final vote before submitting.")]
    public bool Confirmed { get; set; }
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (Decision is "Rejected" or "RequestEdit" && string.IsNullOrWhiteSpace(Notes))
            yield return new ValidationResult(Decision == "RequestEdit" ? "Describe the edits or clarification needed." : "Enter a rejection reason.", [nameof(Notes)]);
    }

}

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

public sealed class CommitteeReportFilter
{
 [StringLength(200)] public string? Search {get;set;}
 public string? Status {get;set;}
 public string? Decision {get;set;}
 public DateOnly? FromDate {get;set;}
 public DateOnly? ToDate {get;set;}
 [Range(1,1000000)] public int Page {get;set;}=1;
}
public sealed record CommitteeReportRow(int TenderId,string Title,string Status,DateTime CreatedAt,int? NominatedBidId,int? AwardedBidId,int Approvals,bool Rejected,string? MyDecision,DateTime? DecisionAt)
{
 public DateTime? CommitteeOutcomeAt { get; init; }
 public DateTime? AwardedAt { get; init; }
}
public sealed record CommitteeReportSummary(int Assigned,int Matching,int Pending,int Awarded,int Rejected,int Returned,int MyDecisions);
public sealed record CommitteeReportPage(CommitteeReportFilter Filter,CommitteeReportSummary Summary,IReadOnlyList<CommitteeReportRow> Rows,DateTime GeneratedAt)
{
 public CommitteeSystemStatistics? SystemStatistics { get; init; }
 public IReadOnlyList<CommitteeSupplierStatistics> Suppliers { get; init; } = [];
}
public sealed record CommitteeSystemStatistics(int Suppliers, int Tenders, int AwardedTenders)
{
    public IReadOnlyDictionary<string, int> SuppliersByStatus { get; init; } = new Dictionary<string, int>();
}
public sealed record CommitteeSupplierStatistics(int SupplierId, string SupplierName, int AwardedTenders)
{
    public string Status { get; init; } = "";
    public int Invited { get; init; }
    public int Dealings { get; init; }
    public decimal Frequency { get; init; }
    public decimal WinRate => Dealings == 0 ? 0 : (decimal)AwardedTenders / Dealings;
}

public sealed class CommitteeNotificationBellViewModel : NotificationBellViewModel
{
    public int Total { get; init; }
    public int Page { get; init; }
    public int Pages { get; init; }
    public string ReturnUrl { get; init; } = "";
    public string? PreviousUrl { get; init; }
    public string? NextUrl { get; init; }
    public bool OpenOnLoad { get; init; }
}

public sealed record CommitteeNotificationItem(int Id,string Title,string Message,bool IsRead,DateTime CreatedAt,int? TenderId);
public sealed record CommitteeNotificationPage(IReadOnlyList<CommitteeNotificationItem> Items,int Total,int Unread,int Page);
