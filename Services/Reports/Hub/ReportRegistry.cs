using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Models.Reports;

namespace SourcingTenderManagement.Services.Reports.Hub;

// The list of all reports, and the rules for which roles may open each one.
public sealed class ReportRegistry
{
    private readonly Dictionary<string, IReport> reports = new(StringComparer.OrdinalIgnoreCase);
    private readonly ApplicationDbContext db;

    public static IReadOnlyList<string> EditableRoles { get; } = Array.AsReadOnly(
        RoleNames.Primary.Where(role => role != RoleNames.Admin && role != RoleNames.Supplier).ToArray());

    public ReportRegistry(IEnumerable<IReport> reports, ApplicationDbContext db)
    {
        this.db = db;
        foreach (var report in reports)
        {
            if (string.IsNullOrWhiteSpace(report.Key))
                throw new InvalidOperationException("Every report must have a non-empty key.");

            if (!this.reports.TryAdd(report.Key, report))
                throw new InvalidOperationException($"The report key '{report.Key}' is registered more than once.");
        }
    }

    public IReport? Find(string? key)
    {
        return key != null && reports.TryGetValue(key, out var report) ? report : null;
    }

    // These reports have narrower authorization inside their existing implementation.
    // A stored permission must never widen that boundary.
    public bool CanConfigure(string key, string role) => Find(key) is { } report &&
        EditableRoles.Contains(role, StringComparer.Ordinal) && SupportsRole(report, role);

    private static bool SupportsRole(IReport report, string role) => report.Key.ToLowerInvariant() switch
    {
        "top-committee" => role is RoleNames.Admin or RoleNames.TopCommittee,
        "audit-log" => role == RoleNames.Admin,
        _ => true
    };

    public async Task<IReadOnlyList<IReport>> GetAvailableAsync(string role, CancellationToken ct = default)
    {
        if (role == RoleNames.Admin) return reports.Values.ToList();
        if (!EditableRoles.Contains(role, StringComparer.Ordinal)) return [];

        var rules = await db.ReportAccessRules.AsNoTracking().Where(rule => rule.Role == role).ToListAsync(ct);
        return reports.Values.Where(report => ResolveAccess(report, role, FindRule(rules, report.Key, role)?.Allowed)).ToList();
    }

    public async Task<bool> CanAccessAsync(string key, string role, CancellationToken ct = default)
    {
        var report = Find(key);
        return report != null && (await GetAvailableAsync(role, ct)).Contains(report);
    }

    public async Task<ReportResult> RunAsync(string key, ReportQuery query, ReportScope scope, CancellationToken ct = default)
    {
        var report = Find(key) ?? throw new KeyNotFoundException("Unknown report key.");
        if (string.IsNullOrWhiteSpace(scope.UserId) || scope.IsAdmin != (scope.Role == RoleNames.Admin) ||
            !await CanAccessAsync(key, scope.Role, ct))
            throw new UnauthorizedAccessException("Access to this report is denied.");

        return await report.RunAsync(query, scope, ct);
    }

    public async Task<IReadOnlyList<ReportAccessEntry>> GetPermissionsAsync(CancellationToken ct = default)
    {
        var rules = await db.ReportAccessRules.AsNoTracking().ToListAsync(ct);
        return reports.Values.Select(report => new ReportAccessEntry(report.Key, report.Title,
            RoleNames.Primary.Select(role =>
            {
                var rule = FindRule(rules, report.Key, role);
                var editable = CanConfigure(report.Key, role);
                return new ReportRoleAccess(role, ResolveAccess(report, role, rule?.Allowed),
                    !editable || rule == null, editable);
            }).ToList())).ToList();
    }

    private static ReportAccessRule? FindRule(IEnumerable<ReportAccessRule> rules, string key, string role) =>
        rules.SingleOrDefault(rule => rule.Role == role && rule.ReportKey.Equals(key, StringComparison.OrdinalIgnoreCase));

    private static bool ResolveAccess(IReport report, string role, bool? allowed)
    {
        if (role == RoleNames.Admin) return true;
        if (!EditableRoles.Contains(role, StringComparer.Ordinal) || !SupportsRole(report, role)) return false;

        return allowed ?? report.DefaultRoles.Contains(role, StringComparer.Ordinal);
    }
}
