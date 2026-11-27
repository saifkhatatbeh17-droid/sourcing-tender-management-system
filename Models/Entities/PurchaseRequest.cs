using SourcingTenderManagement.Data;

namespace SourcingTenderManagement.Models.Entities;

// Database table: a department's purchase request (what they need, why, and its status).
public class PurchaseRequest
{
    public int PurchaseRequestId { get; set; }

    public int DepartmentId { get; set; }

    public string RequestedByUserId { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string Reason { get; set; } = null!;

    public string Priority { get; set; } = null!;

    public DateOnly TargetDate { get; set; }

    /// <summary>Requester's cost estimate in the company currency.</summary>
    public decimal? EstimatedBudget { get; set; }

    /// <summary>What happens to operations if this purchase is late or not made.</summary>
    public string? BusinessImpact { get; set; }

    /// <summary>Estimated cost per day of delay, in the company currency.</summary>
    public decimal? DelayCostPerDay { get; set; }

    public string Status { get; set; } = null!;

    public string? ReviewNotes { get; set; }

    public string? ReviewedByUserId { get; set; }

    public DateTime? ReviewedAt { get; set; }

    public DateTime? SubmittedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public ICollection<Clarification> Clarifications { get; set; } = new List<Clarification>();

    public Department Department { get; set; } = null!;

    public ICollection<PurchaseRequestItem> PurchaseRequestItems { get; set; } = new List<PurchaseRequestItem>();

    public ApplicationUser RequestedByUser { get; set; } = null!;

    public ApplicationUser? ReviewedByUser { get; set; }

    public ICollection<Tender> Tenders { get; set; } = new List<Tender>();
}
