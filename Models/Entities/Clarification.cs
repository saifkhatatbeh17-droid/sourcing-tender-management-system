using SourcingTenderManagement.Data;

namespace SourcingTenderManagement.Models.Entities;

// Database table: a clarification thread between two people about a request, tender or offer.
public class Clarification
{
    public int ClarificationId { get; set; }

    public string SenderUserId { get; set; } = null!;

    public string RecipientUserId { get; set; } = null!;

    public int? PurchaseRequestId { get; set; }

    public int? TenderId { get; set; }

    public int? BidId { get; set; }

    public string Subject { get; set; } = null!;

    public string Description { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime? ResolvedAt { get; set; }

    public Bid? Bid { get; set; }

    public ICollection<ClarificationResponse> ClarificationResponses { get; set; } = new List<ClarificationResponse>();

    public PurchaseRequest? PurchaseRequest { get; set; }

    public ApplicationUser RecipientUser { get; set; } = null!;

    public ApplicationUser SenderUser { get; set; } = null!;

    public Tender? Tender { get; set; }
}
