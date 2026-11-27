using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Services.Reports.Hub;

// Reports hub: the 'Purchase requests' report.
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
            kpis.Add(new(status, facts.Count(request => request.Status == status)
                .ToString(CultureInfo.InvariantCulture), "status:" + status));
        var durations = facts.Select(request => ReviewDays(request.Status, request.SubmittedAt, request.ReviewedAt))
            .Where(days => days.HasValue)
            .ToList();
        kpis.Add(new("Average review time", AverageDays(durations), Hint: "ReviewedAt minus SubmittedAt for finalized reviews with both dates."));
        var returned = facts.Count(request => request.Status == "Returned");
        kpis.Add(new("Returned request rate", (facts.Count == 0 ? 0 : returned * 100d / facts.Count).ToString("F1", CultureInfo.InvariantCulture) + "%",
            "status:Returned", "Currently Returned requests / total requests."));
        var departments = facts.GroupBy(request => new { request.DepartmentId, request.Department })
            .OrderBy(group => group.Key.Department).ThenBy(group => group.Key.DepartmentId).ToList();
        foreach (var group in departments)
        {
            var times = group.Select(request => ReviewDays(request.Status, request.SubmittedAt, request.ReviewedAt))
                .Where(days => days.HasValue)
                .ToList();
            if (times.Count > 0) kpis.Add(new("Average review · " + group.Key.Department, AverageDays(times), "department:" + group.Key.DepartmentId));
        }
        var chart = new ReportChart("Purchase Requests by Department", "bar", departments.Select(group =>
            new ReportChartPoint(group.Key.Department, group.Count(), "department:" + group.Key.DepartmentId)).ToList());

        if (query.Drill?.StartsWith("status:", StringComparison.Ordinal) == true)
        {
            var status = query.Drill[7..];
            rows = rows.Where(request => request.Status == status);
        }
        if (query.Drill?.StartsWith("department:", StringComparison.Ordinal) == true
            && int.TryParse(query.Drill[11..], out var departmentId))
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
            "Dates are UTC. Date filters use the latest saved SubmittedAt. Review time includes Approved, " +
                    "Returned, Rejected, and ConvertedToTender requests with both dates; invalid negative durations are excluded.",
            "KPIs and the chart use the applied filters. Drill-down narrows the results table and exports. " +
                    "Returned rate reflects the current status, not historical returns."
        };
        if (durations.Count == 0) notes.Add("No completed reviews with valid timestamps are available for the average review time.");
        if (scope.Role is RoleNames.Admin or RoleNames.SourcingManager)
            notes.Add("Request details links are unavailable for this role because the existing request details pages " +
                    "authorize Department or Sourcing Officer only.");
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
        status is "Approved" or "Returned" or "Rejected" or "ConvertedToTender"
            && submitted.HasValue
            && reviewed >= submitted
            ? (reviewed!.Value - submitted.Value).TotalDays : null;
    private static string AverageDays(List<double?> times) => FormatDays(times.Count == 0 ? null : times.Average());
    private static string FormatDays(double? days) => days.HasValue ? days.Value.ToString("F1", CultureInfo.InvariantCulture) + " days" : "—";
    private static string Date(DateTime? date) => date?.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) ?? "—";
}
