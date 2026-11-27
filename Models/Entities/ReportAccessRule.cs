namespace SourcingTenderManagement.Models.Entities;

// Database table: whether a role may open a report (set on the Admin 'Report access' page).
public sealed class ReportAccessRule
{
    public int ReportAccessRuleId { get; set; }
    public string ReportKey { get; set; } = "";
    public string Role { get; set; } = "";
    public bool Allowed { get; set; }
    public string UpdatedByUserId { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
}
