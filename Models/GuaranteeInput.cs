using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Models;

// Form data for a bank guarantee or warranty attached to an offer, and the data shown when it is verified.
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
