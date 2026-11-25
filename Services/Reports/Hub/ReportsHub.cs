using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Models.Reports;
using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;
using SourcingTenderManagement.Services.Reports.Data;
using System.Globalization;
using ClosedXML.Excel;
using MigraDoc.DocumentObjectModel;
using SourcingTenderManagement.Services.TopCommittee;

namespace SourcingTenderManagement.Services.Reports.Hub;

public sealed record ReportQuery(
    DateOnly? From = null, DateOnly? To = null,
    int? DepartmentId = null, int? SupplierId = null, int? TenderId = null,
    string? Status = null, string? Search = null,
    string? Drill = null,          // e.g. "status:Approved", "department:3"; set by KPI/chart clicks
    int Page = 1, bool ForExport = false);

public sealed record ReportScope(string UserId, string Role, int? DepartmentId, bool IsAdmin);

public sealed record ReportKpi(string Label, string Value, string? Drill = null, string? Hint = null);
public sealed record ReportChartPoint(string Label, decimal Value, string? Drill = null);
public sealed record ReportChart(string Title, string Kind, IReadOnlyList<ReportChartPoint> Points); // Kind: "bar" | "donut"
public sealed record ReportRow(IReadOnlyList<string> Cells, string? Link = null);
public sealed record ReportTable(IReadOnlyList<string> Columns, IReadOnlyList<ReportRow> Rows, int Total);
public sealed record ReportResult(IReadOnlyList<ReportKpi> Kpis, ReportChart? Chart, ReportTable Table, IReadOnlyList<string> Notes);

public interface IReport
{
    string Key { get; }                         // url slug, e.g. "suppliers"
    string Title { get; }
    string Description { get; }                 // one line for the hub card
    string Icon { get; }                        // icon name used by the hub card
    IReadOnlyList<string> DefaultRoles { get; } // roles allowed when the Admin has not changed access; Admin is always allowed
    IReadOnlyList<string> Filters { get; }      // which filter controls to show: "date","department","supplier","tender","status","search"
    IReadOnlyList<string> StatusOptions { get; }
    Task<ReportResult> RunAsync(ReportQuery query, ReportScope scope, CancellationToken ct);
}

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

// Request-scoped cache: shared sidebar branches reuse the registry's permission decision.
public sealed class ReportNavigation(ReportRegistry registry, ILogger<ReportNavigation> logger)
{
    private readonly Dictionary<string, Task<bool>> visibility = new(StringComparer.Ordinal);

    public Task<bool> HasReportsAsync(string role, CancellationToken ct = default)
    {
        if (!visibility.TryGetValue(role, out var result))
            visibility[role] = result = LoadAsync(role, ct);
        return result;
    }

    private async Task<bool> LoadAsync(string role, CancellationToken ct)
    {
        try { return (await registry.GetAvailableAsync(role, ct)).Count > 0; }
        catch (SqlException exception) when (Controllers.ReportsController.MissingAccessTable(exception))
        {
            logger.LogWarning("Reports navigation hidden: the ReportAccessRules table is unavailable.");
            return false;
        }
    }
}

public static class ReportQueryValidation
{
    public static (ReportQuery Query, List<string> Messages) Normalize(IReport report, ReportQuery query)
    {
        var messages = new List<string>();
        bool Supports(string name) => report.Filters.Contains(name, StringComparer.Ordinal);
        int? PositiveId(int? value)
        {
            if (value is null or > 0) return value;
            messages.Add("Invalid record filters were cleared. Choose a record from the list.");
            return null;
        }

        var from = Supports("date") ? query.From : null;
        var to = Supports("date") ? query.To : null;
        if (from.HasValue && to.HasValue && (to < from ||
            (from.Value.Year <= 9994 && to.Value > from.Value.AddYears(5))))
        {
            messages.Add("Choose an end date on or after the start date, with a range of at most five years. The date filter was cleared.");
            from = null;
            to = null;
        }

        var status = Supports("status") ? query.Status?.Trim() : null;
        if (!string.IsNullOrEmpty(status) && !report.StatusOptions.Contains(status, StringComparer.Ordinal))
        {
            messages.Add("The selected status is not supported by this report. All statuses are shown.");
            status = null;
        }

        var search = Supports("search") ? query.Search?.Trim() : null;
        if (search?.Length > 200)
        {
            messages.Add("Search text was shortened to 200 characters.");
            search = search[..200];
        }

        var drill = query.Drill?.Trim();
        if (!string.IsNullOrEmpty(drill) && (drill.Length > 160 || drill.Any(char.IsControl) ||
            !Regex.IsMatch(drill, @"\A[a-z][a-z-]*:[^:]+\z")))
        {
            messages.Add("The drill-down filter was invalid and has been cleared.");
            drill = null;
        }
        if (drill != null)
        {
            var parts = drill.Split(':', 2);
            if (parts[0] is "department" or "supplier" or "tender" &&
                (!int.TryParse(parts[1], out var id) || id <= 0))
            {
                messages.Add("The drill-down record was invalid and has been cleared.");
                drill = null;
            }
            if (parts[0] == "decision" && parts[1] is not ("Approved" or "Rejected" or "NotVoted"))
            {
                messages.Add("The decision drill-down was unsupported and has been cleared.");
                drill = null;
            }
        }

        if (drill != null && report is PurchaseRequestsReport)
        {
            var parts = drill.Split(':', 2);
            if (parts[0] != "department" && (parts[0] != "status" || !report.StatusOptions.Contains(parts[1], StringComparer.Ordinal)))
            {
                messages.Add("The purchase request drill-down was unsupported and has been cleared.");
                drill = null;
            }
        }

        if (drill != null && report is TendersReport)
        {
            var supported = drill.StartsWith("status:", StringComparison.Ordinal) && report.StatusOptions.Contains(drill[7..], StringComparer.Ordinal) ||
                drill is "chart:status" or "chart:activity" or "activity:received" or "activity:invited-no-offers" or "activity:no-invitations" or "activity:zero-offers" or "activity:evaluation-age";
            if (!supported)
            {
                messages.Add("The tender drill-down was unsupported and has been cleared.");
                drill = null;
            }
        }

        return (new ReportQuery(from, to,
            Supports("department") ? PositiveId(query.DepartmentId) : null,
            Supports("supplier") ? PositiveId(query.SupplierId) : null,
            Supports("tender") ? PositiveId(query.TenderId) : null,
            string.IsNullOrEmpty(status) ? null : status,
            string.IsNullOrEmpty(search) ? null : search,
            string.IsNullOrEmpty(drill) ? null : drill,
            Math.Clamp(query.Page, 1, int.MaxValue / 25), ForExport: false), messages);
    }
}

public sealed class ReportFilterOptions(ApplicationDbContext db)
{
    public async Task<ReportFilterChoices> LoadAsync(IReport report, ReportScope scope, CancellationToken ct)
    {
        // Use the same tender visibility queries as Person B's reports, including committee assignments.
        var tenders = ReportRows.Tenders(db, scope);
        if (!scope.IsAdmin && scope.Role == RoleNames.Department)
            tenders = db.Tenders.Where(tender => scope.DepartmentId != null &&
                tender.PurchaseRequest.DepartmentId == scope.DepartmentId);
        if (report is TendersReport) tenders = TendersReport.VisibleTenders(db, scope);
        var tenderIds = tenders.Select(tender => tender.TenderId);
        IReadOnlyList<ReportFilterOption> departments = [], suppliers = [], tenderOptions = [];

        if (report.Filters.Contains("department"))
        {
            var rows = db.Departments.AsNoTracking();
            if (report is PurchaseRequestsReport)
            {
                var requests = PurchaseRequestsReport.VisibleRequests(db, scope);
                rows = rows.Where(department => requests.Any(request => request.DepartmentId == department.DepartmentId));
            }
            else if (report is TendersReport)
            {
                rows = rows.Where(department => tenders.Any(tender => tender.PurchaseRequest.DepartmentId == department.DepartmentId));
            }
            else if (!scope.IsAdmin)
            {
                var requests = db.PurchaseRequests.AsQueryable();
                requests = scope.Role switch
                {
                    RoleNames.Department => requests.InDepartment(scope.DepartmentId),
                    RoleNames.SourcingOfficer => requests.ReviewableBy(scope.UserId),
                    _ => requests.Where(request => tenders.Any(tender => tender.PurchaseRequestId == request.PurchaseRequestId))
                };
                rows = rows.Where(department => requests.Any(request => request.DepartmentId == department.DepartmentId));
            }
            departments = await rows.OrderBy(department => department.Name)
                .Select(department => new ReportFilterOption(department.DepartmentId, department.Name)).ToListAsync(ct);
        }

        if (report.Filters.Contains("supplier"))
        {
            var rows = db.SupplierProfiles.AsNoTracking();
            if (!scope.IsAdmin || report is TendersReport)
                rows = rows.Where(supplier => supplier.Invitations.Any(invitation => tenderIds.Contains(invitation.TenderId)));
            suppliers = await rows.OrderBy(supplier => supplier.CompanyName)
                .Select(supplier => new ReportFilterOption(supplier.SupplierProfileId, supplier.CompanyName)).ToListAsync(ct);
        }

        if (report.Filters.Contains("tender"))
            tenderOptions = await tenders.AsNoTracking().OrderByDescending(tender => tender.CreatedAt)
                .ThenByDescending(tender => tender.TenderId)
                .Select(tender => new ReportFilterOption(tender.TenderId, "#" + tender.TenderId + " · " + tender.Title))
                .ToListAsync(ct);

        return new ReportFilterChoices(departments, suppliers, tenderOptions);
    }
}

public sealed class PurchaseRequestsReport(ApplicationDbContext db) : IReport
{
    public string Key => "purchase-requests";
    public string Title => "Purchase Requests";
    public string Description => "Request volumes, review times, and returned requests by department.";
    public string Icon => "file";
    public IReadOnlyList<string> DefaultRoles => [RoleNames.Admin, RoleNames.Department, RoleNames.SourcingOfficer, RoleNames.SourcingManager];
    public IReadOnlyList<string> Filters => ["date", "department", "status", "search"];
    public IReadOnlyList<string> StatusOptions => ["Submitted", "UnderReview", "Approved", "Returned", "Rejected", "ConvertedToTender", "Draft"];

    internal static IQueryable<PurchaseRequest> VisibleRequests(ApplicationDbContext db, ReportScope scope)
    {
        var rows = db.PurchaseRequests.AsNoTracking().Where(request => request.Status != "Draft");
        if (string.IsNullOrWhiteSpace(scope.UserId) || scope.IsAdmin != (scope.Role == RoleNames.Admin))
            return rows.Where(_ => false);
        return scope.Role switch
        {
            RoleNames.Admin or RoleNames.SourcingManager => rows,
            RoleNames.Department => rows.InDepartment(scope.DepartmentId),
            RoleNames.SourcingOfficer => rows.ReviewableBy(scope.UserId),
            _ => rows.Where(_ => false)
        };
    }

    public async Task<ReportResult> RunAsync(ReportQuery query, ReportScope scope, CancellationToken ct)
    {
        var export = query.ForExport;
        var (normalized, messages) = ReportQueryValidation.Normalize(this, query);
        query = normalized with { ForExport = export };
        var rows = VisibleRequests(db, scope);
        if (query.From.HasValue)
        {
            var from = query.From.Value.ToDateTime(TimeOnly.MinValue);
            rows = rows.Where(request => request.SubmittedAt >= from);
        }
        if (query.To.HasValue)
        {
            var through = query.To.Value.ToDateTime(TimeOnly.MaxValue);
            rows = rows.Where(request => request.SubmittedAt <= through);
        }
        if (query.DepartmentId.HasValue) rows = rows.Where(request => request.DepartmentId == query.DepartmentId);
        if (query.Status != null) rows = rows.Where(request => request.Status == query.Status);
        if (query.Search != null)
        {
            var search = query.Search;
            var requestNumber = int.TryParse(search.TrimStart('#'), out var number) ? number : 0;
            rows = rows.Where(request => request.Title.Contains(search) || request.Department.Name.Contains(search) ||
                request.RequestedByUser.FullName.Contains(search) || request.PurchaseRequestId == requestNumber);
        }

        // Fetch only aggregate facts here; the detailed table stays paginated in SQL.
        var facts = await rows.Select(request => new { request.Status, request.DepartmentId,
            Department = request.Department.Name, request.SubmittedAt, request.ReviewedAt }).ToListAsync(ct);
        var kpis = new List<ReportKpi> { new("Total purchase requests", facts.Count.ToString(CultureInfo.InvariantCulture)) };
        foreach (var status in StatusOptions.Where(status => status != "Draft"))
            kpis.Add(new(status, facts.Count(request => request.Status == status).ToString(CultureInfo.InvariantCulture), "status:" + status));
        var durations = facts.Select(request => ReviewDays(request.Status, request.SubmittedAt, request.ReviewedAt)).Where(days => days.HasValue).ToList();
        kpis.Add(new("Average review time", AverageDays(durations), Hint: "ReviewedAt minus SubmittedAt for finalized reviews with both dates."));
        var returned = facts.Count(request => request.Status == "Returned");
        kpis.Add(new("Returned request rate", (facts.Count == 0 ? 0 : returned * 100d / facts.Count).ToString("F1", CultureInfo.InvariantCulture) + "%",
            "status:Returned", "Currently Returned requests / total requests."));
        var departments = facts.GroupBy(request => new { request.DepartmentId, request.Department })
            .OrderBy(group => group.Key.Department).ThenBy(group => group.Key.DepartmentId).ToList();
        foreach (var group in departments)
        {
            var times = group.Select(request => ReviewDays(request.Status, request.SubmittedAt, request.ReviewedAt)).Where(days => days.HasValue).ToList();
            if (times.Count > 0) kpis.Add(new("Average review · " + group.Key.Department, AverageDays(times), "department:" + group.Key.DepartmentId));
        }
        var chart = new ReportChart("Purchase Requests by Department", "bar", departments.Select(group =>
            new ReportChartPoint(group.Key.Department, group.Count(), "department:" + group.Key.DepartmentId)).ToList());

        if (query.Drill?.StartsWith("status:", StringComparison.Ordinal) == true)
        {
            var status = query.Drill[7..];
            rows = rows.Where(request => request.Status == status);
        }
        if (query.Drill?.StartsWith("department:", StringComparison.Ordinal) == true && int.TryParse(query.Drill[11..], out var departmentId))
            rows = rows.Where(request => request.DepartmentId == departmentId);
        var total = await rows.CountAsync(ct);
        var records = await rows.OrderByDescending(request => request.SubmittedAt ?? request.CreatedAt)
            .ThenByDescending(request => request.PurchaseRequestId)
            .Skip(query.ForExport ? 0 : (query.Page - 1) * 25).Take(query.ForExport ? ReportExport.MaximumRows : 25)
            .Select(request => new { request.PurchaseRequestId, request.Title, Department = request.Department.Name,
                Requester = request.RequestedByUser.FullName, request.Priority, request.Status, request.SubmittedAt, request.ReviewedAt })
            .ToListAsync(ct);
        var notes = new List<string>(messages)
        {
            "Drafts are not saved in this system.",
            "Dates are UTC. Date filters use the latest saved SubmittedAt. Review time includes Approved, Returned, Rejected, and ConvertedToTender requests with both dates; invalid negative durations are excluded.",
            "KPIs and the chart use the applied filters. Drill-down narrows the results table and exports. Returned rate reflects the current status, not historical returns."
        };
        if (durations.Count == 0) notes.Add("No completed reviews with valid timestamps are available for the average review time.");
        if (scope.Role is RoleNames.Admin or RoleNames.SourcingManager)
            notes.Add("Request details links are unavailable for this role because the existing request details pages authorize Department or Sourcing Officer only.");
        if (query.ForExport && total > ReportExport.MaximumRows) notes.Add(ReportExport.LimitNote);
        return new(kpis, chart, new(["Request Number", "Title", "Department", "Requester", "Priority", "Status", "Submitted Date (UTC)", "Reviewed Date (UTC)", "Approval Time"],
            records.Select(request => new ReportRow(["#" + request.PurchaseRequestId, request.Title, request.Department, request.Requester,
                request.Priority, request.Status, Date(request.SubmittedAt), Date(request.ReviewedAt),
                FormatDays(ReviewDays(request.Status, request.SubmittedAt, request.ReviewedAt))],
                scope.Role switch
                {
                    RoleNames.Department => "/Department/Details/" + request.PurchaseRequestId,
                    RoleNames.SourcingOfficer => "/SourcingOfficer/Details/" + request.PurchaseRequestId,
                    _ => null
                })).ToList(), total), notes);
    }

    private static double? ReviewDays(string status, DateTime? submitted, DateTime? reviewed) =>
        status is "Approved" or "Returned" or "Rejected" or "ConvertedToTender" && submitted.HasValue && reviewed >= submitted
            ? (reviewed!.Value - submitted.Value).TotalDays : null;
    private static string AverageDays(List<double?> times) => FormatDays(times.Count == 0 ? null : times.Average());
    private static string FormatDays(double? days) => days.HasValue ? days.Value.ToString("F1", CultureInfo.InvariantCulture) + " days" : "—";
    private static string Date(DateTime? date) => date?.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) ?? "—";
}

public sealed class TendersReport(ApplicationDbContext db, IConfiguration config) : IReport
{
    public string Key => "tenders";
    public string Title => "Tenders";
    public string Description => "Tender progress, supplier participation, and evaluation age across your workspace.";
    public string Icon => "bar-chart";
    public IReadOnlyList<string> DefaultRoles => [RoleNames.Admin, RoleNames.SourcingOfficer, RoleNames.SourcingManager, RoleNames.EvaluationCommittee, RoleNames.TopCommittee];
    public IReadOnlyList<string> Filters => ["date", "department", "supplier", "status", "search"];
    public IReadOnlyList<string> StatusOptions => ["Published", "Open", "Closed", "UnderEvaluation", "AwaitingOfficerSummary", "AwaitingManagerReview", "AwaitingCommitteeDecision", "Awarded", "Cancelled"];

    internal static IQueryable<Tender> VisibleTenders(ApplicationDbContext db, ReportScope scope)
    {
        if (string.IsNullOrWhiteSpace(scope.UserId) || scope.IsAdmin != (scope.Role == RoleNames.Admin) || scope.Role == RoleNames.Supplier)
            return db.Tenders.Where(_ => false);
        if (scope.Role == RoleNames.Department)
            return db.Tenders.Where(tender => scope.DepartmentId != null && tender.PurchaseRequest.DepartmentId == scope.DepartmentId);
        // Reuse the existing reports' ownership, manager history and committee-assignment boundaries.
        return ReportRows.Tenders(db, scope);
    }

    public async Task<ReportResult> RunAsync(ReportQuery query, ReportScope scope, CancellationToken ct)
    {
        var export = query.ForExport;
        var (normalized, messages) = ReportQueryValidation.Normalize(this, query);
        query = normalized with { ForExport = export };
        var now = DateTime.UtcNow;
        var setting = config["Reports:EvaluationDelayDays"];
        var warningDays = 7;
        if (setting != null && (!int.TryParse(setting, NumberStyles.Integer, CultureInfo.InvariantCulture, out warningDays) || warningDays < 1 || warningDays > 36500))
        {
            warningDays = 7;
            messages.Add("The evaluation age warning setting was invalid; the 7-day default was used.");
        }
        var warningBefore = now.AddDays(-warningDays);
        var from = query.From?.ToDateTime(TimeOnly.MinValue);
        var through = query.To?.ToDateTime(TimeOnly.MaxValue);
        var rows = VisibleTenders(db, scope).AsNoTracking();
        if (from.HasValue) rows = rows.Where(tender => tender.CreatedAt >= from);
        if (through.HasValue) rows = rows.Where(tender => tender.CreatedAt <= through);
        if (query.DepartmentId.HasValue) rows = rows.Where(tender => tender.PurchaseRequest.DepartmentId == query.DepartmentId);
        if (query.SupplierId.HasValue) rows = rows.Where(tender => tender.Invitations.Any(invitation => invitation.SupplierProfileId == query.SupplierId));
        if (query.Status != null) rows = rows.Where(tender => tender.Status == query.Status);
        if (query.Search != null)
        {
            var search = query.Search;
            var number = int.TryParse(search.TrimStart('#'), out var id) ? id : 0;
            rows = rows.Where(tender => tender.Title.Contains(search) || tender.PurchaseRequest.Department.Name.Contains(search) ||
                (tender.Category != null && tender.Category.Contains(search)) || tender.TenderId == number);
        }

        var facts = rows.Select(tender => new
        {
            tender.TenderId, tender.Title, tender.Category, tender.Status, tender.CreatedAt, tender.OpensAt, tender.ClosesAt,
            Department = tender.PurchaseRequest.Department.Name,
            Invited = tender.Invitations.Count(invitation => (!query.SupplierId.HasValue || invitation.SupplierProfileId == query.SupplierId) &&
                (!from.HasValue || invitation.SentAt >= from) && (!through.HasValue || invitation.SentAt <= through)),
            Received = tender.Invitations.Where(invitation => !query.SupplierId.HasValue || invitation.SupplierProfileId == query.SupplierId)
                .SelectMany(invitation => invitation.Bids).Count(bid => bid.SubmittedAt != null && bid.Status != "Withdrawn" &&
                    (!from.HasValue || bid.SubmittedAt >= from) && (!through.HasValue || bid.SubmittedAt <= through)),
            Compliant = tender.Invitations.Where(invitation => !query.SupplierId.HasValue || invitation.SupplierProfileId == query.SupplierId)
                .SelectMany(invitation => invitation.Bids).Count(bid => bid.SubmittedAt != null && bid.Status != "Withdrawn" && bid.ComplianceStatus == "Compliant" &&
                    (!from.HasValue || bid.SubmittedAt >= from) && (!through.HasValue || bid.SubmittedAt <= through)),
            HasAnyOffer = tender.Invitations.SelectMany(invitation => invitation.Bids).Any(bid => bid.SubmittedAt != null && bid.Status != "Withdrawn"),
            AwardedSupplier = tender.Bid == null ? null : tender.Bid.Invitation.SupplierProfile.CompanyName,
            EvaluationStatus = tender.EvaluationReport == null ? null : tender.EvaluationReport.Status,
            ManagerReviewId = db.EvaluationReports.InManagerReview().Where(report => report.TenderId == tender.TenderId)
                .Select(report => (int?)report.EvaluationReportId).FirstOrDefault()
        });
        // Aggregate in SQL and materialize only the requested page (or bounded export).
        var totals = await facts.GroupBy(_ => 1).Select(group => new
        {
            Total = group.Count(),
            Invited = group.Sum(tender => (long)tender.Invited),
            Received = group.Sum(tender => (long)tender.Received),
            ZeroOffers = group.Count(tender => tender.ClosesAt < now && !tender.HasAnyOffer),
            AgeWarnings = group.Count(tender => tender.Status == "UnderEvaluation" && tender.ClosesAt < warningBefore),
            WithOffers = group.Count(tender => tender.Received > 0),
            InvitedNoOffers = group.Count(tender => tender.Invited > 0 && tender.Received == 0),
            NoInvitations = group.Count(tender => tender.Invited == 0 && tender.Received == 0)
        }).SingleOrDefaultAsync(ct);
        var statusCounts = await rows.GroupBy(tender => tender.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() }).ToDictionaryAsync(item => item.Status, item => item.Count, ct);
        var invited = totals?.Invited ?? 0;
        var received = totals?.Received ?? 0;
        var rate = invited == 0 ? "—" : (100m * received / invited).ToString("F1", CultureInfo.InvariantCulture) + "%";
        var kpis = new List<ReportKpi> { new("Total tenders", (totals?.Total ?? 0).ToString(CultureInfo.InvariantCulture)) };
        foreach (var status in StatusOptions)
            kpis.Add(new(status, statusCounts.GetValueOrDefault(status).ToString(CultureInfo.InvariantCulture), "status:" + status));
        kpis.Add(new("Supplier participation rate", rate, "activity:received", "Submitted non-withdrawn offers / invitations × 100, using the selected date range and supplier."));
        kpis.Add(new("Invitations", invited.ToString(CultureInfo.InvariantCulture), "chart:activity"));
        kpis.Add(new("Received offers", received.ToString(CultureInfo.InvariantCulture), "activity:received"));
        kpis.Add(new("Tenders with zero offers", (totals?.ZeroOffers ?? 0).ToString(CultureInfo.InvariantCulture), "activity:zero-offers"));
        kpis.Add(new("Delayed evaluations", "Unavailable", Hint: "No evaluation due date or stage-start timestamp is recorded."));
        kpis.Add(new($"Evaluation age warnings (>{warningDays} days)", (totals?.AgeWarnings ?? 0)
            .ToString(CultureInfo.InvariantCulture), "activity:evaluation-age", "Age since tender closure; not a missed evaluation deadline."));
        var showActivity = query.Drill?.StartsWith("activity:", StringComparison.Ordinal) == true || query.Drill == "chart:activity";
        var chart = showActivity
            ? new ReportChart("Tender activity", "bar", [
                new("With received offers", (totals?.WithOffers ?? 0), "activity:received"),
                new("Invited, no received offers", (totals?.InvitedNoOffers ?? 0), "activity:invited-no-offers"),
                new("No invitations or received offers", (totals?.NoInvitations ?? 0), "activity:no-invitations")])
            : new ReportChart("Tenders by status", "bar", StatusOptions.Select(status => new ReportChartPoint(status,
                statusCounts.GetValueOrDefault(status), "status:" + status)).ToList());

        var filtered = facts;
        if (query.Drill?.StartsWith("status:", StringComparison.Ordinal) == true)
        {
            var drillStatus = query.Drill[7..];
            filtered = filtered.Where(tender => tender.Status == drillStatus);
        }
        filtered = query.Drill switch
        {
            "activity:received" => filtered.Where(tender => tender.Received > 0),
            "activity:invited-no-offers" => filtered.Where(tender => tender.Invited > 0 && tender.Received == 0),
            "activity:no-invitations" => filtered.Where(tender => tender.Invited == 0 && tender.Received == 0),
            "activity:zero-offers" => filtered.Where(tender => tender.ClosesAt < now && !tender.HasAnyOffer),
            "activity:evaluation-age" => filtered.Where(tender => tender.Status == "UnderEvaluation" && tender.ClosesAt < warningBefore),
            _ => filtered
        };
        var total = await filtered.CountAsync(ct);
        var page = await filtered.OrderByDescending(tender => tender.CreatedAt).ThenByDescending(tender => tender.TenderId)
            .Skip(export ? 0 : (query.Page - 1) * 25).Take(export ? ReportExport.MaximumRows : 25).ToListAsync(ct);
        var notes = new List<string>(messages)
        {
            "Dates are UTC. Date filters select tenders by CreatedAt and restrict counted invitations by SentAt and offers by SubmittedAt. Supplier filters restrict these counts to that supplier.",
            "Participation = submitted, non-withdrawn offers / invitations × 100. A missing invitation denominator is unavailable. Offers without SubmittedAt and withdrawn offers are excluded, matching AI Intelligence's received-offer definition; event date filters can produce a rate above 100%.",
            "Zero-offer tenders have a passed closing date and no submitted, non-withdrawn offers from any supplier at any time. Unlike AI Intelligence's general zero-offer count, future deadlines are excluded.",
            $"Actual evaluation delay is unavailable: the schema has no evaluation due date or stage-start timestamp. The >{warningDays}-day age warning uses ClosesAt while Status is UnderEvaluation, not an SLA or a proven delay. Completed evaluations are not flagged.",
            "KPIs and charts use the applied filters. Chart categories and KPI drill-down narrow the table and exports; changing chart mode alone does not narrow records."
        };
        if (scope.Role == RoleNames.EvaluationCommittee) notes.Add("This report uses existing report visibility: only tenders assigned to your Evaluation Committee account.");
        if (scope.Role is RoleNames.Admin or RoleNames.Department)
            notes.Add("No tender details link is shown: the existing tender workflow pages do not authorize this role.");
        if (export && total > ReportExport.MaximumRows) notes.Add(ReportExport.LimitNote);
        var tableRows = page
            .Select(tender => new ReportRow(["#" + tender.TenderId, tender.Title, tender.Department, tender.Category ?? "Uncategorized", tender.Status,
                Date(tender.CreatedAt), Date(tender.OpensAt), Date(tender.ClosesAt), tender.Invited.ToString(CultureInfo.InvariantCulture), tender.Received.ToString(CultureInfo.InvariantCulture),
                tender.Compliant.ToString(CultureInfo.InvariantCulture), tender.AwardedSupplier ?? "—", tender.EvaluationStatus ?? "No report recorded",
                tender.Status == "UnderEvaluation" ? Math.Max(0, (now - tender.ClosesAt).TotalDays).ToString("F1", CultureInfo.InvariantCulture) : "—",
                tender.Status == "UnderEvaluation" ? "Unavailable: no evaluation due date" : "—"],
                scope.Role switch
                {
                    RoleNames.SourcingOfficer => "/SourcingOfficer/Offers?tenderId=" + tender.TenderId,
                    RoleNames.EvaluationCommittee => "/EvaluationCommittee/Evaluations?id=" + tender.TenderId,
                    RoleNames.TopCommittee => "/TopCommittee/TenderDetails/" + tender.TenderId,
                    RoleNames.SourcingManager when tender.ManagerReviewId.HasValue => "/SourcingManager/Submissions?id=" + tender.ManagerReviewId,
                    _ => null
                })).ToList();
        return new(kpis, chart, new(["Tender Number", "Tender Title", "Department", "Category", "Status", "Created Date (UTC)", "Opens (UTC)", "Closing Date (UTC)",
            "Invited Suppliers", "Received Offers", "Compliant Offers", "Awarded Supplier", "Evaluation Status", "Days since close (under evaluation)", "Evaluation Delay"], tableRows, total), notes);
    }

    private static string Date(DateTime value) => value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
}

public sealed class ReportExport
{
    public const int MaximumRows = 5000;
    public const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string LimitNote = "Export limited to the first 5,000 matching records. Narrow the filters to export a smaller set.";

    public static ReportResult Prepare(ReportResult result)
    {
        if (result.Table.Rows.Count < Math.Min(MaximumRows, result.Table.Total))
            throw new InvalidOperationException("The report returned an incomplete export. No partial file can be downloaded.");

        var limited = result.Table.Rows.Count > MaximumRows || result.Table.Total > MaximumRows;
        return result with
        {
            Table = result.Table with { Rows = result.Table.Rows.Take(MaximumRows).ToList() },
            Notes = limited ? result.Notes.Append(LimitNote).Distinct().ToList() : result.Notes
        };
    }

    public byte[] Excel(IReport report, ReportQuery query, ReportResult source, DateTime generatedAtUtc)
    {
        var result = Prepare(source);
        using var workbook = new XLWorkbook();
        var summary = workbook.AddWorksheet("Summary");
        summary.Cell(1, 1).Value = report.Title;
        summary.Range(1, 1, 1, 2).Merge();
        summary.Cell(1, 1).Style.Font.Bold = true;
        summary.Cell(1, 1).Style.Font.FontSize = 18;
        summary.Cell(2, 1).Value = "Generated UTC";
        summary.Cell(2, 2).Value = generatedAtUtc;
        summary.Cell(2, 2).Style.DateFormat.Format = "dd mmm yyyy hh:mm:ss \"UTC\"";
        summary.Cell(3, 1).Value = "Exported records";
        summary.Cell(3, 2).Value = $"{result.Table.Rows.Count:N0} of {result.Table.Total:N0} matching records";
        var row = 4;
        WriteHeading(summary, row++, "Applied filter", "Value");
        foreach (var filter in AppliedFilters(query))
        {
            summary.Cell(row, 1).Value = filter.Key;
            summary.Cell(row++, 2).Value = filter.Value;
        }
        row++;
        WriteHeading(summary, row++, "KPI", "Value");
        foreach (var kpi in result.Kpis)
        {
            summary.Cell(row, 1).Value = kpi.Label;
            WriteValue(summary.Cell(row++, 2), kpi.Value);
        }
        row++;
        WriteHeading(summary, row++, "Report notes", "");
        foreach (var note in result.Notes)
        {
            summary.Cell(row, 1).Value = note;
            summary.Range(row, 1, row, 2).Merge().Style.Alignment.WrapText = true;
            row++;
        }
        summary.Columns(1, 2).AdjustToContents(1, row, 12, 60);
        summary.Rows(1, row).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        summary.Columns(1, 2).Style.Alignment.WrapText = true;

        var data = workbook.AddWorksheet("Data");
        var columns = result.Table.Columns;
        if (columns.Count > 0)
        {
            WriteHeading(data, 1, columns.ToArray());
            for (var index = 0; index < result.Table.Rows.Count; index++)
            {
                var cells = result.Table.Rows[index].Cells;
                for (var column = 0; column < columns.Count; column++)
                    WriteValue(data.Cell(index + 2, column + 1), column < cells.Count ? cells[column] : "");
            }
            data.Columns(1, columns.Count).AdjustToContents(1, result.Table.Rows.Count + 1, 10, 60);
            data.Range(1, 1, Math.Max(1, result.Table.Rows.Count + 1), columns.Count).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
            data.Columns(1, columns.Count).Style.Alignment.WrapText = true;
        }
        data.SheetView.FreezeRows(1);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public byte[] Pdf(IReport report, ReportQuery query, ReportResult source, DateTime generatedAtUtc)
    {
        var result = Prepare(source);
        var document = TopCommitteeReportExport.CreatePdfDocument(report.Title);
        var section = document.LastSection;
        // Wider data tables use a larger landscape sheet while retaining the existing report style.
        var wide = result.Table.Columns.Count > 10;
        if (wide) section.PageSetup.PageFormat = PageFormat.A3;
        var title = section.AddParagraph(report.Title);
        title.Format.Font.Size = 20;
        title.Format.Font.Bold = true;
        title.Format.Font.Color = Color.FromRgb(29, 85, 96);
        TopCommitteeReportExport.AddPdfText(section, "Generated " + generatedAtUtc.ToString("dd MMM yyyy HH:mm:ss 'UTC'", CultureInfo.InvariantCulture));
        var footer = section.Footers.Primary.AddParagraph();
        footer.AddText("TIRYAQ | Page ");
        footer.AddPageField();
        footer.AddText(" of ");
        footer.AddNumPagesField();

        TopCommitteeReportExport.Heading(section, "Applied filters");
        foreach (var filter in AppliedFilters(query))
            TopCommitteeReportExport.AddPdfText(section, filter.Key + " · " + filter.Value);
        if (result.Kpis.Count > 0)
        {
            TopCommitteeReportExport.Heading(section, "Summary");
            var kpis = TopCommitteeReportExport.Table(section, ["KPI", "Value"], [16, 10.7]);
            foreach (var kpi in result.Kpis)
                TopCommitteeReportExport.AddRow(kpis, [kpi.Label, kpi.Value]);
        }
        TopCommitteeReportExport.Heading(section, "Data");
        TopCommitteeReportExport.AddPdfText(section, $"{result.Table.Rows.Count:N0} of {result.Table.Total:N0} matching records exported.");
        if (result.Table.Columns.Count == 0 || result.Table.Rows.Count == 0)
            TopCommitteeReportExport.AddPdfText(section, "No matching records.");
        else
        {
            var width = (wide ? 39d : 26.7d) / result.Table.Columns.Count;
            var headers = result.Table.Columns.Select(header => string.Join("\n",
                TopCommitteeReportExport.WrapPdfText(header, Unit.FromCentimeter(width).Point - 10,
                    fontStyle: PdfSharp.Drawing.XFontStyleEx.Bold))).ToArray();
            var table = TopCommitteeReportExport.Table(section, headers,
                Enumerable.Repeat(width, headers.Length).ToArray());
            foreach (var record in result.Table.Rows)
                TopCommitteeReportExport.AddRow(table, Enumerable.Range(0, headers.Length)
                    .Select(index => index < record.Cells.Count ? record.Cells[index] : "").ToArray());
        }
        if (result.Notes.Count > 0)
        {
            TopCommitteeReportExport.Heading(section, "Report notes");
            foreach (var note in result.Notes) TopCommitteeReportExport.AddPdfText(section, note);
        }
        return TopCommitteeReportExport.RenderPdf(document);
    }

    private static IReadOnlyList<KeyValuePair<string, string>> AppliedFilters(ReportQuery query)
    {
        var filters = new List<KeyValuePair<string, string>>();
        if (query.From.HasValue) filters.Add(new("From UTC", query.From.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)));
        if (query.To.HasValue) filters.Add(new("To UTC", query.To.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)));
        if (query.DepartmentId.HasValue) filters.Add(new("Department ID", query.DepartmentId.Value.ToString(CultureInfo.InvariantCulture)));
        if (query.SupplierId.HasValue) filters.Add(new("Supplier ID", query.SupplierId.Value.ToString(CultureInfo.InvariantCulture)));
        if (query.TenderId.HasValue) filters.Add(new("Tender ID", query.TenderId.Value.ToString(CultureInfo.InvariantCulture)));
        if (query.Status != null) filters.Add(new("Status", query.Status));
        if (query.Search != null) filters.Add(new("Search", query.Search));
        if (query.Drill != null) filters.Add(new("Drill-down", query.Drill));
        if (filters.Count == 0) filters.Add(new("Filters", "All records in your authorized workspace"));
        return filters;
    }

    private static void WriteHeading(IXLWorksheet sheet, int row, params string[] values)
    {
        for (var column = 0; column < values.Length; column++) sheet.Cell(row, column + 1).Value = values[column];
        var heading = sheet.Range(row, 1, row, values.Length);
        heading.Style.Font.Bold = true;
        heading.Style.Fill.BackgroundColor = XLColor.FromHtml("#1d5560");
        heading.Style.Font.FontColor = XLColor.White;
        heading.Style.Alignment.WrapText = true;
    }

    private static void WriteValue(IXLCell cell, string value)
    {
        // Preserve leading-zero identifiers and values beyond Excel's 15-digit numeric precision.
        var digits = value.Where(char.IsDigit).ToArray();
        if (digits.Length <= 15 && Regex.IsMatch(value, @"\A-?(?:0|[1-9]\d*|[1-9]\d{0,2}(?:,\d{3})+)(?:\.\d+)?\z") &&
            decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
            cell.Value = number;
        else
            cell.Value = value; // Assign text, never a spreadsheet formula.
    }
}
