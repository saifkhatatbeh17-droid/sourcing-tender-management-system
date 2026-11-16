using System.ComponentModel.DataAnnotations;

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
{ public string OfferRoundSummary { get; init; } = ""; }
