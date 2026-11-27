using System.ComponentModel.DataAnnotations;
namespace SourcingTenderManagement.Models;
// Form data for login, creating users and supplier registration, with the input rules for each field.
public class LoginViewModel
{
    [Required(ErrorMessage = "Enter your email address.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [StringLength(256)]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Enter your password."), DataType(DataType.Password)]
    public string Password { get; set; } = "";
    public string? ReturnUrl { get; set; }
}
public class CreateUserViewModel
{
    [Phone, StringLength(32)] public string? PhoneNumber { get; set; }
    [Required, StringLength(150)] public string FullName { get; set; } = "";
    [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = "";
    [Required, DataType(DataType.Password)] public string Password { get; set; } = "";
    [Required] public string Role { get; set; } = "";
    public string? CommitteeRole { get; set; }
    public int? DepartmentId { get; set; }
}
// Supplier registration rules. Patterns use explicit Latin and Arabic ranges (not \p{L}) because the same
// patterns run in the browser through jQuery validation, where \p{...} is not supported without the u flag.
// Leading and trailing spaces are allowed here and removed by Normalize() before saving.
public static class SupplierFieldRules
{
    public const string CompanyName = @"^\s*(?=.*[A-Za-z؀-ۿ]).{2,200}\s*$";
    public const string RegistrationNumber = @"^\s*[A-Za-z0-9٠-٩][A-Za-z0-9٠-٩ /\-]{2,29}\s*$";
    public const string TaxNumber = @"^\s*[0-9]{6,15}\s*$";
    public const string PersonName = @"^\s*[A-Za-zÀ-ɏ؀-ۿ][A-Za-zÀ-ɏ؀-ۿ .'\-]{1,149}\s*$";
    public const string Phone = @"^\s*\+?[0-9][0-9 ()\-]{6,19}\s*$";
    public const string Password = @"^(?=.*[a-z])(?=.*[A-Z])(?=.*[0-9])(?=.*[^A-Za-z0-9]).{8,128}$";

    public static string Clean(string? value) => System.Text.RegularExpressions.Regex.Replace(value?.Trim() ?? "", @"\s+", " ");
}
public class SupplierCompanyViewModel : ProcurementCategoryInput
{
    [Required(ErrorMessage = "Enter the company's legal name.")]
    [StringLength(200)]
    [RegularExpression(SupplierFieldRules.CompanyName, ErrorMessage = "Enter the company's legal name (2–200 characters, including letters).")]
    public string CompanyName { get; set; } = "";

    [Required(ErrorMessage = "Enter the commercial registration number.")]
    [StringLength(100)]
    [RegularExpression(SupplierFieldRules.RegistrationNumber, ErrorMessage = "Use 3–30 letters or digits; spaces, “/” and “-” are allowed.")]
    public string RegistrationNumber { get; set; } = "";

    // Required because every offer carries the fixed 16% sales tax (OfferTax).
    [Required(ErrorMessage = "Enter the company's tax number.")]
    [StringLength(100)]
    [RegularExpression(SupplierFieldRules.TaxNumber, ErrorMessage = "Enter the tax number as 6–15 digits, without spaces or dashes.")]
    public string? TaxNumber { get; set; }

    [Required(ErrorMessage = "Enter the contact person's full name.")]
    [StringLength(150)]
    [RegularExpression(SupplierFieldRules.PersonName, ErrorMessage = "Use letters (Arabic or English), spaces, apostrophes or hyphens.")]
    public string ContactName { get; set; } = "";

    [StringLength(500)] public string? Address { get; set; }

    /// <summary>Trims and collapses spaces so duplicates such as "12345" and "12345 " are recognised.</summary>
    public void NormalizeCompany()
    {
        CompanyName = SupplierFieldRules.Clean(CompanyName);
        RegistrationNumber = SupplierFieldRules.Clean(RegistrationNumber);
        TaxNumber = string.IsNullOrWhiteSpace(TaxNumber) ? null : SupplierFieldRules.Clean(TaxNumber);
        ContactName = SupplierFieldRules.Clean(ContactName);
        Address = string.IsNullOrWhiteSpace(Address) ? null : SupplierFieldRules.Clean(Address);
    }
}
public class RegisterViewModel : SupplierCompanyViewModel
{
    [Required(ErrorMessage = "Enter an email address."), EmailAddress(ErrorMessage = "Enter a valid email address."), StringLength(256)]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Enter a phone number.")]
    [StringLength(32)]
    [RegularExpression(SupplierFieldRules.Phone, ErrorMessage = "Enter a phone number with 7–15 digits, for example +962 7X XXX XXXX.")]
    public string PhoneNumber { get; set; } = "";

    [Required(ErrorMessage = "Create a password."), DataType(DataType.Password), MinLength(8)]
    [RegularExpression(SupplierFieldRules.Password, ErrorMessage = "Use at least 8 characters with an uppercase letter, a lowercase letter, a number and a symbol.")]
    public string Password { get; set; } = "";

    [Required(ErrorMessage = "Enter the password again."), DataType(DataType.Password), Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
    public string ConfirmPassword { get; set; } = "";

    public void Normalize()
    {
        NormalizeCompany();
        // MVC binds an empty field to null, so guard every value before trimming.
        Email = Email?.Trim() ?? "";
        PhoneNumber = SupplierFieldRules.Clean(PhoneNumber);
    }
}
