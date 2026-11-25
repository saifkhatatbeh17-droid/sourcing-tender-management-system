using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Contracts.TopCommittee;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Models.TopCommittee;
using System.Text.RegularExpressions;

namespace SourcingTenderManagement.Services.TopCommittee;

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

public sealed class TopCommitteeReports(ApplicationDbContext db,TopCommitteeAccessService access)
{
 public static readonly string[] Statuses=["Draft","Published","Open","Closed","UnderEvaluation","AwaitingOfficerSummary","AwaitingManagerReview","AwaitingCommitteeDecision","Awarded","Cancelled"];
 public async Task<CommitteeReportPage> GetAsync(ClaimsPrincipal actor,CommitteeReportFilter f,bool export=false,CancellationToken ct=default)
 {
  return await ReadAsync(await access.RequireRoleAsync(actor,RoleNames.TopCommittee,ct), f, export, false, false, ct);
 }

 public async Task<CommitteeReportPage> GetHubAsync(ClaimsPrincipal actor, CommitteeReportFilter filter, bool admin, CancellationToken ct)
 {
     var id = await access.RequireRoleAsync(actor, admin ? RoleNames.Admin : RoleNames.TopCommittee, ct);
     return await ReadAsync(id, filter, false, true, admin, ct);
 }

 private async Task<CommitteeReportPage> ReadAsync(string id, CommitteeReportFilter f, bool export, bool hub, bool admin, CancellationToken ct)
 {
  Validator.ValidateObject(f,new ValidationContext(f),true);
  if(f.FromDate>f.ToDate||f.Status is {Length:>0}&&!Statuses.Contains(f.Status)||f.Decision is {Length:>0}&&f.Decision is not("Approved" or "Rejected" or "NotVoted"))throw new ValidationException("Check status, decision and date range.");
  var assigned=admin ? db.Tenders.AsNoTracking() : AssignedTenders(db,id);var q=assigned;
  if(!string.IsNullOrWhiteSpace(f.Search)){var text=f.Search.Trim();q=q.Where(t=>t.Title.Contains(text));}if(!string.IsNullOrEmpty(f.Status))q=q.Where(t=>t.Status==f.Status);
  if(f.FromDate is {} from){var start=from.ToDateTime(TimeOnly.MinValue,DateTimeKind.Utc);q=q.Where(t=>t.CreatedAt>=start);}if(f.ToDate is {} to){var end=to.ToDateTime(TimeOnly.MaxValue,DateTimeKind.Utc);q=q.Where(t=>t.CreatedAt<=end);}
  if(f.Decision=="NotVoted")q=q.Where(t=>!t.TenderApprovals.Any(v=>v.Stage=="Top"&&v.UserId==id));else if(!string.IsNullOrEmpty(f.Decision))q=q.Where(t=>t.TenderApprovals.Any(v=>v.Stage=="Top"&&v.UserId==id&&v.Decision==f.Decision));
  var count=await q.CountAsync(ct);if(export&&count>5000)throw new ValidationException("More than 5000 matching tenders. Narrow the filters before exporting.");
  var summary=new CommitteeReportSummary(await assigned.CountAsync(ct),count,await q.CountAsync(t=>t.Status=="AwaitingCommitteeDecision"&&!t.TenderApprovals.Any(v=>v.Stage=="Top"&&v.Decision=="Rejected"),ct),await q.CountAsync(t=>t.Status=="Awarded",ct),await q.CountAsync(t=>t.TenderApprovals.Any(v=>v.Stage=="Top"&&v.Decision=="Rejected"),ct),await q.CountAsync(t=>t.Status=="AwaitingManagerReview",ct),await q.CountAsync(t=>t.TenderApprovals.Any(v=>v.Stage=="Top"&&v.UserId==id),ct));
  var ordered=q.OrderByDescending(t=>t.CreatedAt).ThenByDescending(t=>t.TenderId);
  var rows=await ProjectRows(hub ? ordered : export ? ordered.Take(5000) : ordered.Skip((f.Page-1)*20).Take(20),id).ToListAsync(ct);
  // System-wide aggregates are export-only. Detail rows retain their assignment scope above.
  var stats = await ScopedSuppliersAsync(q, f.FromDate, f.ToDate, ct);
  if (!export) return new(f,summary,rows,DateTime.UtcNow) { Suppliers = stats };
  var supplierCount = await db.SupplierProfiles.AsNoTracking().CountAsync(ct);
  if (supplierCount > 10000) throw new ValidationException("More than 10000 suppliers. The comprehensive export exceeds its safe size; contact the administrator.");
  var suppliers = stats;
  var system = new CommitteeSystemStatistics(supplierCount, await db.Tenders.AsNoTracking().CountAsync(ct),
      await db.Tenders.AsNoTracking().CountAsync(t => t.Status == "Awarded",ct))
  {
      SuppliersByStatus = await db.SupplierProfiles.AsNoTracking().GroupBy(s => s.Status)
          .Select(g => new { Status = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Status, x => x.Count, ct)
  };
  return new(f,summary,rows,DateTime.UtcNow) { SystemStatistics = system, Suppliers = suppliers };
 }
 private async Task<IReadOnlyList<CommitteeSupplierStatistics>> ScopedSuppliersAsync(IQueryable<Tender> tenders,
     DateOnly? from, DateOnly? to, CancellationToken ct)
 {
     var ids = tenders.Select(t => t.TenderId);
     var rows = await db.SupplierProfiles.AsNoTracking().Where(s => s.Invitations.Any(i => ids.Contains(i.TenderId)))
         .OrderBy(s => s.CompanyName).ThenBy(s => s.SupplierProfileId)
         .Select(s => new
         {
             s.SupplierProfileId, s.CompanyName, s.Status,
             Invited = s.Invitations.Where(i => ids.Contains(i.TenderId)).Select(i => i.TenderId).Distinct().Count(),
             Dealings = s.Invitations.Where(i => ids.Contains(i.TenderId) && i.Bids.Any(b => b.SubmittedAt != null))
                 .Select(i => i.TenderId).Distinct().Count(),
             First = s.Invitations.Where(i => ids.Contains(i.TenderId)).SelectMany(i => i.Bids).Min(b => b.SubmittedAt),
             Awards = tenders.Count(t => t.Status == "Awarded" && t.Bid != null && t.Bid.TenderId == t.TenderId &&
                 t.Bid.Invitation.SupplierProfileId == s.SupplierProfileId)
         }).ToListAsync(ct);
     var today = DateOnly.FromDateTime(DateTime.UtcNow);
     return rows.Select(s => new CommitteeSupplierStatistics(s.SupplierProfileId, s.CompanyName, s.Awards)
     {
         Status = s.Status, Invited = s.Invited, Dealings = s.Dealings,
         Frequency = Frequency(s.Dealings, from ?? (s.First is DateTime first ? DateOnly.FromDateTime(first) : today), to ?? today)
     }).ToArray();
 }

 internal static decimal Frequency(int dealings, DateOnly from, DateOnly to) =>
     Math.Round(dealings / (Math.Max(1, to.DayNumber - from.DayNumber + 1) / 30.4375m), 1);

 internal static IQueryable<Tender> AssignedTenders(ApplicationDbContext context,string memberId) =>
     context.Tenders.AsNoTracking().Where(t=>t.CommitteeAssignments.Any(a=>a.UserId==memberId&&a.CommitteeType=="Top"));
 // One projection is shared by production SQL and database-free regression tests.
 internal static IQueryable<CommitteeReportRow> ProjectRows(IQueryable<Tender> tenders, string memberId) =>
     tenders.Select(t => new CommitteeReportRow(t.TenderId,t.Title,t.Status,t.CreatedAt,
         t.EvaluationReport == null ? null : t.EvaluationReport.RecommendedBidId,t.AwardedBidId,
         t.TenderApprovals.Count(v=>v.Stage=="Top"&&v.Decision=="Approved"),
         t.TenderApprovals.Any(v=>v.Stage=="Top"&&v.Decision=="Rejected"),
         t.TenderApprovals.Where(v=>v.Stage=="Top"&&v.UserId==memberId).Select(v=>v.Decision).FirstOrDefault(),
         t.TenderApprovals.Where(v=>v.Stage=="Top"&&v.UserId==memberId).Select(v=>(DateTime?)v.DecidedAt).FirstOrDefault())
     {
         AwardedAt = t.AwardedAt,
         CommitteeOutcomeAt = t.AwardedAt ?? t.TenderApprovals.Where(v=>v.Stage=="Top").Max(v=>(DateTime?)v.DecidedAt)
     });
}

public sealed class TopCommitteeNotifications(ApplicationDbContext db,TopCommitteeAccessService access)
{
 public async Task<CommitteeNotificationPage> GetAsync(ClaimsPrincipal actor,int page=1,CancellationToken ct=default)
 {
  var id=await access.RequireRoleAsync(actor,RoleNames.TopCommittee,ct);if(page<1||page>1000000)throw new ValidationException("Choose a valid notification page.");var q=db.Notifications.AsNoTracking().Where(n=>n.UserId==id);var total=await q.CountAsync(ct);var unread=await q.CountAsync(n=>!n.IsRead,ct);var rows=await q.OrderByDescending(n=>n.CreatedAt).ThenByDescending(n=>n.NotificationId).Skip((page-1)*20).Take(20).ToListAsync(ct);
  var assigned=await db.CommitteeAssignments.AsNoTracking().Where(a=>a.UserId==id&&a.CommitteeType=="Top").Select(a=>a.TenderId).ToListAsync(ct);
  return new(rows.Select(n=>{int? tender=null;var match=Regex.Match(n.RelativeUrl??"","^/TopCommittee/TenderDetails/([1-9][0-9]*)$",RegexOptions.CultureInvariant);if(match.Success&&int.TryParse(match.Groups[1].Value,out var t)&&assigned.Contains(t))tender=t;return new CommitteeNotificationItem(n.NotificationId,n.Title,n.Message,n.IsRead,n.CreatedAt,tender);}).ToList(),total,unread,page);
 }
 public async Task<bool> MarkAsync(ClaimsPrincipal actor,int notificationId,CancellationToken ct=default)
 {
  var id=await access.RequireRoleAsync(actor,RoleNames.TopCommittee,ct);
  return await db.Notifications.Where(n=>n.NotificationId==notificationId&&n.UserId==id).ExecuteUpdateAsync(s=>s.SetProperty(n=>n.IsRead,true),ct)>0;
 }
 public async Task MarkAllAsync(ClaimsPrincipal actor,CancellationToken ct=default)
 {
  var id=await access.RequireRoleAsync(actor,RoleNames.TopCommittee,ct);await db.Notifications.Where(n=>n.UserId==id&&!n.IsRead).ExecuteUpdateAsync(s=>s.SetProperty(n=>n.IsRead,true),ct);
 }
}

public static class TopCommitteeNotificationEvents
{
 private static async Task AddAsync(ApplicationDbContext db,int tenderId,IEnumerable<string> recipients,string title,CancellationToken ct)
 {
  await TopCommitteeTenderTransaction.RequireLockAsync(db,tenderId,ct);var url=$"/TopCommittee/TenderDetails/{tenderId}";
  foreach(var recipient in recipients.Distinct())
   if(!db.Notifications.Local.Any(n=>n.UserId==recipient&&n.Title==title&&n.RelativeUrl==url)&&!await db.Notifications.AnyAsync(n=>n.UserId==recipient&&n.Title==title&&n.RelativeUrl==url,ct))db.Notifications.Add(new(){UserId=recipient,Title=title,Message="Open the tender to review its current committee status.",RelativeUrl=url,IsRead=false,CreatedAt=DateTime.UtcNow});
 }
 internal static async Task DecisionAsync(ApplicationDbContext db,CommitteeVotingContext context,CancellationToken ct)
 {
  if (context.Access.Tender!.Status == "AwaitingManagerReview")
  {
   var isEditRequest = context.Access.Tender.TopEditState == "WithManager";
   foreach (var manager in context.Access.Managers.Where(m => m.IsActive && m.HasManagerRole))
    db.Notifications.Add(new Notification { UserId = manager.UserId,
     Title = isEditRequest ? "Top Committee requested edits on tender" : "Top Committee rejected tender",
     Message = isEditRequest
      ? $"CEO & Chiefs requested edits/clarification on Tender #{context.Access.Tender.TenderId}. Answer directly or push back to the Sourcing Officer."
      : $"Tender #{context.Access.Tender.TenderId} was completely rejected. Review the reason and forward to its officer.",
     RelativeUrl = $"/SourcingManager/Submissions?rejectedTenderId={context.Access.Tender.TenderId}&showRejections=true", CreatedAt = DateTime.UtcNow });
  }
  var tender=context.Access.Tender!;if(tender.Status is "Awarded" or "AwaitingManagerReview")await AddAsync(db,tender.TenderId,context.Access.Members.Where(m=>m.IsEligible).Select(m=>m.UserId),$"Tender #{tender.TenderId}: {(tender.Status=="Awarded"?"awarded":tender.TopEditState=="WithManager"?"returned to Manager for edits":"rejected and returned to Manager")}",ct);
  if (tender.Status == "Awarded" && tender.AwardedBidId.HasValue)
  {
   var offers = await db.Bids.Where(b => b.TenderId == tender.TenderId && b.SubmittedAt.HasValue && b.Status != "Draft" && b.Status != "Withdrawn")
    .Include(b => b.Invitation).ThenInclude(i => i.SupplierProfile).ToListAsync(ct);
   foreach (var offer in offers)
   {
    var won = offer.BidId == tender.AwardedBidId;
    offer.Status = won ? "Accepted" : "Rejected";
    var recipient = offer.Invitation.SupplierProfile.UserId;
    var url = $"/Supplier/Offer/{offer.BidId}";
    var title = won ? "Your offer was selected" : "Your offer was not selected";
    if (!db.Notifications.Local.Any(n => n.UserId == recipient && n.RelativeUrl == url && n.Title == title)
     && !await db.Notifications.AnyAsync(n => n.UserId == recipient && n.RelativeUrl == url && n.Title == title, ct))
     db.Notifications.Add(new Notification { UserId = recipient, Title = title, RelativeUrl = url,
      Message = won ? $"Your offer #{offer.BidId} for {tender.Title} won the tender." : $"The tender {tender.Title} has been awarded. Your offer #{offer.BidId} was not selected.",
      IsRead = false, CreatedAt = DateTime.UtcNow });
   }
   // Reuse the locked award context and the caller's SaveChanges/commit. Never broadcast by role.
   var request = await db.PurchaseRequests.AsNoTracking()
       .SingleOrDefaultAsync(r => r.PurchaseRequestId == tender.PurchaseRequestId, ct);
   var candidates = RelatedAwardCandidates(tender, context.Access.Managers, request);
   var ids = candidates.Select(c => c.UserId).Distinct().ToArray();
   var accounts = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id))
       .Select(u => new AwardRecipientAccount(u.Id, u.IsActive, u.DepartmentId)).ToListAsync(ct);
   var roles = await (from userRole in db.UserRoles.AsNoTracking()
       join role in db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
       where ids.Contains(userRole.UserId)
       select new { userRole.UserId, Role = role.Name! }).ToListAsync(ct);
   var existingRecipients = context.Access.Members.Where(m => m.IsEligible).Select(m => m.UserId)
       .Concat(offers.Select(b => b.Invitation.SupplierProfile.UserId)).ToHashSet(StringComparer.Ordinal);
   var notices = RelatedAwardNotices(tender, candidates, accounts,
       roles.Select(r => (r.UserId, r.Role)), existingRecipients);
   foreach (var notice in notices)
       if (!db.Notifications.Local.Any(n => SameNotice(n, notice)) &&
           !await db.Notifications.AnyAsync(n => n.UserId == notice.UserId && n.Title == notice.Title && n.RelativeUrl == notice.RelativeUrl, ct))
           db.Notifications.Add(notice);
  }
 }
 internal sealed record AwardRecipientCandidate(string UserId, string Role, string Url, string Message, int? DepartmentId = null);
 internal sealed record AwardRecipientAccount(string UserId, bool IsActive, int? DepartmentId);

 internal static IReadOnlyList<AwardRecipientCandidate> RelatedAwardCandidates(Tender tender,
     IReadOnlyList<Models.TopCommittee.CommitteeManager> managers, PurchaseRequest? request)
 {
  var recipients = new List<AwardRecipientCandidate>();
  if (tender.Status != "Awarded" || !tender.AwardedBidId.HasValue || !tender.AwardedAt.HasValue) return recipients;
  if (managers.Count == 1 && managers[0].IsActive && managers[0].HasManagerRole)
      recipients.Add(new(managers[0].UserId, Data.RoleNames.SourcingManager, "/SourcingManager/ChosenOffers",
          $"Tender #{tender.TenderId} has been awarded. Review the award outcome in Chosen Offers."));
  if (!string.IsNullOrWhiteSpace(tender.CreatedByUserId))
      recipients.Add(new(tender.CreatedByUserId, Data.RoleNames.SourcingOfficer, $"/SourcingOfficer/Tenders?id={tender.TenderId}",
          $"Tender #{tender.TenderId} has been awarded. Review the selected offer in your tender workspace."));
  if (request != null && request.PurchaseRequestId == tender.PurchaseRequestId && !string.IsNullOrWhiteSpace(request.RequestedByUserId))
      recipients.Add(new(request.RequestedByUserId, Data.RoleNames.Department, $"/Department/Details/{request.PurchaseRequestId}",
          $"Tender #{tender.TenderId} for your purchase request #{request.PurchaseRequestId} has been awarded. View your request for reference.", request.DepartmentId));
  return recipients;
 }

 internal static IReadOnlyList<Notification> RelatedAwardNotices(Tender tender,
     IEnumerable<AwardRecipientCandidate> candidates, IEnumerable<AwardRecipientAccount> accounts,
     IEnumerable<(string UserId, string Role)> roles, IEnumerable<string> existingRecipients)
 {
  var result = new List<Notification>();
  if (tender.Status != "Awarded" || !tender.AwardedBidId.HasValue || !tender.AwardedAt.HasValue) return result;
  var seen = existingRecipients.ToHashSet(StringComparer.Ordinal);
  var active = accounts.Where(a => a.IsActive).ToDictionary(a => a.UserId, StringComparer.Ordinal);
  var authorized = roles.ToHashSet();
  foreach (var candidate in candidates)
  {
   if (!active.TryGetValue(candidate.UserId, out var account) || !authorized.Contains((candidate.UserId,candidate.Role)) ||
       candidate.DepartmentId.HasValue && account.DepartmentId != candidate.DepartmentId) continue;
   if (seen.Add(candidate.UserId)) result.Add(new Notification { UserId=candidate.UserId,
       Title=$"Tender #{tender.TenderId}: awarded", Message=candidate.Message, RelativeUrl=candidate.Url,
       IsRead=false, CreatedAt=DateTime.UtcNow });
  }
  return result;
 }
 internal static bool SameNotice(Notification left, Notification right) =>
     left.UserId == right.UserId && left.Title == right.Title && left.RelativeUrl == right.RelativeUrl;
 internal static async Task ClarificationsAsync(ApplicationDbContext db,CommitteeClarificationContext context,CancellationToken ct)
 {
  db.ChangeTracker.DetectChanges();
  foreach(var entry in db.ChangeTracker.Entries<Clarification>().Where(e=>e.State==EntityState.Modified&&e.Property(c=>c.Status).IsModified).ToList()){
   var q=entry.Entity;if(!TopCommitteeClarificationScope.Contains(q,context))continue;
   if(q.Status=="Answered"&&entry.Property(c=>c.Status).OriginalValue=="Pending")await AddAsync(db,q.TenderId!.Value,[q.SenderUserId],$"Tender #{q.TenderId}: clarification #{q.ClarificationId} answered",ct);
   if(q.Status=="Resolved"&&entry.Property(c=>c.Status).OriginalValue=="Answered"&&TopCommitteeClarificationScope.OpenCount(context)==0)await AddAsync(db,q.TenderId!.Value,context.Members.Where(m=>m.IsEligible).Select(m=>m.UserId),$"Tender #{q.TenderId}: clarification #{q.ClarificationId} resolved",ct);
  }
 }
 // Manager team calls after persisting a valid referral, using its authenticated principal.
}

public static class TopCommitteeServiceCollectionExtensions
{
    public static IServiceCollection AddTopCommitteeFoundation(this IServiceCollection services)
    {
        services.AddScoped<TopCommitteeAccessService>();
        services.AddScoped<TopCommitteeReports>();
        services.AddScoped<TopCommitteeHistory>();
        services.AddScoped<TopCommitteeNotifications>();
        services.AddSingleton<TopCommitteeReportExport>();
        services.AddScoped<ITopCommitteeQueries, TopCommitteeQueryService>();
        services.AddScoped<ITopCommitteeTenderTransaction, TopCommitteeTenderTransaction>();
        services.AddScoped<ITopCommitteeVotingPause, TopCommitteeVotingPause>();
        services.AddScoped<ITopCommitteeClarificationStore, TopCommitteeClarificationStore>();
        services.AddScoped<TopCommitteeClarificationService>();
        services.AddScoped<ITopCommitteeClarifications>(provider => provider.GetRequiredService<TopCommitteeClarificationService>());
        services.AddScoped<ITopCommitteeManagerAnswers>(provider => provider.GetRequiredService<TopCommitteeClarificationService>());
        services.AddScoped<ITopCommitteeClarificationQueries, TopCommitteeClarificationQueries>();
        services.AddScoped<ITopCommitteeVotingStore, TopCommitteeVotingStore>();
        services.AddScoped<ITopCommitteeVoting, TopCommitteeVotingService>();
        services.AddScoped<ITopCommitteeVotingReadiness, TopCommitteeVotingDatabase>();
        return services;
    }
}
