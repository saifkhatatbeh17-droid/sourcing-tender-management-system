using SourcingTenderManagement.Data;

namespace SourcingTenderManagement.Models.Entities;

// Database table: a supplier company's profile and approval status.
public class SupplierProfile
{
    public int SupplierProfileId { get; set; }

    public string UserId { get; set; } = null!;

    public string? Category { get; set; }
    public string CompanyName { get; set; } = null!;

    public string RegistrationNumber { get; set; } = null!;

    public string? TaxNumber { get; set; }

    public string ContactName { get; set; } = null!;

    public string? Address { get; set; }

    public string Status { get; set; } = null!;

    public string? ReviewNotes { get; set; }

    public string? ReviewedByUserId { get; set; }

    public DateTime? ReviewedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public ICollection<Invitation> Invitations { get; set; } = new List<Invitation>();

    public ApplicationUser? ReviewedByUser { get; set; }

    public ICollection<SupplierDocument> SupplierDocuments { get; set; } = new List<SupplierDocument>();

    public ApplicationUser User { get; set; } = null!;
}
