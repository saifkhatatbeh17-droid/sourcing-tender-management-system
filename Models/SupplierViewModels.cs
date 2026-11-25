using System.ComponentModel.DataAnnotations;
using SourcingTenderManagement.Models.Entities;
using Microsoft.AspNetCore.Mvc.ModelBinding;

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
    public GuaranteeInput LegalWarranty { get; set; } = new();
    public GuaranteeInput BankGuarantee { get; set; } = new();
    public int InvitationId { get; set; }
    public int TenderId { get; set; }
    public int? BidId { get; set; }
    public string TenderTitle { get; set; } = "";
    public DateTime ClosesAt { get; set; }
    [Required, RegularExpression(@"^[A-Za-z]{3}$", ErrorMessage = "Use a 3-letter currency code.")] public string Currency { get; set; } = "JOD";
    [Range(0, 3650)] public int DeliveryDays { get; set; }
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever] public decimal TaxAmount { get; set; }
    public bool ApplyTax { get; set; } = true;
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever] public decimal TaxPercentage { get; set; } = OfferTax.RatePercent;
    [Range(typeof(decimal), "0", "9999999999999999.99")] public decimal AdditionalCosts { get; set; }
    [StringLength(1000)] public string? PaymentTerms { get; set; }
    public DateOnly? ValidUntil { get; set; }
    [StringLength(4000)] public string? TechnicalDetails { get; set; }
    [MinLength(1, ErrorMessage = "The tender contains no requested items.")] public List<OfferLineViewModel> Items { get; set; } = [];
}

public class OfferLineViewModel
{
    public IFormFile? ProductPhoto { get; set; }
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever] public int? ProductPhotoDocumentId { get; set; }
    public int PurchaseRequestItemId { get; set; }
    public string ItemName { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal RequestedQuantity { get; set; }
    public string RequestedSpecifications { get; set; } = "";
    public int? RequestedLegalWarrantyMonths { get; set; }
    [Range(typeof(decimal), "0.001", "999999999999999.999")] public decimal Quantity { get; set; }
    [Range(typeof(decimal), "0", "9999999999999999.99")] public decimal UnitPrice { get; set; }
    [Required, StringLength(4000)] public string? Specifications { get; set; }
}

public sealed class GuaranteeInput
{
    public bool Enabled { get; set; }
    [StringLength(100)] public string? ReferenceNumber { get; set; }
    [StringLength(150)] public string? Issuer { get; set; }
    [StringLength(10)] public string Coverage { get; set; } = "Product";
    [StringLength(500)] public string? Scope { get; set; }
    public DateOnly? StartsOn { get; set; }
    public DateOnly? ExpiresOn { get; set; }
    public IFormFile? Document { get; set; }
    [BindNever] public string? ExistingFileName { get; set; }
}

public sealed record GuaranteeVerificationView(
    BidGuarantee? Guarantee, string Status, string Description, bool NumberMatches = true);

/// <summary>
/// Sales tax on supplier offers. Fixed at 16% (Jordan general sales tax) of the item subtotal;
/// suppliers cannot change it. Offers submitted earlier keep the tax amount they were recorded with.
/// </summary>
public static class OfferTax
{
    public const decimal RatePercent = 16m;

    /// <summary>When the fixed rate started. Offers saved before then may carry the rate the supplier entered.</summary>
    public static readonly DateTime FixedRateSinceUtc = new(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc);

    public static decimal Amount(decimal itemSubtotal) =>
        decimal.Round(checked(itemSubtotal * RatePercent / 100m), 2, MidpointRounding.AwayFromZero);
}
