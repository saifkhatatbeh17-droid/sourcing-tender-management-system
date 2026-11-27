using SourcingTenderManagement.Data;

namespace SourcingTenderManagement.Models.Entities;

// Database table: a supplier's offer (bid) for a tender, with its status and compliance review.
public class Bid
{
    public int BidId { get; set; }

    public int InvitationId { get; set; }

    public int TenderId { get; set; }

    public string Currency { get; set; } = null!;

    public int DeliveryDays { get; set; }

    public string? PaymentTerms { get; set; }

    public DateOnly? ValidUntil { get; set; }

    public string? TechnicalDetails { get; set; }

    public decimal TaxAmount { get; set; }

    public decimal AdditionalCosts { get; set; }

    public string Status { get; set; } = null!;

    public string? ComplianceStatus { get; set; }

    public string? ComplianceNotes { get; set; }

    public string? ComplianceReviewedByUserId { get; set; }

    public DateTime? ComplianceReviewedAt { get; set; }

    public DateTime? OfficerOpenedAt { get; set; }

    public DateTime? SubmittedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public ICollection<BidDocument> BidDocuments { get; set; } = new List<BidDocument>();

    public ICollection<BidItem> BidItems { get; set; } = new List<BidItem>();

    public ICollection<Clarification> Clarifications { get; set; } = new List<Clarification>();

    public ApplicationUser? ComplianceReviewedByUser { get; set; }

    public ICollection<EvaluationReport> EvaluationReports { get; set; } = new List<EvaluationReport>();

    public ICollection<Evaluation> Evaluations { get; set; } = new List<Evaluation>();

    public Invitation Invitation { get; set; } = null!;

    public ICollection<Tender> Tenders { get; set; } = new List<Tender>();
}
