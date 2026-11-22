using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Services.Reports.Hub;

namespace SourcingTenderManagement.Services.Reports.Data;

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
