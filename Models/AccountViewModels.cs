using System.ComponentModel.DataAnnotations;
namespace SourcingTenderManagement.Models;
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
public class SupplierCompanyViewModel
{
    [Required, StringLength(200)] public string CompanyName { get; set; } = "";
    [Required, StringLength(100)] public string RegistrationNumber { get; set; } = "";
    [StringLength(100)] public string? TaxNumber { get; set; }
    [Required, StringLength(150)] public string ContactName { get; set; } = "";
    [StringLength(500)] public string? Address { get; set; }
}
public class RegisterViewModel : SupplierCompanyViewModel
{
    [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = "";
    [Required, Phone, StringLength(32)] public string PhoneNumber { get; set; } = "";
    [Required, DataType(DataType.Password), MinLength(8)] public string Password { get; set; } = "";
    [Required, DataType(DataType.Password), Compare(nameof(Password))] public string ConfirmPassword { get; set; } = "";
}
