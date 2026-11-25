
namespace SourcingTenderManagement.Models.Entities;

public class BidItem
{
    public int BidItemId { get; set; }

    public int BidId { get; set; }

    public int PurchaseRequestItemId { get; set; }

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public string OfferedSpecifications { get; set; } = null!;

    public Bid Bid { get; set; } = null!;

    public PurchaseRequestItem PurchaseRequestItem { get; set; } = null!;
}
