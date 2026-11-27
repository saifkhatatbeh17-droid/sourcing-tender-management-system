using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Services.Reports.Hub;

namespace SourcingTenderManagement.Services.Reports.Data;

// Reports hub: the 'Approvals' report (who approved or rejected what, and when).
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
