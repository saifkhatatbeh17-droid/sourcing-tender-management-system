using System.ComponentModel.DataAnnotations;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Models;

// Form data for creating or editing a purchase request and its items, with validation rules.
public class PurchaseRequestForm : IValidatableObject
{
    public static readonly string[] Priorities = ["Low", "Normal", "High", "Urgent"];

    [Required, StringLength(200)] public string Title { get; set; } = "";
    [Required, StringLength(2000)] public string Reason { get; set; } = "";
    [Required, RegularExpression("Low|Normal|High|Urgent")] public string Priority { get; set; } = "Normal";
    [Required, DataType(DataType.Date)] public DateOnly? TargetDate { get; set; }
    [MinLength(1, ErrorMessage = "Add at least one item.")]
    public List<PurchaseRequestItemForm> Items { get; set; } = [];

    private static readonly TimeZoneInfo AmmanTimeZone = TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows() ? "Jordan Standard Time" : "Asia/Amman");

    public static DateOnly EarliestTargetDate =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, AmmanTimeZone));

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (TargetDate is DateOnly targetDate && targetDate < EarliestTargetDate)
        {
            yield return new ValidationResult("Choose today or a future date.", [nameof(TargetDate)]);
        }
    }

    public void ApplyTo(PurchaseRequest request)
    {
        request.Title = Title.Trim();
        request.Reason = Reason.Trim();
        request.Priority = Priority;
        request.TargetDate = TargetDate!.Value;
        request.Status = "Submitted";
        request.SubmittedAt = DateTime.UtcNow;
        foreach (var item in Items)
        {
            request.PurchaseRequestItems.Add(new PurchaseRequestItem
            {
                ItemName = item.ItemName.Trim(),
                Quantity = item.Quantity!.Value,
                Unit = item.Unit.Trim(),
                Specifications = item.Specifications.Trim(),
                LegalWarrantyMonths = item.LegalWarrantyMonths
            });
        }
    }
}

public class PurchaseRequestItemForm : IValidatableObject
{
    [Required, StringLength(200)] public string ItemName { get; set; } = "";
    [Required] public decimal? Quantity { get; set; }
    [Required, StringLength(30)] public string Unit { get; set; } = "";
    [Required, StringLength(4000)] public string Specifications { get; set; } = "";
    [Range(1, 120, ErrorMessage = "Enter a warranty period between 1 and 120 months, or leave it empty.")]
    public int? LegalWarrantyMonths { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Quantity is decimal quantity &&
            (quantity < 0.001m || quantity > 999999999999999.999m || decimal.Round(quantity, 3) != quantity))
        {
            yield return new ValidationResult("Enter a positive quantity with up to 15 digits before and 3 digits after the decimal point.", [nameof(Quantity)]);
        }
    }
}
