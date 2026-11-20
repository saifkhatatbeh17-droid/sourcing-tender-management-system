using System.ComponentModel.DataAnnotations;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Models;

public class SupplierDashboardViewModel
{
    public string CompanyName { get; set; } = "";
    public string VerificationStatus { get; set; } = "Pending";
    public string? ReviewNotes { get; set; }
    public int TotalInvitations { get; set; }
    public int PendingInvitations { get; set; }
    public int AcceptedInvitations { get; set; }
    public int SubmittedOffers { get; set; }
    public int AcceptedOffers { get; set; }
    public List<Invitation> RecentInvitations { get; set; } = [];
    public List<Bid> RecentOffers { get; set; } = [];
    public List<Notification> Notifications { get; set; } = [];
}

public class OfferFormViewModel
{
    public int InvitationId { get; set; }
    public int TenderId { get; set; }
    public int? BidId { get; set; }
    public string TenderTitle { get; set; } = "";
    public DateTime ClosesAt { get; set; }
    [Required, StringLength(3, MinimumLength = 3)] public string Currency { get; set; } = "JOD";
    [Range(0, 3650)] public int DeliveryDays { get; set; }
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever] public decimal TaxAmount { get; set; }
    // Fixed rate shown on the form; never read from the request, so a posted value cannot change it.
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever] public decimal TaxPercentage { get; set; } = OfferTax.RatePercent;
    [Range(typeof(decimal), "0", "9999999999999999.99")] public decimal AdditionalCosts { get; set; }
    [StringLength(1000)] public string? PaymentTerms { get; set; }
    public DateOnly? ValidUntil { get; set; }
    [StringLength(4000)] public string? TechnicalDetails { get; set; }
    [MinLength(1, ErrorMessage = "The tender contains no requested items.")] public List<OfferLineViewModel> Items { get; set; } = [];
}

public class OfferLineViewModel
{
    public int PurchaseRequestItemId { get; set; }
    public string ItemName { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal RequestedQuantity { get; set; }
    public string RequestedSpecifications { get; set; } = "";
    [Range(typeof(decimal), "0.001", "999999999999999.999")] public decimal Quantity { get; set; }
    [Range(typeof(decimal), "0", "9999999999999999.99")] public decimal UnitPrice { get; set; }
    [Required, StringLength(4000)] public string? Specifications { get; set; }
}
