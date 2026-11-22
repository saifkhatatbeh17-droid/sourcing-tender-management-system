using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Services.Reports.Data;

namespace SourcingTenderManagement.Services.Reports.Hub;

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

        var facts = await rows.Select(tender => new
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
        }).ToListAsync(ct);
        var invited = facts.Sum(tender => (long)tender.Invited);
        var received = facts.Sum(tender => (long)tender.Received);
        var rate = invited == 0 ? "—" : (100m * received / invited).ToString("F1", CultureInfo.InvariantCulture) + "%";
        var kpis = new List<ReportKpi> { new("Total tenders", facts.Count.ToString(CultureInfo.InvariantCulture)) };
        foreach (var status in StatusOptions)
            kpis.Add(new(status, facts.Count(tender => tender.Status == status).ToString(CultureInfo.InvariantCulture), "status:" + status));
        kpis.Add(new("Supplier participation rate", rate, "activity:received", "Submitted non-withdrawn offers / invitations × 100, using the selected date range and supplier."));
        kpis.Add(new("Invitations", invited.ToString(CultureInfo.InvariantCulture), "chart:activity"));
        kpis.Add(new("Received offers", received.ToString(CultureInfo.InvariantCulture), "activity:received"));
        kpis.Add(new("Tenders with zero offers", facts.Count(tender => tender.ClosesAt < now && !tender.HasAnyOffer).ToString(CultureInfo.InvariantCulture), "activity:zero-offers"));
        kpis.Add(new("Delayed evaluations", "Unavailable", Hint: "No evaluation due date or stage-start timestamp is recorded."));
        kpis.Add(new($"Evaluation age warnings (>{warningDays} days)", facts.Count(tender => tender.Status == "UnderEvaluation" && tender.ClosesAt < warningBefore)
            .ToString(CultureInfo.InvariantCulture), "activity:evaluation-age", "Age since tender closure; not a missed evaluation deadline."));
        var showActivity = query.Drill?.StartsWith("activity:", StringComparison.Ordinal) == true || query.Drill == "chart:activity";
        var chart = showActivity
            ? new ReportChart("Tender activity", "bar", [
                new("With received offers", facts.Count(tender => tender.Received > 0), "activity:received"),
                new("Invited, no received offers", facts.Count(tender => tender.Invited > 0 && tender.Received == 0), "activity:invited-no-offers"),
                new("No invitations or received offers", facts.Count(tender => tender.Invited == 0 && tender.Received == 0), "activity:no-invitations")])
            : new ReportChart("Tenders by status", "bar", StatusOptions.Select(status => new ReportChartPoint(status,
                facts.Count(tender => tender.Status == status), "status:" + status)).ToList());

        var filtered = facts.AsEnumerable();
        if (query.Drill?.StartsWith("status:", StringComparison.Ordinal) == true)
            filtered = filtered.Where(tender => tender.Status == query.Drill[7..]);
        filtered = query.Drill switch
        {
            "activity:received" => filtered.Where(tender => tender.Received > 0),
            "activity:invited-no-offers" => filtered.Where(tender => tender.Invited > 0 && tender.Received == 0),
            "activity:no-invitations" => filtered.Where(tender => tender.Invited == 0 && tender.Received == 0),
            "activity:zero-offers" => filtered.Where(tender => tender.ClosesAt < now && !tender.HasAnyOffer),
            "activity:evaluation-age" => filtered.Where(tender => tender.Status == "UnderEvaluation" && tender.ClosesAt < warningBefore),
            _ => filtered
        };
        var ordered = filtered.OrderByDescending(tender => tender.CreatedAt).ThenByDescending(tender => tender.TenderId).ToList();
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
        if (export && ordered.Count > ReportExport.MaximumRows) notes.Add(ReportExport.LimitNote);
        var tableRows = ordered.Skip(export ? 0 : (query.Page - 1) * 25).Take(export ? ReportExport.MaximumRows : 25)
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
            "Invited Suppliers", "Received Offers", "Compliant Offers", "Awarded Supplier", "Evaluation Status", "Days since close (under evaluation)", "Evaluation Delay"], tableRows, ordered.Count), notes);
    }

    private static string Date(DateTime value) => value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
}
