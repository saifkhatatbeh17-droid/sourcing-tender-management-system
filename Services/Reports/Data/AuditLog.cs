using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Services.Reports.Data;

public interface IAuditLog
{
    Task WriteAsync(string action, string entityType, string entityId, string summary,
        int? tenderId = null, string? changesJson = null, CancellationToken ct = default);
}

public sealed class AuditLog(ApplicationDbContext db, IHttpContextAccessor accessor) : IAuditLog
{
    public Task WriteAsync(string action, string entityType, string entityId, string summary,
        int? tenderId = null, string? changesJson = null, CancellationToken ct = default) =>
        db.Database.ExecuteSqlInterpolatedAsync(Insert(Create(accessor, action, entityType, entityId, summary, tenderId, changesJson)), ct);

    internal static AuditEntry Create(IHttpContextAccessor accessor, string action, string entityType,
        string entityId, string summary, int? tenderId, string? changesJson)
    {
        var context = accessor.HttpContext;
        var user = context?.User;
        return new AuditEntry
        {
            OccurredAt = DateTime.UtcNow, Action = action, EntityType = entityType, EntityId = entityId,
            Summary = Limit(summary), TenderId = tenderId, ChangesJson = changesJson,
            UserId = user?.FindFirstValue(ClaimTypes.NameIdentifier),
            UserName = Limit(user?.Identity?.Name ?? (context is null ? "System" : "Anonymous")),
            Role = user?.FindFirstValue(ClaimTypes.Role), IpAddress = context?.Connection.RemoteIpAddress?.ToString()
        };
    }

    internal static string Limit(string value) => value.Length > 500 ? value[..500] : value;

    internal static FormattableString Insert(AuditEntry a) => $"""
        INSERT INTO dbo.AuditEntries
        (OccurredAt, UserId, UserName, Role, Action, EntityType, EntityId, TenderId, Summary, ChangesJson, IpAddress)
        VALUES ({a.OccurredAt}, {a.UserId}, {a.UserName}, {a.Role}, {a.Action}, {a.EntityType},
        {a.EntityId}, {a.TenderId}, {a.Summary}, {a.ChangesJson}, {a.IpAddress})
        """;
}
