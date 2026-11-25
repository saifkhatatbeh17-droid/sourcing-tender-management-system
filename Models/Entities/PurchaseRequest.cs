using SourcingTenderManagement.Data;

namespace SourcingTenderManagement.Models.Entities;

public class PurchaseRequest
{
    public int PurchaseRequestId { get; set; }

    public int DepartmentId { get; set; }

    public string RequestedByUserId { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string Reason { get; set; } = null!;

    public string Priority { get; set; } = null!;

    public DateOnly TargetDate { get; set; }

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
