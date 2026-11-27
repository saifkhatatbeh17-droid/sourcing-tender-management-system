using System.Globalization;
using Microsoft.AspNetCore.WebUtilities;
using SourcingTenderManagement.Services.Reports.Hub;

namespace SourcingTenderManagement.Models.Reports;

// Data shown on the Reports hub pages (the list of reports and one report with its filters).
public sealed record ReportsHubPage(ReportScope Scope, IReadOnlyList<IReport> Reports);
public sealed record ReportFilterOption(int Id, string Label);
public sealed record ReportFilterChoices(
    IReadOnlyList<ReportFilterOption> Departments,
    IReadOnlyList<ReportFilterOption> Suppliers,
    IReadOnlyList<ReportFilterOption> Tenders);

public sealed record ReportPageModel(IReport Report, ReportScope Scope, ReportQuery Query,
    ReportResult Result, ReportFilterChoices Choices, IReadOnlyList<string> Messages, string ReportPath)
{
    public const int PageSize = 25;
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(Result.Table.Total / (double)PageSize));
    public bool Supports(string filter) => Report.Filters.Contains(filter, StringComparer.Ordinal);

    public string Link(int? page = null, string? drill = null, bool clearDrill = false)
    {
        var selectedPage = page ?? (drill != null || clearDrill ? 1 : Query.Page);
        return BuildLink(ReportPath, selectedPage, clearDrill ? null : drill ?? Query.Drill);
    }

    public string ExportLink(string format) => BuildLink(ReportPath + "/export", 1, Query.Drill, format);

    private string BuildLink(string path, int page, string? drill, string? format = null)
    {
        return QueryHelpers.AddQueryString(path, new Dictionary<string, string?>
        {
            ["from"] = Query.From?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["to"] = Query.To?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["departmentId"] = Query.DepartmentId?.ToString(CultureInfo.InvariantCulture),
            ["supplierId"] = Query.SupplierId?.ToString(CultureInfo.InvariantCulture),
            ["tenderId"] = Query.TenderId?.ToString(CultureInfo.InvariantCulture),
            ["status"] = Query.Status,
            ["search"] = Query.Search,
            ["drill"] = drill,
            ["page"] = page.ToString(CultureInfo.InvariantCulture),
            ["format"] = format
        });
    }
}
