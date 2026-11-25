using System.ComponentModel.DataAnnotations;
using SourcingTenderManagement.Models.Entities;
using System.Net.Mail;

namespace SourcingTenderManagement.Models;

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

public sealed class ContactMessageInput
{
    [Required, StringLength(120)]
    public string FullName { get; set; } = "";

    private string email = "";

    [Required, StringLength(254), EmailAddress]
    public string Email
    {
        get => email;
        set => email = value?.Trim() ?? "";
    }

    [Required, StringLength(150)]
    public string Subject { get; set; } = "";

    [Required, StringLength(2000)]
    public string Message { get; set; } = "";

    public static bool IsValidAddress(string address) =>
        !address.Any(char.IsControl) && MailAddress.TryCreate(address, out var parsed)
        && string.Equals(parsed.Address, address, StringComparison.Ordinal);
}

public sealed record ContactMessagesPage(
    IReadOnlyList<ContactMessage> Messages, string? Search, string? Status, int Page, int TotalPages);

public sealed record SupplierTenderActivity(int TenderId, string Title, int BidId, DateTime SubmittedAt,
    string Outcome, decimal Total, string Currency, string? Compliance);

public sealed record SupplierActivityRow(int SupplierId, string Company, string Status, string RegistrationNumber,
    string? Category, IReadOnlyList<SupplierTenderActivity> History)
{
    public int Awarded => History.Count(row => row.Outcome == "Awarded");
    public int Rejected => History.Count(row => row.Outcome == "Rejected");
}

public sealed record DashboardSlice(string Label, int Count, string Tone = "active", string? Link = null);

public sealed record DashboardOverview(string Title, string Scope, string Unit, string FocusLabel,
    IReadOnlyList<DashboardSlice> Segments, string StagesTitle, string StagesDescription,
    IReadOnlyList<DashboardSlice> Stages, string? Link = null)
{
    public int Total => Segments.Sum(s => s.Count);

    public static DashboardOverview Tenders(IReadOnlyDictionary<string, int> states, string scope)
    {
        int Count(params string[] keys) => keys.Sum(k => states.GetValueOrDefault(k));
        var stages = new List<DashboardSlice> {
            new("Receiving offers", Count("Published", "Open")), new("Reviewing offers", Count("Closed")),
            new("Committee evaluation", Count("UnderEvaluation")), new("Officer report review", Count("AwaitingOfficerSummary")),
            new("Manager review", Count("AwaitingManagerReview")), new("CEO & Chiefs decision", Count("AwaitingCommitteeDecision"))
        };
        var active = stages.Sum(s => s.Count);
        var other = states.Values.Sum() - active - Count("Awarded", "Cancelled");
        var segments = new List<DashboardSlice> { new("Active", active), new("Awarded", Count("Awarded"), "positive"), new("Cancelled", Count("Cancelled"), "muted") };
        if (other > 0) segments.Add(new("Other", other, "other"));
        return new("Tender status", scope, "tenders", "active", segments,
            "Where active tenders are", $"{active:N0} active tenders · current stage", stages);
    }
}

public record DashboardMetric(string Label, string Value, string Note, bool Primary = false, string? Action = null, string? Link = null);
