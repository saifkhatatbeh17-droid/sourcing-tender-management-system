namespace SourcingTenderManagement.Models.Entities;

// Database table: one audit-log line (who did what, to which record, and when).
public sealed class AuditEntry
{
    public long AuditEntryId { get; set; }
    public DateTime OccurredAt { get; set; }
    public string? UserId { get; set; }
    public string? UserName { get; set; }
    public string? Role { get; set; }
    public required string Action { get; set; }
    public required string EntityType { get; set; }
    public required string EntityId { get; set; }
    public int? TenderId { get; set; }
    public required string Summary { get; set; }
    public string? ChangesJson { get; set; }
    public string? IpAddress { get; set; }
}
