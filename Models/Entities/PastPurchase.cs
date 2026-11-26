namespace SourcingTenderManagement.Models.Entities;

/// <summary>A completed purchase kept as a price and delivery benchmark.</summary>
public class PastPurchase
{
    public int PastPurchaseId { get; set; }

    public int DepartmentId { get; set; }

    public string ItemName { get; set; } = null!;

    public string? Category { get; set; }

    public string SupplierName { get; set; } = null!;

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal TotalAmount { get; set; }

    public string Currency { get; set; } = null!;

    public DateOnly PurchasedOn { get; set; }

    public int? PromisedDeliveryDays { get; set; }

    public int? ActualDeliveryDays { get; set; }

    public string? Notes { get; set; }

    public Department Department { get; set; } = null!;
}
