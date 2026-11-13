using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.TopCommittee;
namespace SourcingTenderManagement.Services.TopCommittee;

public sealed class TopCommitteeHistory(ApplicationDbContext db, TopCommitteeAccessService access)
{
    public async Task<CommitteeHistoryPage> GetAsync(ClaimsPrincipal actor, CommitteeHistoryFilter filter, CancellationToken ct = default)
    {
        var memberId = await access.RequireRoleAsync(actor, RoleNames.TopCommittee, ct);
        Validator.ValidateObject(filter, new ValidationContext(filter), true);
        if ((!string.IsNullOrEmpty(filter.Status) && !TopCommitteeReports.Statuses.Contains(filter.Status)) ||
            (!string.IsNullOrEmpty(filter.Decision) && filter.Decision is not ("Approved" or "Rejected")))
            throw new ValidationException("Choose an existing tender status and Approved or Rejected decision.");
        var query = db.TenderApprovals.AsNoTracking().Where(v => v.Stage == "Top" && v.UserId == memberId &&
            (v.Decision == "Approved" || v.Decision == "Rejected") &&
            v.Tender.CommitteeAssignments.Any(a => a.CommitteeType == "Top" && a.UserId == memberId));
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            query = query.Where(v => v.Tender.Title.Contains(search));
        }
        if (!string.IsNullOrEmpty(filter.Status)) query = query.Where(v => v.Tender.Status == filter.Status);
        if (!string.IsNullOrEmpty(filter.Decision)) query = query.Where(v => v.Decision == filter.Decision);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(v => v.DecidedAt).ThenByDescending(v => v.TenderApprovalId)
            .Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
            .Select(v => new CommitteeHistoryRow(v.TenderApprovalId, v.TenderId, v.Tender.Title, v.Tender.Status,
                v.Decision, v.DecidedAt, v.Tender.EvaluationReport == null ? null : v.Tender.EvaluationReport.RecommendedBidId,
                v.Tender.AwardedBidId, v.Tender.TenderApprovals.Count(a => a.Stage == "Top" && a.Decision == "Approved"),
                v.Tender.TenderApprovals.Any(a => a.Stage == "Top" && a.Decision == "Rejected"), v.Tender.AwardedAt)).ToListAsync(ct);
        return new(filter, rows, total);
    }
}
