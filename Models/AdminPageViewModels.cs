using System.ComponentModel.DataAnnotations;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Models;

// Data shown on the Admin pages (dashboard, users, departments, suppliers).
public sealed class AdminDashboardViewModel
{
    public int UsersCount { get; init; }
    public int SuppliersCount { get; init; }
    public int DepartmentsCount { get; init; }
    public int PurchaseRequestsCount { get; init; }
    public int TendersCount { get; init; }
    public IReadOnlyList<PendingSupplierApplicationViewModel> PendingApplications { get; init; } = [];
}

public sealed record PendingSupplierApplicationViewModel(int Id, string CompanyName, DateTime CreatedAt);

public class UsersPageViewModel
{
    public IReadOnlyList<AdminUserRowViewModel> Users { get; init; } = [];
    public IReadOnlyList<Department> Departments { get; init; } = [];
    public CreateUserViewModel CreateUser { get; init; } = new();
    public string? Search { get; init; }
    public string? Role { get; init; }
    public bool? IsActive { get; init; }
    public string? CurrentUserId { get; init; }
}

public sealed record AdminUserRowViewModel(
    string Id, string FullName, string? Email, string? PhoneNumber, bool IsActive, int? DepartmentId,
    IReadOnlyList<string> Roles);

public class EditUserViewModel
{
    [Required] public string Id { get; set; } = "";
    [Required, StringLength(150)] public string FullName { get; set; } = "";
    [Phone, StringLength(32)] public string? PhoneNumber { get; set; }
    public int? DepartmentId { get; set; }
    public bool IsActive { get; set; }
}

public sealed class DepartmentsPageViewModel
{
    public IReadOnlyList<Department> Departments { get; init; } = [];
    public string? CreateName { get; init; }
    public bool OpenCreate { get; init; }
    public EditDepartmentViewModel? EditInput { get; init; }
}

public class EditDepartmentViewModel
{
    public int DepartmentId { get; set; }
    [Required, StringLength(100)] public string Name { get; set; } = "";
    public bool IsActive { get; set; }
}

public class NotificationBellViewModel
{
    public int UnreadCount { get; set; }
    public List<Notification> Items { get; set; } = [];
}

public sealed class SupplierListViewModel
{
    public static IReadOnlyList<string> Decisions { get; } = ["Pending", "Approved", "ChangesRequested", "Rejected", "Suspended"];
    public IReadOnlyList<SupplierProfile> Suppliers { get; init; } = [];
    public IReadOnlyList<string> AvailableStatuses { get; init; } = [];
    public string? SelectedStatus { get; init; }
    public bool ApplicationsOnly { get; init; }

    public static string StatusLabel(string status) => status == "ChangesRequested" ? "Changes requested" : status;

    public static string StatusStyle(string status) => status switch
    {
        "Approved" => "text-bg-success",
        "Pending" => "text-bg-warning",
        "Rejected" or "Suspended" => "text-bg-danger",
        _ => "text-bg-secondary"
    };
}

public sealed record SupplierDetailsViewModel(
    SupplierProfile Supplier, ReviewSupplierViewModel Review, bool DocumentsConfigured);

public sealed class ReviewSupplierViewModel
{
    public int Id { get; set; }
    public string PreviousStatus { get; set; } = "";
    [Range(typeof(long), "0", "3155378975999999999")]
    public long? ReviewedAtTicks { get; set; }
    [Required(ErrorMessage = "Choose a review decision.")]
    public string Status { get; set; } = "";
    [Required(ErrorMessage = "Enter a reason for this decision.")]
    [StringLength(2000)]
    public string Notes { get; set; } = "";
}
