using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Services.Reports.Hub;

namespace SourcingTenderManagement.Services.Reports.Data;

// Reports hub: the 'Audit log' report.
public sealed class AuditLogReport(ApplicationDbContext db) : IReport
{
    public string Key => "audit-log";
    public string Title => "Audit log";
    public string Description => "Account and procurement activity with a record of changes.";
    public string Icon => "clock-history";
    public IReadOnlyList<string> DefaultRoles => [];
    public IReadOnlyList<string> Filters => ["date", "tender", "status", "search"];
    public IReadOnlyList<string> StatusOptions => ["Added", "Modified", "Deleted", "Account.SignInSuccess", "Account.SignInFailure",
        "Account.Create", "Account.Edit", "Account.PasswordReset", "Account.Import", "SupplierProfile", "SupplierDocument",
        "Tender", "Bid", "Invitation", "PurchaseRequest", "Evaluation", "EvaluationReport", "TenderApproval", "Clarification"];

    public async Task<ReportResult> RunAsync(ReportQuery query, ReportScope scope, CancellationToken ct)
    {
        var columns = new[] { "Time (UTC)", "User", "Role", "Action", "Entity", "Record", "Summary" };
        if (!scope.IsAdmin) return ReportRows.Result(query, columns, []);
        var rows = db.AuditEntries.AsNoTracking().Where(a => (query.TenderId == null || a.TenderId == query.TenderId)
            && (query.Status == null || a.Action == query.Status || a.EntityType == query.Status)
            && (query.Search == null || a.UserName != null && a.UserName.Contains(query.Search)
                || a.UserId != null && a.UserId.Contains(query.Search)));
        if (query.From is { } from) rows = rows.Where(a => a.OccurredAt >= from.ToDateTime(TimeOnly.MinValue));
        if (query.To is { } to) rows = rows.Where(a => a.OccurredAt <= to.ToDateTime(TimeOnly.MaxValue));
        var count = await rows.CountAsync(ct);
        var counts = await rows.GroupBy(a => a.Action).Select(g => new { Action = g.Key, Count = g.Count() }).ToListAsync(ct);
        var limit = query.ForExport ? 5000 : 25;
        var page = Math.Clamp(query.Page, 1, int.MaxValue / 25);
        var data = await rows.OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.AuditEntryId)
            .Skip(query.ForExport ? 0 : (page - 1) * 25).Take(limit).ToListAsync(ct);
        return new([new("Entries", count.ToString())],
            new("Action counts", "bar", counts.OrderByDescending(x => x.Count).Select(x => new ReportChartPoint(x.Action, x.Count)).ToArray()),
            new(columns, data.Select(a => new ReportRow([a.OccurredAt.ToString("dd MMM yyyy HH:mm:ss"), a.UserName ?? "—",
                a.Role ?? "—", a.Action, a.EntityType, a.EntityId, a.Summary], $"/Admin/AuditEntry/{a.AuditEntryId}")).ToArray(), count),
            query.ForExport && count > limit ? ["Export limited to the first 5,000 rows."] : []);
    }
}
