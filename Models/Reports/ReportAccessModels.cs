using System.ComponentModel.DataAnnotations;

namespace SourcingTenderManagement.Models.Reports;

public sealed class ReportAccessChange
{
    [Required, StringLength(100)]
    public string ReportKey { get; set; } = "";

    [Required, StringLength(64)]
    public string Role { get; set; } = "";

    [Required]
    public bool? Allowed { get; set; }
}

public sealed record ReportRoleAccess(string Role, bool Allowed, bool IsDefault, bool CanEdit);

public sealed record ReportAccessEntry(string Key, string Title, IReadOnlyList<ReportRoleAccess> Permissions);
