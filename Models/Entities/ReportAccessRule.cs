namespace SourcingTenderManagement.Models.Entities;

public sealed class ReportAccessRule
{
    public int ReportAccessRuleId { get; set; }
    public string ReportKey { get; set; } = "";
    public string Role { get; set; } = "";
    public bool Allowed { get; set; }
    public string UpdatedByUserId { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
}
