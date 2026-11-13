using System.ComponentModel.DataAnnotations;

namespace SourcingTenderManagement.Models;

public sealed class EditProfileViewModel
{
    [Phone, StringLength(32)]
    [Display(Name = "Phone number")]
    public string? PhoneNumber { get; set; }
}

public record AccountProfileViewModel(
    string FullName,
    string? Email,
    string? PhoneNumber,
    string? DepartmentName,
    IReadOnlyList<string> Roles);
