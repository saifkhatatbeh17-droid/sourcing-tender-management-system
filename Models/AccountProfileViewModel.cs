using System.ComponentModel.DataAnnotations;

namespace SourcingTenderManagement.Models;

// Data shown and posted by the profile pages (edit profile, change password).
public sealed class EditProfileViewModel
{
    [Phone, StringLength(32)]
    [Display(Name = "Phone number")]
    public string? PhoneNumber { get; set; }
}

public sealed class ChangePasswordViewModel
{
    [Required, DataType(DataType.Password)]
    [Display(Name = "Current password")]
    public string CurrentPassword { get; set; } = "";

    [Required, DataType(DataType.Password)]
    [Display(Name = "New password")]
    public string NewPassword { get; set; } = "";

    [Required, DataType(DataType.Password), Compare(nameof(NewPassword))]
    [Display(Name = "Confirm new password")]
    public string ConfirmPassword { get; set; } = "";
}

public record AccountProfileViewModel(
    string FullName,
    string? Email,
    string? PhoneNumber,
    string? DepartmentName,
    IReadOnlyList<string> Roles,
    string? UserName,
    bool? DepartmentIsActive);
