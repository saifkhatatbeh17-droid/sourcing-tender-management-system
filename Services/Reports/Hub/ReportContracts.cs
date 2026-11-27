namespace SourcingTenderManagement.Services.Reports.Hub;

// The shared shapes used by every report in the Reports hub: the query, the user's scope, KPIs, columns and rows.
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
