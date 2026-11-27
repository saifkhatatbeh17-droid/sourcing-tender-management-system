using System.Security.Claims;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.TopCommittee;
using SourcingTenderManagement.Services.Reports.Hub;
using SourcingTenderManagement.Services.TopCommittee;

namespace SourcingTenderManagement.Services.Reports.Data;

// Reports hub: the CEO & Chiefs decisions report.
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
            new("Awarded", summary.Awarded.ToString(), "status:Awarded"), new("Rejected", summary.Rejected
                .ToString(), "outcome:Rejected"),
            new("Returned", summary.Returned.ToString(), "status:AwaitingManagerReview"), new("My decisions", summary.MyDecisions
                .ToString()),
            new("Supplier statistics", page.Suppliers.Count.ToString(), "table:suppliers")
        };
        var (_, chart) = ReportRows.Counts("Tender status", page.Rows.Select(r => r.Status));
        if (query.Drill == "table:suppliers")
            return ReportRows.Result(query, ["Supplier", "Status", "Invited", "Dealings", "Awarded", "Win rate", "Dealings per month"],
                page.Suppliers.Select(s => new ReportRow([s.SupplierName, s.Status, s.Invited.ToString(), s.Dealings.ToString(),
                    s.AwardedTenders.ToString(), s.WinRate.ToString("P1"), s.Frequency.ToString("N1")])), kpis, chart);
        var status = ReportRows.ScopeFilter(query, "status:");
        return ReportRows.Result(query, ["Tender", "Status", "Created (UTC)", "Nominated bid", "Awarded bid", "Approvals", "Rejected", "My decision", "Decision date"],
            page.Rows.Where(r => (status.Length == 0 || r.Status == status)
                && (query.Drill != "outcome:Rejected" || r.Rejected))
                .Select(r => new ReportRow([r.Title, r.Status, ReportRows.Date(r.CreatedAt), r.NominatedBidId?.ToString() ?? "—",
                    r.AwardedBidId?.ToString() ?? "—", r.Approvals.ToString(), r.Rejected ? "Yes" : "No",
                    r.MyDecision ?? "—", ReportRows.Date(r.DecisionAt)],
                    scope.IsAdmin ? null : $"/TopCommittee/TenderDetails/{r.TenderId}")), kpis, chart);
    }
}
