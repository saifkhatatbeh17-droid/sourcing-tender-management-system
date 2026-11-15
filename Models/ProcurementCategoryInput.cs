using System.ComponentModel.DataAnnotations;

namespace SourcingTenderManagement.Models;

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
