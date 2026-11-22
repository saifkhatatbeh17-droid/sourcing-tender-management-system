using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Services.Reports.Hub;

namespace SourcingTenderManagement.Services.Reports.Data;

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
