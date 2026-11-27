namespace SourcingTenderManagement.Models.Entities;

// Database table: one item in a purchase request (name, quantity, unit, specification).
public class PurchaseRequestItem
{
    public int PurchaseRequestItemId { get; set; }

    public int PurchaseRequestId { get; set; }

    public string ItemName { get; set; } = null!;

    public decimal Quantity { get; set; }

    public string Unit { get; set; } = null!;

    public string Specifications { get; set; } = null!;

    /// <summary>Minimum legal warranty, in months, the department asks suppliers for on this item. Null means none required.</summary>
    public int? LegalWarrantyMonths { get; set; }

    public ICollection<BidItem> BidItems { get; set; } = new List<BidItem>();

    public PurchaseRequest PurchaseRequest { get; set; } = null!;
}
