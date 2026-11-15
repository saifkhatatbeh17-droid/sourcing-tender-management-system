namespace SourcingTenderManagement.Models;

public class SourcingOfficerTenderForm : ProcurementCategoryInput
{
    public int TenderId { get; set; }
    public int PurchaseRequestId { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string? Terms { get; set; }
    public DateTime OpensAt { get; set; }
    public DateTime ClosesAt { get; set; }
}
