using System.ComponentModel.DataAnnotations;
using SourcingTenderManagement.Models.Entities;
using System.Text.RegularExpressions;

namespace SourcingTenderManagement.Models;

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

public static class DepartmentRequestProgress
{
    public static readonly string[] Statuses =
    [
        "Submitted", "UnderReview", "Returned", "Approved", "ConvertedToTender",
        "Draft", "Published", "Open", "Closed", "UnderEvaluation",
        "AwaitingOfficerSummary", "AwaitingManagerReview", "AwaitingCommitteeDecision",
        "Awarded", "Rejected", "Cancelled"
    ];

    public static string Code(PurchaseRequest request) =>
        request.Status == "ConvertedToTender"
            ? request.Tenders.OrderByDescending(t => t.TenderId).FirstOrDefault()?.Status ?? request.Status
            : request.Status;

    // Keep filtering in SQL, using the same current stage shown by Code.
    public static IQueryable<PurchaseRequest> Filter(IQueryable<PurchaseRequest> requests, string status) =>
        requests.Where(request => (request.Status == "ConvertedToTender"
            ? request.Tenders.OrderByDescending(t => t.TenderId).Select(t => t.Status).FirstOrDefault() ?? request.Status
            : request.Status) == status);

    public static string Label(string status) => status switch
    {
        "UnderReview" => "Under review",
        "ConvertedToTender" => "Converted to tender",
        "UnderEvaluation" => "Under evaluation",
        "AwaitingOfficerSummary" => "Officer review",
        "AwaitingManagerReview" => "Manager review",
        "AwaitingCommitteeDecision" => "Committee decision",
        _ => Regex.Replace(status, "([a-z])([A-Z])", "$1 $2")
    };
}

public record ClarificationRecordOption(string Kind, int Id, string Title, string Detail);

public class ProcurementCategoryInput : IValidatableObject
{
    public const string Other = "Other";
    public static IReadOnlyList<string> Options { get; } = [
        "IT & computers", "Laboratory instruments & supplies", "Pharmaceuticals",
        "Medical equipment & supplies", "Chemicals & raw materials", "Packaging & labeling",
        "Office supplies", "Maintenance & facilities", "Professional services"
    ];

    public string? Category { get; set; }

    [StringLength(100)]
    public string? OtherCategory { get; set; }

    public string? SelectedCategory => Category == Other ? OtherCategory?.Trim() : Category;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Category == Other)
        {
            if (string.IsNullOrWhiteSpace(OtherCategory))
                yield return new ValidationResult("Enter the category name.", [nameof(OtherCategory)]);
        }
        else if (Category == null || !Options.Contains(Category))
        {
            yield return new ValidationResult("Choose a category.", [nameof(Category)]);
        }
    }

    public void LoadCategory(string? category)
    {
        if (category == null || Options.Contains(category))
        {
            Category = category;
            OtherCategory = null;
        }
        else
        {
            Category = Other;
            OtherCategory = category;
        }
    }
}

public class SourcingOfficerTenderForm : ProcurementCategoryInput
{
    public int TenderId { get; set; }
    public int? ReplacesTenderId { get; set; }
    public int PurchaseRequestId { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string? Terms { get; set; }
    public DateTime OpensAt { get; set; }
    public DateTime ClosesAt { get; set; }
}
