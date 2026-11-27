using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Contracts.TopCommittee;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Models.TopCommittee;

namespace SourcingTenderManagement.Services.TopCommittee;

// Loads the CEO & Chiefs dashboard, the list of tenders to decide, and one tender's details.
public sealed class TopCommitteeQueryService(ApplicationDbContext db, TopCommitteeAccessService access)
    : ITopCommitteeQueries
{
    private static readonly HashSet<string> TenderStatuses =
    ["Draft", "Published", "Open", "Closed", "UnderEvaluation", "AwaitingOfficerSummary",
        "AwaitingManagerReview", "AwaitingCommitteeDecision", "Awarded", "Cancelled"];

    private IQueryable<Tender> AssignedTenders(string userId) => db.Tenders.AsNoTracking()
        .Where(t => t.CommitteeAssignments.Any(a => a.CommitteeType == "Top" && a.UserId == userId));

    // Committee conversations use TenderId (not the request or bid link). Other participants' message
    // contents are never returned to a member; only their unresolved count is used for the voting pause.
    private IQueryable<Clarification> RelevantClarifications() => TopCommitteeClarificationScope.Query(db);

    private IQueryable<CommitteeTenderRow> Rows(IQueryable<Tender> tenders)
    {
        var clarifications = RelevantClarifications();
        return tenders.Select(t =>
        new CommitteeTenderRow(t.TenderId, t.Title, t.Status, t.CreatedAt,
            t.EvaluationReport == null ? null : t.EvaluationReport.RecommendedBidId,
            t.EvaluationReport == null || t.EvaluationReport.Bid == null ? null :
                t.EvaluationReport.Bid.Invitation.SupplierProfile.CompanyName,
            t.TenderApprovals.Count(a => a.Stage == "Top" && a.Decision == "Approved"),
            t.TenderApprovals.Any(a => a.Stage == "Top" && a.Decision == "Rejected"),
            clarifications.Count(c => c.TenderId == t.TenderId && c.Status != "Resolved")));
    }

    public async Task<CommitteePage<CommitteeTenderRow>> GetTendersAsync(ClaimsPrincipal actor,
        CommitteeTenderFilter filter, CancellationToken cancellationToken = default)
    {
        var userId = await access.RequireRoleAsync(actor, RoleNames.TopCommittee, cancellationToken);
        ArgumentNullException.ThrowIfNull(filter);
        Validator.ValidateObject(filter, new ValidationContext(filter), validateAllProperties: true);
        if (filter.FromDate > filter.ToDate)
            throw new ValidationException("From date must not be later than to date.");
        var status = filter.Status?.Trim();
        if (!string.IsNullOrEmpty(status) && !TenderStatuses.Contains(status))
            throw new ValidationException("Unknown tender status.");

        var query = AssignedTenders(userId);
        if (!string.IsNullOrEmpty(status)) query = query.Where(t => t.Status == status);
        if (status == "AwaitingCommitteeDecision") query = query.Where(t => !t.AwardedBidId.HasValue &&
            !t.TenderApprovals.Any(a => a.Stage == "Top" && a.Decision == "Rejected"));
        var search = filter.Search?.Trim();
        if (!string.IsNullOrEmpty(search)) query = query.Where(t => t.Title.Contains(search) ||
            t.EvaluationReport != null && t.EvaluationReport.Bid != null &&
                t.EvaluationReport.Bid.Invitation.SupplierProfile.CompanyName.Contains(search));
        if (filter.FromDate is { } from)
        {
            var start = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(t => t.CreatedAt >= start);
        }
        if (filter.ToDate is { } to)
        {
            var end = to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            query = query.Where(t => t.CreatedAt <= end);
        }
        var count = await query.CountAsync(cancellationToken);
        var items = await Rows(query.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.TenderId)
                .Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)).ToListAsync(cancellationToken);
        return new CommitteePage<CommitteeTenderRow>(items, count, filter.Page, filter.PageSize);
    }

    public async Task<CommitteeDashboard> GetDashboardAsync(ClaimsPrincipal actor,
        CancellationToken cancellationToken = default)
    {
        var userId = await access.RequireRoleAsync(actor, RoleNames.TopCommittee, cancellationToken);
        var tenders = AssignedTenders(userId);
        var pending = tenders.Where(t => t.Status == "AwaitingCommitteeDecision" &&
            !t.TenderApprovals.Any(a => a.Stage == "Top" && a.Decision == "Rejected"));
        var completed = tenders.Where(t => t.Status == "Awarded" ||
            t.TenderApprovals.Any(a => a.Stage == "Top" && a.Decision == "Rejected"));
        var total = await tenders.CountAsync(cancellationToken);
        var awaiting = await pending.CountAsync(cancellationToken);
        var awarded = await tenders.CountAsync(t => t.Status == "Awarded", cancellationToken);
        var rejected = await tenders.CountAsync(t => t.TenderApprovals.Any(a => a.Stage == "Top" &&
            a.Decision == "Rejected"), cancellationToken);
        var open = await RelevantClarifications().CountAsync(c => c.Status != "Resolved" &&
            tenders.Any(t => t.TenderId == c.TenderId), cancellationToken);
        var pendingRows = await Rows(pending.OrderBy(t => t.CreatedAt).ThenBy(t => t.TenderId).Take(5))
            .ToListAsync(cancellationToken);
        var recent = await Rows(completed.OrderByDescending(t => t.AwardedAt ??
                t.TenderApprovals.Where(a => a.Stage == "Top").Max(a => (DateTime?)a.DecidedAt) ?? t.CreatedAt)
                .ThenByDescending(t => t.TenderId).Take(3)).ToListAsync(cancellationToken);
        var pendingIds = pendingRows.Select(t => t.TenderId).ToArray();
        var progress = await db.CommitteeAssignments.AsNoTracking()
            .Where(a => a.CommitteeType == "Top" && pendingIds.Contains(a.TenderId))
            .OrderBy(a => a.TenderId).ThenBy(a => a.Position)
            .Select(a => new CommitteeMemberProgress(a.TenderId, a.UserId, a.User.FullName, a.Position,
                a.User.IsActive, db.UserRoles.Any(ur => ur.UserId == a.UserId &&
                    db.Roles.Any(r => r.Id == ur.RoleId && r.Name == RoleNames.TopCommittee)),
                a.TenderApprovalCommitteeAssignmentNavigations.Where(v => v.Stage == "Top")
                    .Select(v => v.Decision).FirstOrDefault()))
            .ToListAsync(cancellationToken);
        var states = await tenders.GroupBy(t => t.TenderApprovals.Any(a => a.Stage == "Top" && a.Decision == "Rejected") ? "Rejected" :
            t.Status == "Awarded" ? "Awarded" : t.Status == "AwaitingCommitteeDecision" ? "Pending" : "Other")
            .Select(g => new { Status = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Status, g => g.Count, cancellationToken);
        var needsVote = await pending.CountAsync(t => !t.TenderApprovals.Any(a => a.Stage == "Top" && a.UserId == userId), cancellationToken);
        return new CommitteeDashboard(total, awaiting, awarded, rejected, open, pendingRows, recent, progress)
        {
            Overview = new SourcingTenderManagement.Models.DashboardOverview("Assigned tender status", "Only tenders assigned to you", "tenders", "pending",
                [new("Pending decision", states.GetValueOrDefault("Pending")), new("Awarded", states.GetValueOrDefault("Awarded"), "positive"), new("Rejected", states.GetValueOrDefault("Rejected"), "muted"), new("Other stages", states.GetValueOrDefault("Other"), "other")],
                "Your decision workload", "Current stage of your assigned tenders",
                [new("Needs your vote", needsVote), new("Waiting for other votes", awaiting - needsVote), new("Awarded", states.GetValueOrDefault("Awarded")), new("Rejected", states.GetValueOrDefault("Rejected")), new("Other stages", states.GetValueOrDefault("Other"))])
        };
    }

    public async Task<CommitteeTenderDetails> GetTenderDetailsAsync(ClaimsPrincipal actor, int tenderId,
        CancellationToken cancellationToken = default)
    {
        var userId = await access.RequireAssignmentAsync(actor, tenderId, cancellationToken);
        var tender = await DetailsQuery(userId, tenderId).SingleOrDefaultAsync(cancellationToken)
            ?? throw new CommitteeTenderNotFoundException();
        var members = await access.GetMembersAsync(tenderId, cancellationToken);
        var managers = await access.GetManagersAsync(tenderId, cancellationToken);
        var open = await RelevantClarifications().CountAsync(c => c.TenderId == tenderId && c.Status != "Resolved", cancellationToken);
        var mine = await RelevantClarifications().Where(c => c.TenderId == tenderId && c.SenderUserId == userId)
            .Include(c => c.ClarificationResponses).ThenInclude(r => r.RespondedByUser)
            .OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.ClarificationId)
            .ToListAsync(cancellationToken);
        return MapDetails(tender, members, managers, mine, open, userId);
    }

    // Kept internal so SELECT translation can be checked even when the local database has no tenders.
    internal IQueryable<Tender> DetailsQuery(string userId, int tenderId) =>
        AssignedTenders(userId).Where(t => t.TenderId == tenderId)
            .Include(t => t.PurchaseRequest).ThenInclude(r => r.Department)
            .Include(t => t.PurchaseRequest).ThenInclude(r => r.PurchaseRequestItems)
            .Include(t => t.OfferExtensions)
            .Include(t => t.EvaluationReport)
            .Include(t => t.TenderDocuments)
            .Include(t => t.TenderApprovals).ThenInclude(a => a.User)
            .Include(t => t.Invitations).ThenInclude(i => i.SupplierProfile)
            .Include(t => t.Invitations).ThenInclude(i => i.Bids).ThenInclude(b => b.BidItems).ThenInclude(i => i.PurchaseRequestItem)
            .Include(t => t.Invitations).ThenInclude(i => i.Bids).ThenInclude(b => b.BidDocuments)
            .Include(t => t.Invitations).ThenInclude(i => i.Bids).ThenInclude(b => b.Evaluations)
                .ThenInclude(e => e.CommitteeAssignment).ThenInclude(a => a.User)
            .Include(t => t.Invitations).ThenInclude(i => i.Bids).ThenInclude(b => b.Evaluations)
                .ThenInclude(e => e.EvaluationScores).ThenInclude(s => s.EvaluationCriterion)
            .AsSplitQuery();

    internal static CommitteeTenderDetails MapDetails(Tender tender, IReadOnlyList<CommitteeMember> members,
        IReadOnlyList<CommitteeManager> managers, IReadOnlyList<Clarification> ownClarifications,
        int openClarifications, string userId)
    {
        var report = tender.EvaluationReport;
        // Include submitted bids even after their status becomes Compliant/Accepted/Withdrawn.
        var bids = tender.Invitations.SelectMany(i => i.Bids.Where(b => b.SubmittedAt.HasValue && b.Status != "Draft")
            .OrderBy(b => b.BidId).Select(b => MapBid(b, i, report?.RecommendedBidId, tender.CommitteeFinancialWeight))).OrderBy(b => b.BidId).ToList();
        var nomination = tender.Invitations.SelectMany(i => i.Bids)
            .SingleOrDefault(b => b.BidId == report?.RecommendedBidId);
        var request = tender.PurchaseRequest;
        var votes = tender.TenderApprovals.Where(a => a.Stage == "Top").OrderBy(a => a.DecidedAt)
            .ThenBy(a => a.TenderApprovalId).Select(a => new CommitteeVote(a.TenderApprovalId, a.UserId,
                a.CommitteeAssignmentId, a.User.FullName, a.Decision, a.Notes, a.DecidedAt)).ToList();
        var clarifications = ownClarifications.Where(c => c.SenderUserId == userId && c.TenderId == tender.TenderId)
            .Select(c => new CommitteeClarification(c.ClarificationId, c.Subject, c.Description, c.Status,
                c.CreatedAt, c.ResolvedAt, c.ClarificationResponses.OrderBy(r => r.CreatedAt)
                    .ThenBy(r => r.ClarificationResponseId).Select(r => new CommitteeClarificationResponse(
                        r.RespondedByUserId, r.RespondedByUser.FullName, r.Message, r.CreatedAt)).ToList())).ToList();
        return new CommitteeTenderDetails(tender.TenderId, tender.Title, tender.Description, tender.Terms,
            tender.Status, tender.OpensAt, tender.ClosesAt, tender.AwardedBidId, tender.AwardedAt, tender.AwardNotes,
            new CommitteePurchaseRequest(request.PurchaseRequestId, request.Title, request.Reason, request.Department.Name,
                request.Priority, request.TargetDate, request.PurchaseRequestItems.OrderBy(i => i.PurchaseRequestItemId)
                    .Select(i => new CommitteeRequestItem(i.PurchaseRequestItemId, i.ItemName, i.Quantity, i.Unit, i.Specifications)).ToList()),
            report == null ? null : new CommitteeReport(report.EvaluationReportId, report.Status, report.RecommendedBidId,
                report.CommitteeSummary, report.OfficerSummary, report.ManagerSummary, report.SubmittedAt),
            managers.Count == 1 ? managers[0] : null, members, votes, bids,
            tender.TenderDocuments.OrderBy(d => d.TenderDocumentId).Select(d =>
                new CommitteeDocument(d.TenderDocumentId, d.OriginalFileName, null, d.FileSizeBytes)).ToList(),
            openClarifications, clarifications, TopCommitteeRules.Inspect(tender, members, managers, nomination, openClarifications, userId))
        {
            OfferRoundSummary = SourcingTenderManagement.Services.Reports.OfferRoundContext.Describe(tender),
            TopEditState = tender.TopEditState,
            TopEditRequestNotes = tender.TopEditRequestNotes,
            TopEditRequestedAt = tender.TopEditRequestedAt
        };
    }

    private static CommitteeBid MapBid(Bid bid, Invitation invitation, int? nominationId, decimal? financialWeight)
    {
        var subtotal = bid.BidItems.Sum(i => i.Quantity * i.UnitPrice);
        return new CommitteeBid(bid.BidId, invitation.SupplierProfileId, invitation.SupplierProfile.CompanyName,
            bid.Status, bid.ComplianceStatus, bid.ComplianceNotes, bid.Currency, subtotal, bid.TaxAmount,
            bid.AdditionalCosts, subtotal + bid.TaxAmount + bid.AdditionalCosts, bid.DeliveryDays, bid.PaymentTerms,
            bid.ValidUntil, bid.TechnicalDetails, bid.SubmittedAt, bid.BidId == nominationId,
            bid.BidItems.OrderBy(i => i.BidItemId).Select(i => new CommitteeBidItem(i.PurchaseRequestItemId,
                i.PurchaseRequestItem.ItemName, i.Quantity, i.UnitPrice, i.OfferedSpecifications)).ToList(),
            bid.BidDocuments.OrderBy(d => d.BidDocumentId).Select(d => new CommitteeDocument(d.BidDocumentId,
                d.OriginalFileName, d.DocumentType, d.FileSizeBytes)).ToList(),
            bid.Evaluations.Where(e => e.Status is "Submitted" or "Approved").OrderBy(e => e.EvaluationId)
                .Select(e => new CommitteeEvaluation(e.CommitteeAssignment.User.FullName, e.Status, e.Notes,
                    e.SubmittedAt, e.ApprovedAt, EvaluationScores(e, financialWeight))).ToList());
    }

    private static IReadOnlyList<CommitteeScore> EvaluationScores(SourcingTenderManagement.Models.Entities.Evaluation evaluation, decimal? financialWeight)
    {
        if (evaluation.FinancialMark.HasValue && evaluation.TechnicalMark.HasValue && financialWeight.HasValue)
            return [
                new("Financial mark", "Financial", financialWeight.Value, evaluation.FinancialMark.Value, null) { Maximum = financialWeight.Value },
                new("Technical mark", "Requirements", 100 - financialWeight.Value, evaluation.TechnicalMark.Value, null) { Maximum = 100 - financialWeight.Value }
            ];
        return evaluation.EvaluationScores.OrderBy(s => s.EvaluationCriterionId)
            .Select(s => new CommitteeScore(s.EvaluationCriterion.Name, s.EvaluationCriterion.Category, s.EvaluationCriterion.Weight, s.Score, s.Notes)).ToList();
    }
}
