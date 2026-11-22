using System.Globalization;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Services.Reports.Hub;

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
