using System.Globalization;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Services.Reports.Hub;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using SourcingTenderManagement.Models.TopCommittee;
using SourcingTenderManagement.Services.TopCommittee;
using SourcingTenderManagement.Models;

namespace SourcingTenderManagement.Services.Reports.Data;

internal static class ReportRows
{
    public static string Date(DateTime? value) => value?.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) ?? "—";
    public static string Money(decimal value, string currency) => $"{value:N2} {currency}";
    public static string ScopeFilter(ReportQuery query, string prefix) =>
        query.Drill?.StartsWith(prefix, StringComparison.Ordinal) == true ? query.Drill[prefix.Length..] : "";
    public static bool Within(DateTime value, ReportQuery query) =>
        (!query.From.HasValue || DateOnly.FromDateTime(value) >= query.From) &&
        (!query.To.HasValue || DateOnly.FromDateTime(value) <= query.To);

    public static IQueryable<Tender> Tenders(ApplicationDbContext db, ReportScope scope)
    {
        var rows = db.Tenders.AsQueryable();
        if (scope.IsAdmin) return rows;
        return scope.Role switch
        {
            RoleNames.SourcingOfficer => rows.Where(t => t.CreatedByUserId == scope.UserId),
            RoleNames.EvaluationCommittee => rows.InEvaluationWorkspace(scope.UserId)
                .Where(t => t.CommitteeAssignments.Any(a => a.CommitteeType == "Evaluation" && a.UserId == scope.UserId)),
            RoleNames.TopCommittee => rows.Where(t => t.CommitteeAssignments.Any(a => a.CommitteeType == "Top" && a.UserId == scope.UserId)),
            RoleNames.SourcingManager => rows.Where(t => t.EvaluationReport != null &&
                (t.Status == "AwaitingManagerReview" || t.TenderApprovals.Any(a => a.Stage == "Manager" && a.UserId == scope.UserId))),
            _ => rows.Where(_ => false)
        };
    }

    public static ReportResult Result(ReportQuery query, string[] columns, IEnumerable<ReportRow> rows,
        IReadOnlyList<ReportKpi>? kpis = null, ReportChart? chart = null, params string[] notes)
    {
        var all = rows.ToList();
        var limit = query.ForExport ? 5000 : 25;
        var page = Math.Clamp(query.Page, 1, int.MaxValue / 25);
        var selected = all.Skip(query.ForExport ? 0 : (page - 1) * 25).Take(limit).ToArray();
        if (query.ForExport && all.Count > limit) notes = [..notes, "Export limited to the first 5,000 rows."];
        return new(kpis ?? [], chart, new(columns, selected, all.Count), notes);
    }

    public static (IReadOnlyList<ReportKpi>, ReportChart) Counts(string title, IEnumerable<string> labels, string prefix = "status:")
    {
        var groups = labels.GroupBy(x => x).OrderBy(x => x.Key).ToArray();
        return (groups.Select(x => new ReportKpi(x.Key, x.Count().ToString(), prefix + x.Key)).ToArray(),
            new(title, "bar", groups.Select(x => new ReportChartPoint(x.Key, x.Count(), prefix + x.Key)).ToArray()));
    }
}

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

public sealed class AuditSaveChangesInterceptor(IHttpContextAccessor accessor) : SaveChangesInterceptor
{
    private sealed record Change(EntityEntry Entry, string Action, string? Values, object? OriginalKey, int? OriginalTenderId);

    private static readonly HashSet<Type> Audited =
    [
        typeof(PurchaseRequest), typeof(Tender), typeof(Invitation), typeof(Bid), typeof(EvaluationReport),
        typeof(Models.Entities.Evaluation), typeof(TenderApproval), typeof(CommitteeWeightProposal), typeof(CommitteeTieVote),
        typeof(CommitteeAssignment), typeof(Clarification), typeof(SupplierProfile), typeof(SupplierDocument),
        typeof(Department), typeof(ApplicationUser), typeof(IdentityUserRole<string>), typeof(TenderContract)
    ];

    private List<Change> changes = [];
    private IDbContextTransaction? transaction;

    public override InterceptionResult<int> SavingChanges(DbContextEventData data, InterceptionResult<int> result)
    {
        if (data.Context is { } context && Capture(context) && context.Database.CurrentTransaction is null)
            transaction = context.Database.BeginTransaction();
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
        InterceptionResult<int> result, CancellationToken ct = default)
    {
        if (data.Context is { } context && Capture(context) && context.Database.CurrentTransaction is null)
            transaction = await context.Database.BeginTransactionAsync(ct);
        return result;
    }

    public override int SavedChanges(SaveChangesCompletedEventData data, int result)
    {
        try
        {
            foreach (var change in changes)
                data.Context!.Database.ExecuteSqlInterpolated(AuditLog.Insert(Entry(change)));
            transaction?.Commit();
            return result;
        }
        finally { Reset(); }
    }

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken ct = default)
    {
        try
        {
            foreach (var change in changes)
                await data.Context!.Database.ExecuteSqlInterpolatedAsync(AuditLog.Insert(Entry(change)), ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
            return result;
        }
        finally { await ResetAsync(); }
    }

    public override void SaveChangesFailed(DbContextErrorEventData data) => Reset();
    public override void SaveChangesCanceled(DbContextEventData data) => Reset();
    public override Task SaveChangesFailedAsync(DbContextErrorEventData data, CancellationToken ct = default) => ResetAsync().AsTask();
    public override Task SaveChangesCanceledAsync(DbContextEventData data, CancellationToken ct = default) => ResetAsync().AsTask();

    private void Reset()
    {
        transaction?.Dispose();
        transaction = null;
        changes.Clear();
    }

    private async ValueTask ResetAsync()
    {
        if (transaction is not null) await transaction.DisposeAsync();
        transaction = null;
        changes.Clear();
    }

    private bool Capture(DbContext context)
    {
        changes = context.ChangeTracker.Entries().Where(e => Audited.Contains(e.Metadata.ClrType) &&
                e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(e =>
            {
                var modified = e.State == EntityState.Modified;
                var fields = e.Properties.Where(p => Allowed(e, p.Metadata.Name) && (!modified || p.IsModified))
                    .ToDictionary(p => p.Metadata.Name, p => new
                    {
                        Before = e.State == EntityState.Added ? null : Safe(p.OriginalValue),
                        After = e.State == EntityState.Deleted ? null : Safe(p.CurrentValue)
                    });
                var key = e.Metadata.FindPrimaryKey()?.Properties.FirstOrDefault()?.Name;
                return new Change(e, e.State.ToString(), fields.Count == 0 ? null : JsonSerializer.Serialize(fields),
                    key is null ? null : e.Property(key).OriginalValue, TenderId(e));
            }).Where(c => c.Values is not null).ToList();
        return changes.Count != 0;
    }

    private AuditEntry Entry(Change change)
    {
        var e = change.Entry;
        var key = e.Metadata.FindPrimaryKey()?.Properties.FirstOrDefault()?.Name;
        var id = (change.Action == "Deleted" || key is null ? change.OriginalKey : e.Property(key).CurrentValue)?.ToString() ?? "";
        var tender = change.Action == "Deleted" ? change.OriginalTenderId : TenderId(e);
        var roleChange = e.Entity is IdentityUserRole<string>;
        var type = roleChange ? nameof(ApplicationUser) : e.Metadata.ClrType.Name;
        var action = roleChange ? "Account.Role" + change.Action : change.Action;
        return AuditLog.Create(accessor, action, type, id, $"{type} {change.Action.ToLowerInvariant()}", tender, change.Values);
    }

    private static int? TenderId(EntityEntry entry) => entry.Metadata.FindProperty("TenderId") is null
        ? null : entry.Property("TenderId").CurrentValue as int?;

    private static object? Safe(object? value) => value is string text ? AuditLog.Limit(text) : value;

    private static bool Allowed(EntityEntry e, string name)
    {
        if (e.Entity is ApplicationUser) return name is nameof(ApplicationUser.IsActive) or nameof(ApplicationUser.DepartmentId);
        return !e.Property(name).Metadata.IsPrimaryKey() || e.Entity is IdentityUserRole<string>
            ? !name.Contains("StoredFile", StringComparison.OrdinalIgnoreCase) &&
              !name.Contains("Token", StringComparison.OrdinalIgnoreCase) &&
              !name.Contains("Password", StringComparison.OrdinalIgnoreCase) &&
              !name.Contains("Hash", StringComparison.OrdinalIgnoreCase) &&
              !name.Contains("Stamp", StringComparison.OrdinalIgnoreCase) &&
              !name.Contains("Json", StringComparison.OrdinalIgnoreCase) &&
              e.Property(name).Metadata.ClrType != typeof(byte[])
            : false;
    }
}

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

public sealed class ApprovalsReport(ApplicationDbContext db) : IReport
{
    public string Key => "approvals";
    public string Title => "Approvals & decisions";
    public string Description => "Committee recommendations, management decisions, awards, and contracts.";
    public string Icon => "check-circle";
    public IReadOnlyList<string> DefaultRoles => [RoleNames.SourcingManager, RoleNames.TopCommittee];
    public IReadOnlyList<string> Filters => ["date", "department", "tender", "status", "search"];
    public IReadOnlyList<string> StatusOptions => ["Awarded", "Rejected", "Cancelled", "Pending", "Approved", "ReturnedForReview"];

    internal static IQueryable<TenderApproval> ManagerDecisions(ApplicationDbContext db, string? search, string? decision, string? userId = null)
    {
        var rows = db.TenderApprovals.AsNoTracking().Where(a => a.Stage == "Manager");
        if (userId != null) rows = rows.Where(a => a.UserId == userId);
        if (!string.IsNullOrWhiteSpace(search)) rows = rows.Where(a => a.Tender.Title.Contains(search.Trim()));
        if (decision is "Approved" or "Rejected" or "ReturnedForReview") rows = rows.Where(a => a.Decision == decision);
        return rows;
    }

    public async Task<ReportResult> RunAsync(ReportQuery query, ReportScope scope, CancellationToken ct)
    {
        var manager = !scope.IsAdmin && scope.Role == RoleNames.SourcingManager;
        var managerDecisions = ManagerDecisions(db, query.Search, null, manager ? scope.UserId : null);
        var tenders = ReportRows.Tenders(db, scope).Where(t =>
            (t.EvaluationReport != null || t.CommitteeAssignments.Any(a => a.CommitteeType == "Evaluation")) &&
            (query.DepartmentId == null || t.PurchaseRequest.DepartmentId == query.DepartmentId) &&
            (query.TenderId == null || t.TenderId == query.TenderId) &&
            (query.Search == null || t.Title.Contains(query.Search)));
        if (manager) tenders = tenders.Where(t => managerDecisions.Any(a => a.TenderId == t.TenderId));
        var data = await tenders.AsNoTracking().OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.TenderId)
            .Select(t => new
            {
                t.TenderId, t.Title, t.Status, t.CreatedAt, t.AwardedAt,
                ReportStatus = t.EvaluationReport == null ? null : t.EvaluationReport.Status,
                Recommended = t.EvaluationReport == null || t.EvaluationReport.Bid == null ? null : t.EvaluationReport.Bid.Invitation.SupplierProfile.CompanyName,
                OfficerSummary = t.EvaluationReport == null ? null : t.EvaluationReport.OfficerSummary,
                ManagerSummary = t.EvaluationReport == null ? null : t.EvaluationReport.ManagerSummary,
                Decision = managerDecisions.Where(a => a.TenderId == t.TenderId).OrderByDescending(a => a.DecidedAt)
                    .Select(a => new { a.Decision, a.DecidedAt, Comments = a.Notes }).FirstOrDefault(),
                Approvals = t.TenderApprovals.Count(a => a.Stage == "Evaluation" && a.Decision == "Approved"),
                TopYes = t.TenderApprovals.Count(a => a.Stage == "Top" && a.Decision == "Approved"),
                TopNo = t.TenderApprovals.Count(a => a.Stage == "Top" && a.Decision == "Rejected"),
                HasContract = db.TenderContracts.Any(c => c.TenderId == t.TenderId)
            }).ToListAsync(ct);
        var rows = data.Where(t => ReportRows.Within(manager ? t.Decision?.DecidedAt ?? t.CreatedAt : t.AwardedAt ?? t.CreatedAt, query))
            .Select(t => (Tender: t, Outcome: t.Status == "Awarded" ? "Awarded" : t.TopNo > 0 || t.ReportStatus == "ManagerRejected"
                ? "Rejected" : t.Status == "Cancelled" ? "Cancelled" : "Pending")).ToArray();
        var (kpis, chart) = ReportRows.Counts("Tender outcomes", rows.Select(r => r.Outcome), "outcome:");
        var outcome = ReportRows.ScopeFilter(query, "outcome:");
        var matches = rows.Where(r => (outcome.Length == 0 || r.Outcome == outcome) &&
            (string.IsNullOrEmpty(query.Status) || r.Outcome == query.Status || r.Tender.Decision?.Decision == query.Status));
        return ReportRows.Result(query,
            ["Tender", "Recommended supplier", "Report status", "Committee approvals", "Officer summary", "Manager decision", "Manager comments", "Manager date", "Top approve / reject", "Outcome", "Award date", "Contract"],
            matches.Select(r => new ReportRow([r.Tender.Title, r.Tender.Recommended ?? "—", r.Tender.ReportStatus ?? "—",
                r.Tender.Approvals.ToString(), string.IsNullOrWhiteSpace(r.Tender.OfficerSummary) ? "Pending" : "Submitted",
                r.Tender.Decision?.Decision ?? r.Tender.ReportStatus ?? "—",
                r.Tender.Decision?.Comments ?? (manager ? null : r.Tender.ManagerSummary) ?? "—",
                ReportRows.Date(r.Tender.Decision?.DecidedAt), $"{r.Tender.TopYes} / {r.Tender.TopNo}", r.Outcome,
                ReportRows.Date(r.Tender.AwardedAt), r.Tender.HasContract ? "Available" : (scope.IsAdmin || manager) && r.Outcome == "Awarded" ? "Generate" : "—"],
                r.Tender.HasContract || (scope.IsAdmin || manager) && r.Outcome == "Awarded" ? $"/TenderContracts/Details/{r.Tender.TenderId}" : null)), kpis, chart);
    }
}

public sealed class BidsEvaluationReport(ApplicationDbContext db) : IReport
{
    public string Key => "bids-evaluation";
    public string Title => "Bids & evaluation";
    public string Description => "Compare offers, committee scores, and compliance exclusions.";
    public string Icon => "bar-chart";
    public IReadOnlyList<string> DefaultRoles => [RoleNames.SourcingOfficer, RoleNames.EvaluationCommittee, RoleNames.SourcingManager, RoleNames.TopCommittee];
    public IReadOnlyList<string> Filters => ["tender", "date", "supplier", "status"];
    public IReadOnlyList<string> StatusOptions => ["Compliant", "Excluded", "NonCompliant", "Rejected", "Withdrawn"];
    public async Task<ReportResult> RunAsync(ReportQuery query, ReportScope scope, CancellationToken ct)
    {
        var tenders = ReportRows.Tenders(db, scope).Where(t => query.TenderId == null || t.TenderId == query.TenderId);
        var bids = db.Bids.AsNoTracking().Where(b => b.SubmittedAt != null &&
            tenders.Any(t => t.TenderId == b.TenderId) &&
            (query.SupplierId == null || b.Invitation.SupplierProfileId == query.SupplierId));
        if (query.From is { } from) bids = bids.Where(b => b.SubmittedAt >= from.ToDateTime(TimeOnly.MinValue));
        if (query.To is { } to) bids = bids.Where(b => b.SubmittedAt <= to.ToDateTime(TimeOnly.MaxValue));
        var data = await bids.OrderBy(b => b.TenderId).ThenBy(b => b.BidId).Select(b => new
        {
            b.TenderId, b.BidId, Tender = b.Invitation.Tender.Title, b.Currency, b.Status, b.ComplianceStatus,
            Supplier = b.Invitation.SupplierProfile.CompanyName, b.TaxAmount, b.AdditionalCosts, b.DeliveryDays,
            Subtotal = b.BidItems.Sum(i => (decimal?)(i.Quantity * i.UnitPrice)) ?? 0,
            Excluded = b.ComplianceStatus == "NonCompliant" || b.Status == "Rejected" || b.Status == "Withdrawn",
            b.ComplianceNotes, b.ComplianceReviewedAt,
            Reviewer = b.ComplianceReviewedByUser == null ? null : b.ComplianceReviewedByUser.FullName,
            Financial = b.Evaluations.Where(e => e.Status == "Approved" && e.ApprovedAt != null && e.FinancialMark != null && e.TechnicalMark != null).Average(e => (decimal?)e.FinancialMark),
            Technical = b.Evaluations.Where(e => e.Status == "Approved" && e.ApprovedAt != null && e.FinancialMark != null && e.TechnicalMark != null).Average(e => (decimal?)e.TechnicalMark),
            Weight = b.Invitation.Tender.CommitteeFinancialWeight,
            Recommended = b.Invitation.Tender.EvaluationReport != null &&
                b.Invitation.Tender.EvaluationReport.RecommendedBidId == b.BidId,
            ReportId = b.Invitation.Tender.EvaluationReport == null ? (int?)null : b.Invitation.Tender.EvaluationReport.EvaluationReportId
        }).ToListAsync(ct);
        var comparable = data.Where(b => !b.Excluded).GroupBy(b => (b.TenderId, b.Currency))
            .ToDictionary(g => g.Key, g => (Low: g.Min(b => b.Subtotal + b.TaxAmount + b.AdditionalCosts),
                High: g.Max(b => b.Subtotal + b.TaxAmount + b.AdditionalCosts)));
        var spreads = comparable.GroupBy(x => x.Key.Currency).Select(g => ReportRows.Money(g.Average(x => x.Value.High - x.Value.Low), g.Key));
        var tenderCount = data.Select(b => b.TenderId).Distinct().Count();
        var kpis = new ReportKpi[] {
            new("Offers received", data.Count.ToString()), new("Compliant", data.Count(b => !b.Excluded && b.ComplianceStatus == "Compliant").ToString(), "status:Compliant"),
            new("Excluded", data.Count(b => b.Excluded).ToString(), "status:Excluded"),
            new("Average offers per tender", tenderCount == 0 ? "—" : ((decimal)data.Count / tenderCount).ToString("N1")),
            new("Average spread", comparable.Count == 0 ? "—" : string.Join(" · ", spreads))
        };
        var chart = ReportRows.Counts("Offers by compliance", data.Select(b => b.Excluded ? "Excluded" : b.ComplianceStatus ?? "Pending")).Item2;
        var status = ReportRows.ScopeFilter(query, "status:");
        if (status.Length == 0) status = query.Status ?? "";
        var selected = data.Where(b => status switch
        {
            "" => true,
            "Excluded" => b.Excluded,
            "Compliant" => !b.Excluded && b.ComplianceStatus == "Compliant",
            _ => status == b.Status || status == b.ComplianceStatus
        });
        return ReportRows.Result(query,
            ["Tender", "Supplier", "Items", "Tax", "Extra costs", "Total", "Above lowest", "Delivery days", "Compliance", "Financial avg", "Technical avg", "Overall avg", "Financial / technical weight", "Recommended", "Exclusion reason", "Reviewed by", "Reviewed (UTC)"],
            selected.Select(b =>
            {
                var total = b.Subtotal + b.TaxAmount + b.AdditionalCosts;
                var overall = b.Financial + b.Technical;
                return new ReportRow([b.Tender, b.Supplier, ReportRows.Money(b.Subtotal, b.Currency), ReportRows.Money(b.TaxAmount, b.Currency),
                    ReportRows.Money(b.AdditionalCosts, b.Currency), ReportRows.Money(total, b.Currency),
                    !b.Excluded && comparable.TryGetValue((b.TenderId, b.Currency), out var bounds)
                        ? ReportRows.Money(total - bounds.Low, b.Currency) : "—",
                    b.DeliveryDays.ToString(), b.Excluded && b.Status is "Rejected" or "Withdrawn" ? b.Status : b.ComplianceStatus ?? b.Status,
                    b.Financial?.ToString("N2") ?? "—", b.Technical?.ToString("N2") ?? "—", overall?.ToString("N2") ?? "—",
                    b.Weight is decimal weight ? $"{weight:0.##}% / {100 - weight:0.##}%" : "—", b.Recommended ? "Yes" : "—",
                    b.ComplianceNotes ?? "—", b.Reviewer ?? "—", ReportRows.Date(b.ComplianceReviewedAt)],
                    b.ReportId != null && scope.Role == RoleNames.TopCommittee && !scope.IsAdmin ? $"/TopCommittee/UnifiedReportPdf/{b.TenderId}" :
                    b.ReportId != null && scope.Role == RoleNames.EvaluationCommittee && !scope.IsAdmin ? $"/EvaluationCommittee/ReportPdf/{b.TenderId}" :
                    b.ReportId != null && scope.Role == RoleNames.SourcingOfficer && !scope.IsAdmin ? $"/SourcingOfficer/EvaluationReportPdf?tenderId={b.TenderId}" :
                    b.ReportId != null && scope.Role == RoleNames.SourcingManager && !scope.IsAdmin ? $"/SourcingManager/SubmissionPdf/{b.ReportId}" :
                    b.ReportId != null && scope.IsAdmin ? $"/Admin/TenderEvaluationPdf/{b.TenderId}" : null);
            }), kpis, chart);
    }
}

public sealed class SuppliersReport(ApplicationDbContext db, SupplierActivityQuery activity, IConfiguration config) : IReport
{
    public string Key => "suppliers";
    public string Title => "Suppliers";
    public string Description => "Supplier registration, document readiness, and tender activity.";
    public string Icon => "people";
    public IReadOnlyList<string> DefaultRoles => [RoleNames.SourcingManager, RoleNames.TopCommittee];
    public IReadOnlyList<string> Filters => ["date", "supplier", "tender", "status", "search"];
    public IReadOnlyList<string> StatusOptions => ["Approved", "Pending", "Suspended", "Rejected", "ChangesRequested"];

    public async Task<ReportResult> RunAsync(ReportQuery query, ReportScope scope, CancellationToken ct)
    {
        var tenders = ReportRows.Tenders(db, scope).Where(t => query.TenderId == null || t.TenderId == query.TenderId);
        var tenderIds = tenders.Select(t => t.TenderId);
        var selectedId = int.TryParse(ReportRows.ScopeFilter(query, "supplier:"), out var drilled) ? drilled : query.SupplierId;
        var suppliers = await db.SupplierProfiles.AsNoTracking().Where(s =>
                (selectedId == null || s.SupplierProfileId == selectedId) &&
                (query.Search == null || s.CompanyName.Contains(query.Search)) &&
                ((scope.IsAdmin && query.TenderId == null) || s.Invitations.Any(i => tenderIds.Contains(i.TenderId))))
            .OrderBy(s => s.CompanyName).ThenBy(s => s.SupplierProfileId)
            .Select(s => new
            {
                s.SupplierProfileId, s.CompanyName, s.Category, s.Status,
                Documents = s.SupplierDocuments.Select(d => new { d.ExpiresOn, d.VerificationStatus }).ToList(),
                Invitations = s.Invitations.Where(i => tenderIds.Contains(i.TenderId))
                    .Select(i => new { i.SentAt, i.TenderId, Submitted = i.Bids.Where(b => b.SubmittedAt != null).Min(b => b.SubmittedAt) }).ToList()
            }).ToListAsync(ct);
        var history = await activity.ReadAsync(tenderIds, selectedId, ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var warning = config.GetValue("Reports:DocumentExpiryWarningDays", 30);
        var docs = suppliers.SelectMany(s => s.Documents).ToArray();
        var kpis = new List<ReportKpi> { new("Registered", suppliers.Count.ToString()) };
        kpis.AddRange(StatusOptions.Select(status => new ReportKpi(status, suppliers.Count(s => s.Status == status).ToString(), "status:" + status)));
        kpis.AddRange(new[]
        {
            new ReportKpi("Documents expired", docs.Count(d => DocumentExpiry.State(d.ExpiresOn, today, warning) == "Expired").ToString(), "document:Expired"),
            new ReportKpi("Documents expiring soon", docs.Count(d => DocumentExpiry.State(d.ExpiresOn, today, warning) == "Expiring soon").ToString(), "document:Expiring soon"),
            new ReportKpi("Awaiting verification", docs.Count(d => d.VerificationStatus == "Pending").ToString(), "document:Pending")
        });
        var (_, chart) = ReportRows.Counts("Suppliers by status", suppliers.Select(s => s.Status));
        var statusFilter = ReportRows.ScopeFilter(query, "status:");
        var documentFilter = ReportRows.ScopeFilter(query, "document:");
        var filtered = suppliers.Where(s => (string.IsNullOrEmpty(query.Status) || s.Status == query.Status) &&
            (statusFilter.Length == 0 || s.Status == statusFilter) &&
            (documentFilter.Length == 0 || s.Documents.Any(d => documentFilter == "Pending"
                ? d.VerificationStatus == "Pending" : DocumentExpiry.State(d.ExpiresOn, today, warning) == documentFilter))).ToArray();

        if (drilled > 0)
        {
            var rows = filtered.SelectMany(s => history[s.SupplierProfileId].Where(b => ReportRows.Within(b.SubmittedAt, query))
                .OrderByDescending(b => b.SubmittedAt).Select(b => new ReportRow(
                    [s.CompanyName, b.Title, ReportRows.Date(b.SubmittedAt), b.Outcome, ReportRows.Money(b.Total, b.Currency)],
                    scope.IsAdmin ? $"/Admin/SupplierActivity?supplierId={drilled}" : null)));
            return ReportRows.Result(query, ["Supplier", "Tender", "Submitted (UTC)", "Outcome", "Total"], rows, kpis, chart);
        }

        return ReportRows.Result(query,
            ["Supplier", "Category", "Status", "Verified / total", "Next expiry", "Invited", "Submitted", "Compliant", "Awarded", "Win rate", "Avg. response (days)"],
            filtered.Select(s =>
            {
                var active = history[s.SupplierProfileId].Where(b => ReportRows.Within(b.SubmittedAt, query)).ToArray();
                var invited = s.Invitations.Where(i => ReportRows.Within(i.SentAt, query)).ToArray();
                var award = active.Count(b => b.Outcome == "Awarded");
                var next = s.Documents.Where(d => d.ExpiresOn >= today).Min(d => d.ExpiresOn);
                var response = invited.Where(i => i.Submitted >= i.SentAt).Select(i => (double?)(i.Submitted!.Value - i.SentAt).TotalDays).Average();
                var expiry = next ?? s.Documents.Max(d => d.ExpiresOn);
                return new ReportRow([s.CompanyName, s.Category ?? "—", s.Status,
                    $"{s.Documents.Count(d => d.VerificationStatus == "Verified")} / {s.Documents.Count}",
                    scope.IsAdmin ? $"{expiry?.ToString("dd MMM yyyy") ?? "—"} · {DocumentExpiry.State(expiry, today, warning)}" : "—",
                    invited.Select(i => i.TenderId).Distinct().Count().ToString(), active.Length.ToString(),
                    active.Count(b => b.Compliance == "Compliant").ToString(), award.ToString(),
                    active.Length == 0 ? "—" : ((decimal)award / active.Length).ToString("P1"), response?.ToString("N1") ?? "—"],
                    scope.IsAdmin ? $"/Admin/SupplierActivity?supplierId={s.SupplierProfileId}" : null);
            }), kpis, chart);
    }
}

public sealed class TopCommitteeHubReport(TopCommitteeReports reports) : IReport
{
    public string Key => "top-committee";
    public string Title => "Top Committee statistics";
    public string Description => "Tender decisions and supplier dealing frequency.";
    public string Icon => "graph-up";
    public IReadOnlyList<string> DefaultRoles => [RoleNames.TopCommittee];
    public IReadOnlyList<string> Filters => ["date", "status", "search"];
    public IReadOnlyList<string> StatusOptions => TopCommitteeReports.Statuses;

    public async Task<ReportResult> RunAsync(ReportQuery query, ReportScope scope, CancellationToken ct)
    {
        var filter = new CommitteeReportFilter
        {
            FromDate = query.From, ToDate = query.To, Search = query.Search, Status = query.Status,
            Decision = ReportRows.ScopeFilter(query, "decision:")
        };
        var actor = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, scope.UserId), new Claim(ClaimTypes.Role, scope.Role)], "Report"));
        var page = await reports.GetHubAsync(actor, filter, scope.IsAdmin, ct);
        var summary = page.Summary;
        var kpis = new ReportKpi[]
        {
            new("Assigned", summary.Assigned.ToString()), new("Matching", summary.Matching.ToString()),
            new("Pending", summary.Pending.ToString(), "status:AwaitingCommitteeDecision"),
            new("Awarded", summary.Awarded.ToString(), "status:Awarded"), new("Rejected", summary.Rejected.ToString(), "outcome:Rejected"),
            new("Returned", summary.Returned.ToString(), "status:AwaitingManagerReview"), new("My decisions", summary.MyDecisions.ToString()),
            new("Supplier statistics", page.Suppliers.Count.ToString(), "table:suppliers")
        };
        var (_, chart) = ReportRows.Counts("Tender status", page.Rows.Select(r => r.Status));
        if (query.Drill == "table:suppliers")
            return ReportRows.Result(query, ["Supplier", "Status", "Invited", "Dealings", "Awarded", "Win rate", "Dealings per month"],
                page.Suppliers.Select(s => new ReportRow([s.SupplierName, s.Status, s.Invited.ToString(), s.Dealings.ToString(),
                    s.AwardedTenders.ToString(), s.WinRate.ToString("P1"), s.Frequency.ToString("N1")])), kpis, chart);
        var status = ReportRows.ScopeFilter(query, "status:");
        return ReportRows.Result(query, ["Tender", "Status", "Created (UTC)", "Nominated bid", "Awarded bid", "Approvals", "Rejected", "My decision", "Decision date"],
            page.Rows.Where(r => (status.Length == 0 || r.Status == status) && (query.Drill != "outcome:Rejected" || r.Rejected))
                .Select(r => new ReportRow([r.Title, r.Status, ReportRows.Date(r.CreatedAt), r.NominatedBidId?.ToString() ?? "—",
                    r.AwardedBidId?.ToString() ?? "—", r.Approvals.ToString(), r.Rejected ? "Yes" : "No",
                    r.MyDecision ?? "—", ReportRows.Date(r.DecisionAt)],
                    scope.IsAdmin ? null : $"/TopCommittee/TenderDetails/{r.TenderId}")), kpis, chart);
    }
}

public sealed class SupplierActivityQuery(ApplicationDbContext db)
{
    public async Task<ILookup<int, SupplierTenderActivity>> ReadAsync(IQueryable<int> tenderIds,
        int? supplierId, CancellationToken ct)
    {
        var bids = await db.Bids.AsNoTracking().Where(b => b.SubmittedAt != null && tenderIds.Contains(b.TenderId))
            .Where(b => supplierId == null || b.Invitation.SupplierProfileId == supplierId)
            .Select(b => new
            {
                b.Invitation.SupplierProfileId, b.TenderId, b.BidId, b.SubmittedAt, b.Status, b.ComplianceStatus, b.Currency,
                Total = (b.BidItems.Sum(i => (decimal?)(i.Quantity * i.UnitPrice)) ?? 0) + b.TaxAmount + b.AdditionalCosts,
                b.Invitation.Tender.Title, TenderStatus = b.Invitation.Tender.Status, b.Invitation.Tender.AwardedBidId,
                RejectedByCommittee = b.Invitation.Tender.TenderApprovals.Any(a => a.Stage == "Top" && a.Decision == "Rejected")
            }).ToListAsync(ct);
        return bids.GroupBy(b => new { b.SupplierProfileId, b.TenderId })
            .Select(group => group.OrderByDescending(b => b.BidId == b.AwardedBidId)
                .ThenByDescending(b => b.SubmittedAt).ThenByDescending(b => b.BidId).First())
            .ToLookup(b => b.SupplierProfileId, b => new SupplierTenderActivity(b.TenderId, b.Title, b.BidId, b.SubmittedAt!.Value,
                b.TenderStatus == "Awarded" && b.AwardedBidId == b.BidId ? "Awarded" :
                b.Status == "Withdrawn" ? "Withdrawn" :
                b.TenderStatus == "Cancelled" && !b.RejectedByCommittee ? "Cancelled" :
                b.RejectedByCommittee || b.TenderStatus == "Awarded" || b.Status is "Rejected" or "NonCompliant" ||
                b.ComplianceStatus == "NonCompliant" ? "Rejected" : "Pending", b.Total, b.Currency, b.ComplianceStatus));
    }
}

public static class DocumentExpiry
{
    public static string State(DateOnly? expires, DateOnly today, int warningDays) =>
        expires is null ? "No expiry date" : expires < today ? "Expired" :
        expires.Value.DayNumber - today.DayNumber <= Math.Max(0, warningDays) ? "Expiring soon" : "Valid";
}
